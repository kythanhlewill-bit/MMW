using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.Json;
using MMW.Application.MarketData;
using MMW.Application.MarketData.Models;
using MMW.Domain.Entities;
using MMW.Shared.Interfaces;

namespace MMW.Infrastructure.Exchanges.Binance;

/// <summary>
/// Đặt lệnh USDT-M Futures bằng API key có quyền trading (ký HMAC-SHA256, POST).
/// Tự làm tròn khối lượng/giá về precision (stepSize/tickSize) của từng symbol để tránh lỗi -1111.
/// </summary>
public class BinanceFuturesOrderProvider : IExchangeOrderProvider
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _apiSecret;
    private readonly IBaseRepository<ExchangeApiAuditRecord> _apiAudits;
    private readonly IUnitOfWork _unitOfWork;

    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Precision theo symbol ít đổi → cache tĩnh dùng chung mọi instance.
    private static readonly ConcurrentDictionary<string, SymbolFilter> FilterCache = new();

    /// <summary>
    /// Bộ theo dõi lệnh cấm IP dùng chung. Xem <see cref="BinanceIpBanTracker"/> cho lý do.
    /// </summary>
    private static readonly BinanceIpBanTracker BanTracker = BinanceIpBanTracker.Shared;

    // Position Mode (Hedge vs One-way) gắn theo TÀI KHOẢN → cache theo instance (mỗi instance 1 API key).
    private bool? _isHedgeMode;
    private readonly SemaphoreSlim _hedgeGate = new(1, 1);

    public BinanceFuturesOrderProvider(
        HttpClient http,
        string apiKey,
        string apiSecret,
        IBaseRepository<ExchangeApiAuditRecord> apiAudits,
        IUnitOfWork unitOfWork)
    {
        _http = http;
        _apiKey = apiKey;
        _apiSecret = apiSecret;
        _apiAudits = apiAudits;
        _unitOfWork = unitOfWork;
    }

    public async Task<ExchangeOrderResult> PlaceFuturesOrderAsync(FuturesOrderRequest req, CancellationToken cancellationToken = default)
    {
        var (p, isConditional) = await BuildOrderParamsAsync(req, cancellationToken);

        using var doc = await SignedSendAsync(
            HttpMethod.Post, isConditional ? "/fapi/v1/algoOrder" : "/fapi/v1/order", p, cancellationToken);
        var root = doc.RootElement;

        // Algo trả về algoId/clientAlgoId/algoStatus thay cho orderId/clientOrderId/status. Gộp về
        // một hình dạng để tầng trên không phải biết lệnh nằm ở dịch vụ nào.
        var orderId = root.TryGetProperty(isConditional ? "algoId" : "orderId", out var oid)
            ? oid.GetRawText().Trim('"') : "";
        var clientId = root.TryGetProperty(isConditional ? "clientAlgoId" : "clientOrderId", out var cid)
            ? cid.GetString() : null;
        var status = root.TryGetProperty(isConditional ? "algoStatus" : "status", out var st)
            ? st.GetString() ?? "" : "";
        return new ExchangeOrderResult(orderId, clientId, status);
    }

    /// <summary>
    /// Gửi lệnh vào endpoint KIỂM TRA của sàn: sàn xác thực đầy đủ rồi trả về mà không đặt gì.
    /// </summary>
    /// <remarks>
    /// Tồn tại vì lỗi định dạng lệnh chỉ lộ ra ở đúng khoảnh khắc đặt lệnh thật, và khoảnh khắc
    /// đó không lặp lại theo ý muốn — ngày 18/08/2026 bốn lệnh đầu tiên của hệ thống bị bác
    /// -1111 vì bộ lọc precision lấy nhầm mã, và không có cách nào phát hiện trước.
    ///
    /// Đi qua ĐÚNG đường dựng tham số của lệnh thật (<c>BuildOrderParamsAsync</c>), nếu không thì
    /// nó chỉ kiểm được chính nó. Lệnh điều kiện KHÔNG kiểm được: Algo Service không có endpoint
    /// tương ứng, nên hàm trả về ghi chú thay vì im lặng báo thành công.
    /// </remarks>
    public async Task<string> ValidateFuturesOrderAsync(
        FuturesOrderRequest req, CancellationToken cancellationToken = default)
    {
        var (p, isConditional) = await BuildOrderParamsAsync(req, cancellationToken);

        if (isConditional)
            return "BỎ QUA: Algo Service không có endpoint kiểm thử cho lệnh điều kiện.";

        using var _ = await SignedSendAsync(HttpMethod.Post, "/fapi/v1/order/test", p, cancellationToken);
        return "OK: " + string.Join(", ", p
            .Where(x => x.Key is "symbol" or "side" or "type" or "quantity" or "price" or "timeInForce" or "positionSide")
            .Select(x => x.Key + "=" + x.Value));
    }

    /// <summary>Dựng tham số cho một lệnh. Dùng chung giữa đặt thật và tiền kiểm.</summary>
    private async Task<(List<KeyValuePair<string, string>> Params, bool IsConditional)> BuildOrderParamsAsync(
        FuturesOrderRequest req, CancellationToken cancellationToken)
    {
        var symbol = req.Symbol.ToUpperInvariant();
        var filter = await GetFiltersAsync(symbol, cancellationToken);
        var hedge = await IsHedgeModeAsync(cancellationToken);

        // Không có bộ lọc thì DỪNG, không gửi lệnh với định dạng đoán. Trước đây thiếu bộ lọc sẽ
        // rơi về "tối đa 12 số lẻ" và sàn bác -1111 — một lỗi trông như sự cố mạng nhất thời trong
        // khi thực chất là hệ thống không biết mã này có bao nhiêu số lẻ. Chết sớm và nói rõ.
        if (filter is null)
            throw new InvalidOperationException(
                $"Không lấy được bộ lọc precision của {symbol} từ sàn — không gửi lệnh để tránh bị bác -1111.");

        var p = new List<KeyValuePair<string, string>>
        {
            new("symbol", symbol),
            new("side", req.Side == OrderSide.Buy ? "BUY" : "SELL"),
            new("type", MapType(req.Kind)),
        };

        // Hedge Mode: bắt buộc positionSide LONG/SHORT. One-way: KHÔNG gửi (mặc định BOTH).
        if (hedge)
        {
            var posSide = req.PositionSide switch
            {
                FuturesPositionSide.Long => "LONG",
                FuturesPositionSide.Short => "SHORT",
                // Chưa chỉ định rõ phía: suy từ side (chỉ đúng cho lệnh MỞ, không phải lệnh đóng).
                _ => req.Side == OrderSide.Buy ? "LONG" : "SHORT",
            };
            p.Add(new("positionSide", posSide));
        }

        // Từ 2025-12-09 Binance chuyển lệnh điều kiện sang Algo Service. Gửi STOP_MARKET /
        // TAKE_PROFIT_MARKET vào /fapi/v1/order bị chặn thẳng bằng -4120 STOP_ORDER_SWITCH_ALGO.
        // Endpoint khác thì tên tham số cũng khác: stopPrice → triggerPrice, newClientOrderId →
        // clientAlgoId, và phải khai algoType=CONDITIONAL.
        var isConditional = req.Kind is FuturesOrderKind.StopMarket or FuturesOrderKind.TakeProfitMarket;
        if (isConditional) p.Insert(0, new("algoType", "CONDITIONAL"));

        if (req.Kind == FuturesOrderKind.Limit)
        {
            p.Add(new("timeInForce", req.TimeInForce));
            if (req.Price is decimal price) p.Add(new("price", FmtPrice(price, filter)));
        }

        if (req.StopPrice is decimal stop)
            p.Add(new(isConditional ? "triggerPrice" : "stopPrice", FmtPrice(stop, filter)));

        if (req.ClosePosition)
        {
            p.Add(new("closePosition", "true"));
        }
        else if (req.Quantity is decimal qty)
        {
            var snapped = SnapQuantity(qty, filter);
            if (snapped <= 0m)
                throw new InvalidOperationException(
                    $"Khối lượng {qty} sau khi làm tròn theo stepSize {filter?.StepSize} = 0 (nhỏ hơn mức tối thiểu của {symbol}).");
            p.Add(new("quantity", FmtWithDecimals(snapped, filter?.QtyDecimals ?? 8)));
        }

        // reduceOnly KHÔNG hợp lệ ở Hedge Mode (positionSide đã đủ phân biệt). Chỉ gửi ở One-way.
        if (!hedge && req.ReduceOnly && !req.ClosePosition)
            p.Add(new("reduceOnly", "true"));

        if (!string.IsNullOrWhiteSpace(req.NewClientOrderId))
            p.Add(new(isConditional ? "clientAlgoId" : "newClientOrderId", req.NewClientOrderId!));

        return (p, isConditional);
    }

    public async Task SetLeverageAsync(string symbol, int leverage, CancellationToken cancellationToken = default)
    {
        var p = new List<KeyValuePair<string, string>>
        {
            new("symbol", symbol.ToUpperInvariant()),
            new("leverage", leverage.ToString(CultureInfo.InvariantCulture)),
        };
        using var _ = await SignedSendAsync(HttpMethod.Post, "/fapi/v1/leverage", p, cancellationToken);
    }

    public async Task CancelOrderAsync(string symbol, string orderId, CancellationToken cancellationToken = default)
    {
        var p = new List<KeyValuePair<string, string>>
        {
            new("symbol", symbol.ToUpperInvariant()),
            new("orderId", orderId),
        };
        using var _ = await SignedSendAsync(HttpMethod.Delete, "/fapi/v1/order", p, cancellationToken);
    }

    public async Task<IReadOnlyList<ExchangePosition>> GetOpenPositionsAsync(string? symbol = null, CancellationToken cancellationToken = default)
    {
        var p = new List<KeyValuePair<string, string>>();
        if (!string.IsNullOrWhiteSpace(symbol)) p.Add(new("symbol", symbol.ToUpperInvariant()));

        using var doc = await SignedSendAsync(HttpMethod.Get, "/fapi/v2/positionRisk", p, cancellationToken);
        var list = new List<ExchangePosition>();
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var amt = ParseDec(e.TryGetProperty("positionAmt", out var pa) ? pa.GetString() : "0");
                if (amt == 0m) continue;
                var sym = e.TryGetProperty("symbol", out var s) ? s.GetString() ?? "" : "";
                var entry = ParseDec(e.TryGetProperty("entryPrice", out var ep) ? ep.GetString() : "0");
                DateTime? updatedAtUtc = null;
                if (e.TryGetProperty("updateTime", out var updateTime)
                    && updateTime.TryGetInt64(out var updateTimeMs)
                    && updateTimeMs > 0)
                {
                    updatedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(updateTimeMs).UtcDateTime;
                }

                list.Add(new ExchangePosition(sym, amt, entry, updatedAtUtc));
            }
        }
        return list;
    }

    public async Task<IReadOnlyList<ExchangeOpenOrder>> GetOpenOrdersAsync(string? symbol = null, CancellationToken cancellationToken = default)
    {
        var p = new List<KeyValuePair<string, string>>();
        if (!string.IsNullOrWhiteSpace(symbol)) p.Add(new("symbol", symbol.ToUpperInvariant()));

        using var doc = await SignedSendAsync(HttpMethod.Get, "/fapi/v1/openOrders", p, cancellationToken);
        var list = new List<ExchangeOpenOrder>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;

        foreach (var e in doc.RootElement.EnumerateArray())
        {
            string Str(string name) => e.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
            decimal Dec(string name) => e.TryGetProperty(name, out var v) ? ParseDec(v.GetString()) : 0m;
            bool Bool(string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

            var side = string.Equals(Str("side"), "BUY", StringComparison.OrdinalIgnoreCase) ? OrderSide.Buy : OrderSide.Sell;
            var timeMs = e.TryGetProperty("time", out var t) && t.TryGetInt64(out var ms) ? ms : 0L;

            list.Add(new ExchangeOpenOrder(
                Str("symbol"),
                e.TryGetProperty("orderId", out var oid) ? oid.GetRawText().Trim('"') : "",
                Str("clientOrderId"),
                side,
                Str("type"),
                Str("positionSide"),
                Dec("price"),
                Dec("stopPrice"),
                Dec("origQty"),
                Dec("executedQty"),
                Bool("reduceOnly"),
                Bool("closePosition"),
                DateTimeOffset.FromUnixTimeMilliseconds(timeMs).UtcDateTime));
        }

        // Lệnh điều kiện (SL/TP) nằm ở Algo Service từ 2025-12-09 và KHÔNG xuất hiện trong
        // /fapi/v1/openOrders. Thiếu vế này thì bảng "lệnh chờ trên sàn" hiện vị thế trần trụi
        // không SL — đúng cái mà người xem đang tìm cách kiểm tra.
        try
        {
            list.AddRange(await GetOpenConditionalOrdersAsync(symbol, cancellationToken));
        }
        catch (InvalidOperationException)
        {
            // Nuốt có chủ ý Ở ĐÂY, không phải bên trong: danh sách gộp thường vẫn có giá trị dù
            // vế algo hỏng, còn ném ra sẽ làm mất luôn cả hai. Nhưng ai cần BIẾT mình đọc hụt —
            // ví dụ lượt đối chiếu vị thế — thì gọi thẳng hàm dưới và tự bắt lỗi.
            //
            // Không mất dấu vết: SignedSendAsync đã ghi request/response vào bảng audit trước khi ném.
        }

        return list;
    }

    /// <summary>
    /// Chỉ sổ lệnh ĐIỀU KIỆN (SL/TP). Ném khi không đọc được thay vì trả danh sách rỗng.
    /// </summary>
    /// <remarks>
    /// Tách khỏi <see cref="GetOpenOrdersAsync"/> vì hai người gọi cần hai hành vi trái ngược.
    /// Đường giao dịch muốn best-effort. Đường đối chiếu thì KHÔNG: ở đó "đọc được, không có lệnh
    /// bảo vệ nào" và "không đọc được sổ" dẫn tới hai kết luận ngược nhau về một vị thế đang mở,
    /// và gộp chúng vào cùng một danh sách rỗng là cách chắc chắn nhất để báo an toàn cho một vị
    /// thế trần trụi — đúng lúc câu trả lời đó nguy hiểm nhất.
    /// </remarks>
    public async Task<IReadOnlyList<ExchangeOpenOrder>> GetOpenConditionalOrdersAsync(
        string? symbol = null, CancellationToken cancellationToken = default)
    {
        var p = new List<KeyValuePair<string, string>>();
        if (!string.IsNullOrWhiteSpace(symbol)) p.Add(new("symbol", symbol.ToUpperInvariant()));

        var list = new List<ExchangeOpenOrder>();

        using var doc = await SignedSendAsync(HttpMethod.Get, "/fapi/v1/openAlgoOrders", p, cancellationToken);
        var root = doc.RootElement;

        // Endpoint trả mảng trần hoặc bọc trong { "orders": [...] } tuỳ phiên bản — nhận cả hai.
        var array = root.ValueKind == JsonValueKind.Array
            ? root
            : root.TryGetProperty("orders", out var wrapped) && wrapped.ValueKind == JsonValueKind.Array
                ? wrapped
                : default;
        if (array.ValueKind != JsonValueKind.Array) return list;

        foreach (var e in array.EnumerateArray())
        {
            string Str(string name) => e.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
            decimal Dec(string name) => e.TryGetProperty(name, out var v) ? ParseDec(v.GetString()) : 0m;
            bool Bool(string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

            var side = string.Equals(Str("side"), "BUY", StringComparison.OrdinalIgnoreCase) ? OrderSide.Buy : OrderSide.Sell;
            var timeMs = e.TryGetProperty("createTime", out var t) && t.TryGetInt64(out var ms) ? ms : 0L;

            list.Add(new ExchangeOpenOrder(
                Str("symbol"),
                e.TryGetProperty("algoId", out var aid) ? aid.GetRawText().Trim('"') : "",
                Str("clientAlgoId"),
                side,
                Str("orderType"),
                Str("positionSide"),
                Dec("price"),
                Dec("triggerPrice"),
                Dec("quantity"),
                0m,
                Bool("reduceOnly"),
                Bool("closePosition"),
                DateTimeOffset.FromUnixTimeMilliseconds(timeMs).UtcDateTime));
        }

        return list;
    }

    /// <summary>
    /// Huỷ mọi lệnh chờ của một mã, gồm cả lệnh điều kiện.
    /// </summary>
    /// <remarks>
    /// Hai endpoint chứ không phải một: từ 2025-12-09 lệnh điều kiện nằm ở Algo Service, và
    /// <c>/fapi/v1/allOpenOrders</c> KHÔNG chạm tới chúng. Gọi thiếu vế algo thì SL cũ vẫn treo
    /// trên sàn sau khi vị thế đã đóng — lần vào lệnh sau nó tự kích hoạt và mở vị thế ngược.
    ///
    /// Lỗi ở vế này không được chặn vế kia chạy, vì đúng cái vế bị bỏ sót mới là cái nguy hiểm.
    /// </remarks>
    public async Task CancelAllOpenOrdersAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var p = new List<KeyValuePair<string, string>> { new("symbol", symbol.ToUpperInvariant()) };
        var failures = new List<Exception>();

        foreach (var path in new[] { "/fapi/v1/allOpenOrders", "/fapi/v1/algoOpenOrders" })
        {
            try
            {
                using var _ = await SignedSendAsync(HttpMethod.Delete, path, p, cancellationToken);
            }
            catch (Exception ex)
            {
                failures.Add(new InvalidOperationException($"Huỷ lệnh chờ qua {path} lỗi: {ex.Message}", ex));
            }
        }

        if (failures.Count > 0) throw new AggregateException(failures);
    }

    public async Task ClosePositionAsync(string symbol, CancellationToken cancellationToken = default)
    {
        // Hedge Mode có thể tồn tại đồng thời cả Long lẫn Short cùng symbol → đóng từng phía.
        var positions = await GetOpenPositionsAsync(symbol, cancellationToken);
        foreach (var pos in positions.Where(x =>
                     string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase) && x.PositionAmt != 0m))
        {
            await PlaceFuturesOrderAsync(new FuturesOrderRequest
            {
                Symbol = symbol,
                Side = pos.PositionAmt > 0m ? OrderSide.Sell : OrderSide.Buy, // đóng Long bằng Sell và ngược lại
                Kind = FuturesOrderKind.Market,
                Quantity = Math.Abs(pos.PositionAmt),
                PositionSide = pos.PositionAmt > 0m ? FuturesPositionSide.Long : FuturesPositionSide.Short,
                ReduceOnly = true,
            }, cancellationToken);
        }
    }

    public async Task<decimal> NormalizeQuantityAsync(string symbol, decimal desiredQty, CancellationToken cancellationToken = default)
    {
        var filter = await GetFiltersAsync(symbol.ToUpperInvariant(), cancellationToken);
        var snapped = SnapQuantity(desiredQty, filter);
        // Ép lên min sàn nếu nhỏ hơn (caller sẽ chấm lại rule với qty này).
        if (filter is { MinQty: > 0m } && snapped < filter.MinQty)
            snapped = filter.MinQty;
        return snapped;
    }

    public async Task<decimal> NormalizeQuantityForNotionalAsync(
        string symbol,
        decimal desiredQty,
        decimal entryPrice,
        decimal minNotionalUsdt,
        CancellationToken cancellationToken = default)
    {
        var filter = await GetFiltersAsync(symbol.ToUpperInvariant(), cancellationToken);
        var snapped = SnapQuantity(desiredQty, filter);
        if (filter is { MinQty: > 0m } && snapped < filter.MinQty)
            snapped = filter.MinQty;

        if (entryPrice <= 0m || minNotionalUsdt <= 0m)
            return snapped;

        var minQtyByNotional = minNotionalUsdt / entryPrice;
        var snappedMinQty = SnapQuantityUp(minQtyByNotional, filter);
        if (filter is { MinQty: > 0m } && snappedMinQty < filter.MinQty)
            snappedMinQty = filter.MinQty;

        return snapped < snappedMinQty ? snappedMinQty : snapped;
    }

    /// <summary>
    /// Tài khoản đang ở Hedge Mode (dualSidePosition=true) hay One-way? Hỏi sàn 1 lần rồi cache theo instance.
    /// Lỗi mạng → coi như One-way (an toàn, giữ hành vi cũ).
    /// </summary>
    private async Task<bool> IsHedgeModeAsync(CancellationToken ct)
    {
        if (_isHedgeMode is bool cached) return cached;

        await _hedgeGate.WaitAsync(ct);
        try
        {
            if (_isHedgeMode is bool c) return c;
            try
            {
                using var doc = await SignedSendAsync(
                    HttpMethod.Get, "/fapi/v1/positionSide/dual", new List<KeyValuePair<string, string>>(), ct);
                _isHedgeMode = doc.RootElement.TryGetProperty("dualSidePosition", out var d) && d.GetBoolean();
            }
            catch
            {
                _isHedgeMode = false; // không xác định được → mặc định One-way
            }
            return _isHedgeMode.Value;
        }
        finally
        {
            _hedgeGate.Release();
        }
    }

    // --- Precision (stepSize / tickSize / minQty) ---

    private sealed record SymbolFilter(decimal StepSize, int QtyDecimals, decimal TickSize, int PriceDecimals, decimal MinQty);

    private async Task<SymbolFilter?> GetFiltersAsync(string symbol, CancellationToken ct)
    {
        if (FilterCache.TryGetValue(symbol, out var cached)) return cached;
        try
        {
            using var resp = await _http.GetAsync($"/fapi/v1/exchangeInfo?symbol={symbol}", ct);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("symbols", out var symbols) || symbols.GetArrayLength() == 0)
                return null;

            // PHẢI tìm đúng symbol trong mảng. /fapi/v1/exchangeInfo BỎ QUA tham số ?symbol=
            // và luôn trả về toàn bộ danh sách, nên symbols[0] luôn là BTCUSDT bất kể hỏi mã nào.
            // Hậu quả đo được ngày 18/08/2026: bốn lệnh ETHUSDT đầu tiên bị sàn bác -1111 vì khối
            // lượng làm tròn theo stepSize 0,0001 của BTC trong khi ETH chỉ nhận 0,001.
            JsonElement? target = null;
            foreach (var s in symbols.EnumerateArray())
            {
                if (s.TryGetProperty("symbol", out var name)
                    && string.Equals(name.GetString(), symbol, StringComparison.OrdinalIgnoreCase))
                {
                    target = s;
                    break;
                }
            }

            // Không thấy mã ⟹ trả null để bên gọi dùng định dạng chung, KHÔNG mượn bộ lọc của mã
            // khác: một bộ lọc sai còn nguy hiểm hơn không có bộ lọc, vì nó trông như đã xử lý.
            if (target is not { } symbolNode) return null;

            decimal step = 0m, tick = 0m, minQty = 0m;
            foreach (var f in symbolNode.GetProperty("filters").EnumerateArray())
            {
                var type = f.GetProperty("filterType").GetString();
                if (type == "LOT_SIZE")
                {
                    if (f.TryGetProperty("stepSize", out var ss)) step = ParseDec(ss.GetString());
                    if (f.TryGetProperty("minQty", out var mq)) minQty = ParseDec(mq.GetString());
                }
                else if (type == "PRICE_FILTER" && f.TryGetProperty("tickSize", out var ts))
                {
                    tick = ParseDec(ts.GetString());
                }
            }

            var filter = new SymbolFilter(step, DecimalsOf(step), tick, DecimalsOf(tick), minQty);
            FilterCache[symbol] = filter;
            return filter;
        }
        catch
        {
            return null; // không lấy được filter → fallback định dạng chung (best effort)
        }
    }

    /// <summary>Làm tròn XUỐNG khối lượng về bội số stepSize (không vượt rủi ro dự kiến).</summary>
    private static decimal SnapQuantity(decimal qty, SymbolFilter? f)
    {
        if (f is not { StepSize: > 0m }) return qty;
        var snapped = Math.Floor(qty / f.StepSize) * f.StepSize;
        return Math.Round(snapped, f.QtyDecimals, MidpointRounding.AwayFromZero);
    }

    /// <summary>Làm tròn LÊN khối lượng về bội số stepSize để đạt min notional.</summary>
    private static decimal SnapQuantityUp(decimal qty, SymbolFilter? f)
    {
        if (f is not { StepSize: > 0m }) return qty;
        var snapped = Math.Ceiling(qty / f.StepSize) * f.StepSize;
        return Math.Round(snapped, f.QtyDecimals, MidpointRounding.AwayFromZero);
    }

    /// <summary>Làm tròn giá về bội số tickSize (gần nhất).</summary>
    private static decimal SnapPrice(decimal price, SymbolFilter? f)
    {
        if (f is not { TickSize: > 0m }) return price;
        var snapped = Math.Round(price / f.TickSize, MidpointRounding.AwayFromZero) * f.TickSize;
        return Math.Round(snapped, f.PriceDecimals, MidpointRounding.AwayFromZero);
    }

    private static string FmtPrice(decimal price, SymbolFilter? f) =>
        f is { TickSize: > 0m } ? FmtWithDecimals(SnapPrice(price, f), f.PriceDecimals) : FmtGeneral(price);

    private static string FmtWithDecimals(decimal v, int decimals) =>
        v.ToString("F" + Math.Clamp(decimals, 0, 18), CultureInfo.InvariantCulture);

    private static string FmtGeneral(decimal d) => d.ToString("0.############", CultureInfo.InvariantCulture);

    private static decimal ParseDec(string? s) =>
        decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;

    /// <summary>Số chữ số thập phân của step/tick (vd 0.001 → 3, 0.10 → 1, 1 → 0).</summary>
    private static int DecimalsOf(decimal step)
    {
        if (step <= 0m) return 0;
        var s = step.ToString(CultureInfo.InvariantCulture);
        var dot = s.IndexOf('.');
        if (dot < 0) return 0;
        return s.TrimEnd('0').Length - dot - 1;
    }

    private async Task<JsonDocument> SignedSendAsync(
        HttpMethod method, string path, List<KeyValuePair<string, string>> p, CancellationToken ct)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        p.Add(new("recvWindow", "5000"));
        p.Add(new("timestamp", timestamp.ToString(CultureInfo.InvariantCulture)));

        var query = string.Join("&", p.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        var signature = BinanceSigner.Sign(query, _apiSecret);
        var url = $"{path}?{query}&signature={signature}";

        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-MBX-APIKEY", _apiKey);

        // Đang trong lệnh cấm thì KHÔNG gửi. Gửi lúc này chỉ có hai kết cục, cả hai đều xấu: bị
        // từ chối, và lệnh cấm bị nới dài thêm. Ném ra để người gọi thấy — nuốt lặng sẽ biến một
        // lệnh cấm thành "không có gì xảy ra", mà ở đây "không có gì xảy ra" nghĩa là dừng lỗ
        // không được cập nhật.
        if (BanTracker.IsBanned(DateTimeOffset.UtcNow, out var remaining))
        {
            throw new InvalidOperationException(
                $"Binance đang cấm IP tới {BanTracker.BannedUntil!.Value.UtcDateTime:HH:mm:ss} UTC "
                + $"(còn {remaining.TotalMinutes:N1} phút) — bỏ qua {method} {path} thay vì gọi để khỏi nới dài lệnh cấm.");
        }

        var requestedAt = DateTime.UtcNow;
        try
        {
            using var response = await _http.SendAsync(request, ct);
            var respondedAt = DateTime.UtcNow;
            var body = await response.Content.ReadAsStringAsync(ct);

            await AuditAsync(method, path, p, response, body, requestedAt, respondedAt, ct);

            if (!response.IsSuccessStatusCode)
            {
                BanTracker.Note((int)response.StatusCode, body, DateTimeOffset.UtcNow);
                throw new InvalidOperationException($"Binance order lỗi ({(int)response.StatusCode}): {body}");
            }

            return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            await AuditFailureAsync(method, path, p, requestedAt, DateTime.UtcNow, ex, ct);
            throw;
        }
    }

    private async Task AuditAsync(
        HttpMethod method,
        string path,
        IReadOnlyList<KeyValuePair<string, string>> parameters,
        HttpResponseMessage response,
        string body,
        DateTime requestedAt,
        DateTime respondedAt,
        CancellationToken ct)
    {
        try
        {
            var parameterMap = parameters
                .GroupBy(p => p.Key)
                .ToDictionary(
                    g => g.Key,
                    g => Redact(g.Key, g.Last().Value),
                    StringComparer.OrdinalIgnoreCase);

            var symbol = parameterMap.TryGetValue("symbol", out var s) ? s : null;
            var clientOrderId = parameterMap.TryGetValue("newClientOrderId", out var cid) ? cid : null;

            await _apiAudits.AddAsync(new ExchangeApiAuditRecord
            {
                Exchange = "Binance",
                Symbol = symbol,
                Method = method.Method,
                Path = path,
                ClientOrderId = clientOrderId,
                RequestedAtUtc = requestedAt,
                RespondedAtUtc = respondedAt,
                DurationMs = (int)Math.Max(0, (respondedAt - requestedAt).TotalMilliseconds),
                StatusCode = (int)response.StatusCode,
                Succeeded = response.IsSuccessStatusCode,
                RequestJson = JsonSerializer.Serialize(new
                {
                    baseUrl = _http.BaseAddress?.ToString(),
                    path,
                    method = method.Method,
                    parameters = parameterMap,
                    headers = new
                    {
                        xMbxApiKey = MaskKey(_apiKey),
                    },
                }, AuditJsonOptions),
                ResponseJson = Truncate(body, 4000),
                Error = response.IsSuccessStatusCode ? null : Truncate(body, 500),
            });

            await _unitOfWork.CommitAsync(ct);
        }
        catch
        {
            // Audit không được làm hỏng luồng đặt lệnh thật.
        }
    }

    private async Task AuditFailureAsync(
        HttpMethod method,
        string path,
        IReadOnlyList<KeyValuePair<string, string>> parameters,
        DateTime requestedAt,
        DateTime failedAt,
        Exception ex,
        CancellationToken ct)
    {
        try
        {
            var parameterMap = parameters
                .GroupBy(p => p.Key)
                .ToDictionary(
                    g => g.Key,
                    g => Redact(g.Key, g.Last().Value),
                    StringComparer.OrdinalIgnoreCase);

            var symbol = parameterMap.TryGetValue("symbol", out var s) ? s : null;
            var clientOrderId = parameterMap.TryGetValue("newClientOrderId", out var cid) ? cid : null;

            await _apiAudits.AddAsync(new ExchangeApiAuditRecord
            {
                Exchange = "Binance",
                Symbol = symbol,
                Method = method.Method,
                Path = path,
                ClientOrderId = clientOrderId,
                RequestedAtUtc = requestedAt,
                RespondedAtUtc = failedAt,
                DurationMs = (int)Math.Max(0, (failedAt - requestedAt).TotalMilliseconds),
                Succeeded = false,
                RequestJson = JsonSerializer.Serialize(new
                {
                    baseUrl = _http.BaseAddress?.ToString(),
                    path,
                    method = method.Method,
                    parameters = parameterMap,
                    headers = new
                    {
                        xMbxApiKey = MaskKey(_apiKey),
                    },
                }, AuditJsonOptions),
                Error = Truncate(ex.Message, 500),
            });

            await _unitOfWork.CommitAsync(ct);
        }
        catch
        {
            // Audit không được làm hỏng luồng đặt lệnh thật.
        }
    }

    private static string Redact(string key, string value)
    {
        if (key.Equals("signature", StringComparison.OrdinalIgnoreCase))
            return "***redacted***";
        if (key.Contains("secret", StringComparison.OrdinalIgnoreCase) || key.Contains("apiKey", StringComparison.OrdinalIgnoreCase))
            return "***redacted***";
        return value;
    }

    private static string MaskKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        if (value.Length <= 8)
            return "***";
        return value[..4] + "***" + value[^4..];
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
            return value ?? "";
        return value[..max];
    }

    private static string MapType(FuturesOrderKind kind) => kind switch
    {
        FuturesOrderKind.Market => "MARKET",
        FuturesOrderKind.Limit => "LIMIT",
        FuturesOrderKind.StopMarket => "STOP_MARKET",
        FuturesOrderKind.TakeProfitMarket => "TAKE_PROFIT_MARKET",
        _ => "MARKET",
    };
}
