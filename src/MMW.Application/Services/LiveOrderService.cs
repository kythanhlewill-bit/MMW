using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MMW.Application.Interfaces;
using MMW.Application.MarketData;
using MMW.Application.MarketData.Models;
using MMW.Application.Models;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;

namespace MMW.Application.Services;

/// <summary>
/// Thực thi đặt lệnh thật (USDT-M Futures) với nhiều lớp chặn an toàn.
/// </summary>
public class LiveOrderService : ILiveOrderService
{
    private readonly IBaseRepository<Trade> _trades;
    private readonly IBaseRepository<TradingAccount> _accounts;
    private readonly IBaseRepository<Flag> _flags;
    private readonly IExchangeOrderProviderFactory _orderFactory;
    private readonly INotificationService _notifications;
    private readonly ILlmService _llm;
    private readonly ITradeWorkflowService _workflow;
    private readonly ISettingsService _settings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly LiveTradingOptions _options;
    private readonly ILogger<LiveOrderService> _logger;

    public LiveOrderService(
        IBaseRepository<Trade> trades,
        IBaseRepository<TradingAccount> accounts,
        IBaseRepository<Flag> flags,
        IExchangeOrderProviderFactory orderFactory,
        INotificationService notifications,
        ILlmService llm,
        ITradeWorkflowService workflow,
        ISettingsService settings,
        IUnitOfWork unitOfWork,
        IOptions<LiveTradingOptions> options,
        ILogger<LiveOrderService> logger)
    {
        _trades = trades;
        _accounts = accounts;
        _flags = flags;
        _orderFactory = orderFactory;
        _notifications = notifications;
        _llm = llm;
        _workflow = workflow;
        _settings = settings;
        _unitOfWork = unitOfWork;
        _options = options.Value;
        _logger = logger;
    }

    public async Task PlaceForTradeAsync(long tradeId, CancellationToken cancellationToken = default)
    {
        // (0) Công tắc tổng — tắt thì không bao giờ chạm sàn.
        if (!_options.Enabled)
        {
            _logger.LogInformation("Live trading tắt (Enabled=false) — bỏ qua trade {TradeId}.", tradeId);
            return;
        }

        var trade = await _trades.FindAsync(tradeId);
        if (trade is null) return;

        // (1) Idempotency — đã gửi rồi thì thôi.
        if (trade.IsLive)
        {
            _logger.LogInformation("Trade {TradeId} đã IsLive — bỏ qua (idempotent).", tradeId);
            return;
        }

        // (1b) Chỉ gửi lệnh Open. Planned/Closed/Cancelled không bắn lên sàn.
        if (trade.Status != TradeStatus.Open)
        {
            _logger.LogInformation("Trade {TradeId} status {Status} (không phải Open) — không gửi sàn.", tradeId, trade.Status);
            return;
        }

        var account = await _accounts.FindAsync(trade.TradingAccountId);
        if (account is null || string.IsNullOrWhiteSpace(account.ApiKey) || string.IsNullOrWhiteSpace(account.ApiSecret))
        {
            await BlockAsync(trade, account, "Tài khoản chưa có API key trading.", cancellationToken);
            return;
        }

        // (1c) Bắt buộc AI: không cấu hình LLM thì KHÔNG đặt lệnh thật.
        if (!_llm.IsConfigured)
        {
            await BlockAsync(trade, account, "AI chưa cấu hình — bắt buộc AI mới được đặt lệnh thật.", cancellationToken);
            return;
        }

        // Tạo provider sớm để vừa dedup vị thế thật vừa chuẩn hoá/đặt lệnh.
        var provider = _orderFactory.Create(account.ApiKey!, account.ApiSecret!, _options.UseTestnet);

        // (1d) Bỏ qua nếu TRÙNG TƯƠNG ĐỐI: (a) lệnh hệ thống Open cùng symbol+hướng+giá xấp xỉ,
        //      hoặc (b) vị thế THẬT trên Binance cùng symbol+hướng.
        var openSame = await _trades.FindListAsync(t =>
            t.Id != tradeId && t.TradingAccountId == account.Id && t.Symbol == trade.Symbol
            && t.Direction == trade.Direction && t.Status == TradeStatus.Open);
        if (openSame.Any(t => TradeDuplication.IsNearPrice(t.EntryPrice, trade.EntryPrice)))
        {
            await BlockAsync(trade, account, $"Đã có lệnh {trade.Direction} {trade.Symbol} giá ~{trade.EntryPrice} đang mở — bỏ qua trùng.", cancellationToken);
            return;
        }
        try
        {
            var positions = await provider.GetOpenPositionsAsync(trade.Symbol, cancellationToken);
            var dupOnExchange = positions.Any(pos =>
                string.Equals(pos.Symbol, trade.Symbol, StringComparison.OrdinalIgnoreCase)
                && (trade.Direction == TradeDirection.Long ? pos.IsLong : pos.IsShort));
            if (dupOnExchange)
            {
                await BlockAsync(trade, account, $"Đã có vị thế {trade.Direction} {trade.Symbol} trên Binance — bỏ qua trùng.", cancellationToken);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không kiểm tra được vị thế Binance cho {Symbol}, dùng dedup DB.", trade.Symbol);
        }

        if (trade.EntryPrice <= 0m)
        {
            await BlockAsync(trade, account, "Giá vào không hợp lệ.", cancellationToken);
            return;
        }

        // (1e) Bắt buộc SL & TP — không có hoặc phía sai thì chặn ngay, không vào sàn.
        var slValid = trade.StopLoss is decimal slCheck && slCheck > 0m
            && (trade.Direction == TradeDirection.Long ? slCheck < trade.EntryPrice : slCheck > trade.EntryPrice);
        var tpValid = trade.TakeProfit is decimal tpCheck && tpCheck > 0m
            && (trade.Direction == TradeDirection.Long ? tpCheck > trade.EntryPrice : tpCheck < trade.EntryPrice);

        if (!slValid)
        {
            var slReason = trade.StopLoss is null or <= 0m
                ? "Thiếu giá Stop Loss."
                : $"Stop Loss {trade.StopLoss} không hợp lệ — {(trade.Direction == TradeDirection.Long ? "Long yêu cầu SL < Entry" : "Short yêu cầu SL > Entry")} ({trade.EntryPrice}).";
            _logger.LogWarning("Trade {TradeId}: chặn — {Reason}", tradeId, slReason);
            await BlockAsync(trade, account, slReason, cancellationToken);
            return;
        }

        if (!tpValid)
        {
            var tpReason = trade.TakeProfit is null or <= 0m
                ? "Thiếu giá Take Profit."
                : $"Take Profit {trade.TakeProfit} không hợp lệ — {(trade.Direction == TradeDirection.Long ? "Long yêu cầu TP > Entry" : "Short yêu cầu TP < Entry")} ({trade.EntryPrice}).";
            _logger.LogWarning("Trade {TradeId}: chặn — {Reason}", tradeId, tpReason);
            await BlockAsync(trade, account, tpReason, cancellationToken);
            return;
        }

        // Cờ "mặc kệ rủi ro": bỏ qua các rào RỦI RO (cap đòn bẩy, cap notional, giới hạn lệnh/ngày, rule Critical).
        // VẪN giữ rào KỸ THUẬT: min-size + min-notional của sàn + chống trùng vị thế.
        var appSetting = await _settings.GetAppSettingAsync(cancellationToken);
        var overrideRisk = appSetting.AllowOverrideRisk;

        // (2) Cap đòn bẩy.
        var defaultLeverage = _options.DefaultLeverage <= 0 ? 20 : _options.DefaultLeverage;
        var leverage = (int)Math.Round(trade.Leverage ?? defaultLeverage, MidpointRounding.AwayFromZero);
        if (leverage < 1) leverage = 1;
        if (leverage > _options.MaxLeverage)
        {
            if (!overrideRisk)
            {
                await BlockAsync(trade, account, $"Đòn bẩy {leverage}x vượt cap {_options.MaxLeverage}x.", cancellationToken);
                return;
            }
            _logger.LogWarning("Trade {TradeId}: đòn bẩy {Lev}x vượt cap {Max}x nhưng BỎ QUA do AllowOverrideRisk.", tradeId, leverage, _options.MaxLeverage);
        }

        // (2b) Chuẩn hoá khối lượng theo precision + ÉP LÊN min sàn nếu size quá nhỏ.
        decimal effectiveQty;
        try
        {
            effectiveQty = await provider.NormalizeQuantityForNotionalAsync(
                trade.Symbol,
                trade.Quantity,
                trade.EntryPrice,
                _options.MinOrderNotionalUsdt,
                cancellationToken);
        }
        catch (Exception ex)
        {
            await BlockAsync(trade, account, $"Không lấy được quy tắc khối lượng của {trade.Symbol}: {Truncate(ex.Message, 200)}", cancellationToken);
            return;
        }
        if (effectiveQty <= 0m)
        {
            await BlockAsync(trade, account, $"Không xác định được khối lượng hợp lệ cho {trade.Symbol}.", cancellationToken);
            return;
        }

        // Nếu bị ép tăng (size theo rủi ro < min sàn) → cập nhật qty + CHẤM LẠI rule với qty mới.
        if (effectiveQty != trade.Quantity)
        {
            _logger.LogInformation("Trade {TradeId}: khối lượng {Old} → ép lên min sàn/notional {New}, chấm lại rule.", tradeId, trade.Quantity, effectiveQty);
            trade.Quantity = effectiveQty;
            _trades.Update(trade);
            await _unitOfWork.CommitAsync(cancellationToken);
            await _workflow.ProcessTradeAsync(tradeId, cancellationToken);
            trade = await _trades.FindAsync(tradeId) ?? trade;
        }

        // (2c) Cap notional theo qty thật.
        var notional = trade.EntryPrice * effectiveQty;
        if (_options.MinOrderNotionalUsdt > 0m && notional < _options.MinOrderNotionalUsdt)
        {
            await BlockAsync(trade, account, $"Notional {notional:N2} USDT nhỏ hơn mức tối thiểu {_options.MinOrderNotionalUsdt:N2} USDT của Binance Futures.", cancellationToken);
            return;
        }

        if (notional > _options.MaxNotionalUsdt)
        {
            if (!overrideRisk)
            {
                await BlockAsync(trade, account, $"Notional {notional:N2} (khối lượng {effectiveQty}) vượt cap {_options.MaxNotionalUsdt:N2} USDT. Tăng MaxNotionalUsdt hoặc chọn symbol giá thấp hơn.", cancellationToken);
                return;
            }
            _logger.LogWarning("Trade {TradeId}: notional {N:N2} vượt cap {Max:N2} nhưng BỎ QUA do AllowOverrideRisk.", tradeId, notional, _options.MaxNotionalUsdt);
        }

        var sinceMidnight = DateTime.UtcNow.Date;
        var todayLive = await _trades.FindListAsync(t =>
            t.TradingAccountId == account.Id && t.IsLive && t.CreatedDate >= sinceMidnight);
        if (todayLive.Count >= _options.MaxOrdersPerDay && !overrideRisk)
        {
            await BlockAsync(trade, account, $"Đã đạt giới hạn {_options.MaxOrdersPerDay} lệnh live/ngày.", cancellationToken);
            return;
        }

        // (3) Rule gate — chấm theo qty THẬT; có Critical thì KHÔNG gửi, TRỪ khi bật cờ override.
        var criticals = await _flags.FindListAsync(f =>
            f.TradeId == tradeId && f.Severity == FlagSeverity.Critical);
        if (criticals.Count > 0)
        {
            var reasons = string.Join("; ", criticals.Select(c => c.Message).Take(3));
            if (!overrideRisk)
            {
                await BlockAsync(trade, account, $"Vi phạm rule nghiêm trọng: {reasons}", cancellationToken);
                return;
            }
            _logger.LogWarning("Trade {TradeId}: BỎ QUA rule Critical do bật AllowOverrideRisk. {Reasons}", tradeId, reasons);
        }

        // (4) Gửi entry. Lỗi entry = CHƯA vào sàn → huỷ lệnh hệ thống (1-1).
        var side = trade.Direction == TradeDirection.Long ? OrderSide.Buy : OrderSide.Sell;
        var closeSide = side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy;
        // Phía vị thế (Hedge Mode bắt buộc); entry và SL/TP đều thuộc cùng vị thế theo hướng trade.
        var positionSide = trade.Direction == TradeDirection.Long ? FuturesPositionSide.Long : FuturesPositionSide.Short;
        var clientId = $"mmw-{tradeId}";
        ExchangeOrderResult result;
        try
        {
            await provider.SetLeverageAsync(trade.Symbol, leverage, cancellationToken);
            result = await provider.PlaceFuturesOrderAsync(new FuturesOrderRequest
            {
                Symbol = trade.Symbol,
                Side = side,
                Kind = trade.OrderType == OrderType.Market ? FuturesOrderKind.Market : FuturesOrderKind.Limit,
                Quantity = effectiveQty,
                Price = trade.OrderType == OrderType.Market ? null : trade.EntryPrice,
                PositionSide = positionSide,
                NewClientOrderId = clientId,
                // GTX = post-only: sàn TỪ CHỐI lệnh nếu nó cắt qua sổ và khớp thành taker.
                // Cổng chi phí đã chấm phiếu này theo phí maker, nên một cú khớp taker âm thầm
                // sẽ làm mọi con số kinh tế của phiếu thành sai. Thà bị từ chối và không vào.
                TimeInForce = trade.OrderType == OrderType.Market ? "GTC" : "GTX",
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Đặt entry lỗi cho trade {TradeId} {Symbol} — huỷ lệnh hệ thống.", tradeId, trade.Symbol);
            trade.LiveStatus = LiveOrderStatus.Error;
            trade.Status = TradeStatus.Cancelled;
            trade.LiveNote = Truncate(ex.Message, 500);
            _trades.Update(trade);
            await _unitOfWork.CommitAsync(cancellationToken);
            await NotifyAsync(account, NotificationSeverity.Critical,
                $"Lỗi đặt lệnh #{tradeId} (đã huỷ)", Truncate(ex.Message, 200), trade.Symbol, cancellationToken);
            return;
        }

        // Lệnh chờ chưa khớp thì CHƯA có vị thế nào để bảo vệ — dừng ở đây, để job đối soát đặt
        // SL/TP ngay khi nó khớp, hoặc huỷ khi hết hạn. Xem chú thích LiveOrderStatus.EntryPending.
        if (trade.OrderType == OrderType.Limit)
        {
            trade.IsLive = true;
            trade.Source = TradeSource.Api;
            trade.LiveStatus = LiveOrderStatus.EntryPending;
            trade.ExchangeOrderId = result.OrderId;
            trade.ExchangeClientOrderId = result.ClientOrderId ?? clientId;
            trade.ExternalId ??= result.OrderId;
            trade.LiveNote = $"Lệnh chờ maker đặt lúc {DateTime.UtcNow:HH:mm:ss} UTC — chờ khớp.";
            _trades.Update(trade);
            await _unitOfWork.CommitAsync(cancellationToken);

            await NotifyAsync(account, NotificationSeverity.Info,
                $"Đặt lệnh chờ {(_options.UseTestnet ? "TESTNET" : "THẬT")} #{tradeId} · {trade.Symbol} {trade.Direction}",
                $"Chờ khớp tại {trade.EntryPrice} · qty {effectiveQty}, đòn bẩy {leverage}x. "
                + "SL/TP sẽ đặt ngay khi khớp.",
                trade.Symbol, cancellationToken);
            return;
        }

        // Entry đã vào → đặt SL/TP. Lỗi SL/TP KHÔNG huỷ vị thế (vị thế đã tồn tại thật).
        // Retry ngay 3 lần (delay 500ms); nếu vẫn fail → đánh dấu SltpPending để job retry sau.
        var sltpNote = "";
        try
        {
            await PlaceProtectiveSetAsync(provider, trade, clientId, cancellationToken);
        }
        catch (Exception ex)
        {
            sltpNote = " · SL/TP lỗi sau 3 lần retry: " + Truncate(ex.Message, 120);
            _logger.LogWarning(ex, "Đặt SL/TP lỗi sau 3 lần retry cho trade {TradeId} — đánh dấu SltpPending.", tradeId);
            trade.LiveStatus = LiveOrderStatus.SltpPending;
            trade.IsLive = true;
            trade.Source = TradeSource.Api;
            trade.ExchangeOrderId = result.OrderId;
            trade.ExchangeClientOrderId = result.ClientOrderId ?? clientId;
            trade.ExternalId ??= result.OrderId;
            trade.LiveNote = $"Entry vào sàn lúc {DateTime.UtcNow:HH:mm:ss} UTC nhưng SL/TP chưa đặt được — job sẽ retry.{sltpNote}";
            _trades.Update(trade);
            await _unitOfWork.CommitAsync(cancellationToken);
            await NotifyAsync(account, NotificationSeverity.Warning,
                $"SL/TP chưa đặt được #{tradeId}", $"{trade.Symbol} — sẽ tự retry. {sltpNote}", trade.Symbol, cancellationToken);
            return;
        }

        trade.IsLive = true;
        trade.LiveStatus = LiveOrderStatus.Submitted;
        trade.Source = TradeSource.Api;     
        trade.ExchangeOrderId = result.OrderId;
        trade.ExchangeClientOrderId = result.ClientOrderId ?? clientId;
        trade.ExternalId ??= result.OrderId;   // để trade-result-sync khớp fills
        trade.LiveNote = $"Gửi sàn {(_options.UseTestnet ? "TESTNET" : "LIVE")} lúc {DateTime.UtcNow:HH:mm:ss} UTC (status {result.Status}).{sltpNote}";
        _trades.Update(trade);
        await _unitOfWork.CommitAsync(cancellationToken);

        // Ghi thẳng dừng lỗ và chốt lời vào thông báo. Trước đây chỉ có giá vào, nên muốn biết
        // lệnh đang rủi ro bao nhiêu phải mở web — mà thời điểm cần biết điều đó chính là lúc
        // nhận thông báo. Rủi ro tính theo % để đọc được ngay không cần nhẩm.
        var stopText = trade.StopLoss is { } stopPrice
            ? $"{stopPrice}"
              + (trade.EntryPrice > 0m
                  ? $" ({Math.Abs(trade.EntryPrice - stopPrice) / trade.EntryPrice * 100m:N2}%)"
                  : string.Empty)
            : "CHƯA ĐẶT";
        var targetText = trade.TakeProfit is { } targetPrice ? $"{targetPrice}" : "CHƯA ĐẶT";

        await NotifyAsync(account, NotificationSeverity.Info,
            $"Vào lệnh {(_options.UseTestnet ? "TESTNET" : "THẬT")} #{tradeId} · {trade.Symbol} {trade.Direction}",
            $"Vào {trade.EntryPrice} · Dừng lỗ {stopText} · Chốt lời {targetText}"
            + $" · qty {effectiveQty}, đòn bẩy {leverage}x.{sltpNote}",
            trade.Symbol, cancellationToken);
    }

    public async Task SyncLevelsAsync(long tradeId, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return;
        var trade = await _trades.FindAsync(tradeId);
        if (trade is null || !trade.IsLive || trade.Status != TradeStatus.Open) return;

        var account = await _accounts.FindAsync(trade.TradingAccountId);
        if (account is null || string.IsNullOrWhiteSpace(account.ApiKey) || string.IsNullOrWhiteSpace(account.ApiSecret)) return;

        try
        {
            var provider = _orderFactory.Create(account.ApiKey!, account.ApiSecret!, _options.UseTestnet);
            // Huỷ SL/TP chờ cũ rồi đặt lại theo giá mới (entry đã khớp, không đổi).
            await provider.CancelAllOpenOrdersAsync(trade.Symbol, cancellationToken);

            await PlaceProtectiveSetAsync(provider, trade, $"mmw-{tradeId}-s{DateTime.UtcNow:HHmmss}", cancellationToken);
            _trades.Update(trade);
            await _unitOfWork.CommitAsync(cancellationToken);

            _logger.LogInformation("Đã đồng bộ SL/TP lệnh #{TradeId} lên sàn.", tradeId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Đồng bộ SL/TP lên sàn lỗi cho trade {TradeId}.", tradeId);
            await NotifyAsync(account, NotificationSeverity.Warning,
                $"Lỗi đồng bộ SL/TP #{tradeId}", Truncate(ex.Message, 200), trade.Symbol, cancellationToken);
        }
    }

    public async Task CloseOnExchangeAsync(long tradeId, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return;
        var trade = await _trades.FindAsync(tradeId);
        if (trade is null || !trade.IsLive) return;

        var account = await _accounts.FindAsync(trade.TradingAccountId);
        if (account is null || string.IsNullOrWhiteSpace(account.ApiKey) || string.IsNullOrWhiteSpace(account.ApiSecret)) return;

        try
        {
            var provider = _orderFactory.Create(account.ApiKey!, account.ApiSecret!, _options.UseTestnet);
            await provider.CancelAllOpenOrdersAsync(trade.Symbol, cancellationToken); // huỷ SL/TP chờ
            await provider.ClosePositionAsync(trade.Symbol, cancellationToken);        // đóng vị thế MARKET reduceOnly
            _logger.LogInformation("Đã đóng vị thế #{TradeId} {Symbol} trên sàn.", tradeId, trade.Symbol);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Đóng vị thế trên sàn lỗi cho trade {TradeId}.", tradeId);
            await NotifyAsync(account, NotificationSeverity.Warning,
                $"Lỗi đóng vị thế #{tradeId} trên sàn", Truncate(ex.Message, 200), trade.Symbol, cancellationToken);
        }
    }

    /// <summary>
    /// Đặt một lệnh bảo vệ. Không truyền <paramref name="quantity"/> thì lệnh đóng TOÀN BỘ vị
    /// thế còn lại; truyền vào thì chỉ đóng đúng phần đó.
    /// </summary>
    /// <remarks>
    /// Hai chế độ này loại trừ nhau ở phía sàn: <c>closePosition</c> không đi cùng
    /// <c>quantity</c> hay <c>reduceOnly</c>. Đó cũng là lý do dừng lỗ luôn dùng chế độ đóng
    /// toàn bộ — sau khi chốt phần đầu, phần còn lại bao nhiêu thì dừng lỗ vẫn phủ hết bấy
    /// nhiêu, không cần ai đi sửa khối lượng của nó.
    /// </remarks>
    private static async Task TryPlaceProtectiveAsync(
        IExchangeOrderProvider provider, string symbol, OrderSide side, FuturesPositionSide positionSide,
        FuturesOrderKind kind, decimal stopPrice, string clientId, CancellationToken ct,
        decimal? quantity = null)
    {
        await provider.PlaceFuturesOrderAsync(new FuturesOrderRequest
        {
            Symbol = symbol,
            Side = side,
            Kind = kind,
            StopPrice = stopPrice,
            Quantity = quantity,
            ClosePosition = quantity is null,
            ReduceOnly = quantity is not null,
            PositionSide = positionSide,
            NewClientOrderId = clientId,
        }, ct);
    }

    /// <summary>
    /// Đặt trọn bộ lệnh bảo vệ cho một lệnh: dừng lỗ, chốt phần đầu (nếu có), chốt phần cuối.
    /// </summary>
    /// <remarks>
    /// Gom về một chỗ vì bốn đường khác nhau cùng cần nó — vào lệnh thị trường, lệnh chờ vừa
    /// khớp, đồng bộ lại mức, và job retry. Trước đây mỗi đường tự viết lấy hai lệnh SL/TP, nên
    /// khi thêm mục tiêu thứ hai sẽ phải sửa đúng bốn nơi và chỉ cần bỏ sót một nơi là có lệnh
    /// chạy với nửa bộ bảo vệ.
    ///
    /// Thứ tự đặt là dừng lỗ TRƯỚC. Nếu mạng đứt giữa chừng thì thứ còn thiếu là một mục tiêu
    /// chốt lời chứ không phải cái phanh.
    /// </remarks>
    private async Task PlaceProtectiveSetAsync(
        IExchangeOrderProvider provider, Trade trade, string clientId, CancellationToken ct)
    {
        var closeSide = trade.Direction == TradeDirection.Long ? OrderSide.Sell : OrderSide.Buy;
        var positionSide = trade.Direction == TradeDirection.Long ? FuturesPositionSide.Long : FuturesPositionSide.Short;

        if (trade.StopLoss is decimal sl && sl > 0m)
        {
            await RetryAsync(() => TryPlaceProtectiveAsync(provider, trade.Symbol, closeSide, positionSide,
                FuturesOrderKind.StopMarket, sl, $"{clientId}-sl", ct), 3, 500, ct);
        }
        else
        {
            _logger.LogWarning("Trade {TradeId}: BỎ QUA đặt SL — StopLoss={Sl} (null hoặc <= 0).", trade.Id, trade.StopLoss);
        }

        // Mục tiêu gần chỉ đặt khi lệnh còn nguyên. Đã chốt phần đầu rồi mà đặt lại thì lệnh
        // này sẽ ăn nốt phần runner ngay lần chạm tiếp theo — đúng thứ mà việc giữ runner sinh
        // ra để tránh.
        if (trade.FirstTargetFilledAt is null
            && trade.FirstTakeProfit is decimal tp1 && tp1 > 0m
            && trade.FirstTakeProfitFraction is decimal fraction && fraction is > 0m and < 1m)
        {
            var desired = trade.Quantity * fraction;
            var qty = trade.FirstTakeProfitQuantity
                      ?? await provider.NormalizeQuantityAsync(trade.Symbol, desired, ct);

            // Sàn ép khối lượng lên mức tối thiểu. Nếu phần chốt đầu bị ép bằng cả vị thế thì
            // "chốt một phần" đã biến thành "chốt hết" — bỏ hẳn mục tiêu gần còn trung thực hơn.
            if (qty > 0m && qty < trade.Quantity)
            {
                trade.FirstTakeProfitQuantity = qty;
                await RetryAsync(() => TryPlaceProtectiveAsync(provider, trade.Symbol, closeSide, positionSide,
                    FuturesOrderKind.TakeProfitMarket, tp1, $"{clientId}-tp1", ct, qty), 3, 500, ct);
            }
            else
            {
                _logger.LogWarning(
                    "Trade {TradeId}: bỏ mục tiêu gần — khối lượng {Qty} không nhỏ hơn cả vị thế {Total}.",
                    trade.Id, qty, trade.Quantity);
                trade.FirstTakeProfit = null;
                trade.FirstTakeProfitFraction = null;
            }
        }

        if (trade.TakeProfit is decimal tp && tp > 0m)
        {
            await RetryAsync(() => TryPlaceProtectiveAsync(provider, trade.Symbol, closeSide, positionSide,
                FuturesOrderKind.TakeProfitMarket, tp, $"{clientId}-tp", ct), 3, 500, ct);
        }
        else
        {
            _logger.LogWarning("Trade {TradeId}: BỎ QUA đặt TP — TakeProfit={Tp} (null hoặc <= 0).", trade.Id, trade.TakeProfit);
        }
    }

    private async Task BlockAsync(Trade trade, TradingAccount? account, string reason, CancellationToken ct)
    {
        _logger.LogWarning("Chặn đặt lệnh trade {TradeId}: {Reason}", trade.Id, reason);
        trade.LiveStatus = LiveOrderStatus.Blocked;
        trade.Status = TradeStatus.Cancelled;   // 1-1: không vào sàn → không giữ vị thế Open "ma"
        trade.LiveNote = Truncate(reason, 500);
        _trades.Update(trade);
        await _unitOfWork.CommitAsync(ct);

        if (account is not null)
            await NotifyAsync(account, NotificationSeverity.Warning,
                $"Chặn đặt lệnh #{trade.Id}", reason, trade.Symbol, ct);
    }

    private async Task NotifyAsync(
        TradingAccount account, NotificationSeverity severity,
        string title, string message, string symbol, CancellationToken ct)
    {
        try
        {
            await _notifications.PublishAsync(new NotificationCreateModel
            {
                Type = NotificationType.TradeRiskWarning,
                Severity = severity,
                Title = title,
                Message = message,
                Source = "live_order",
                SourceKey = $"{account.Id}:{title}",
                RelatedSymbol = symbol,
                RelatedUrl = "/Trades",
                ExpiresAt = DateTime.UtcNow.AddHours(12),
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không gửi được notification live-order.");
        }
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max];

    /// <summary>Thời gian tối đa một lệnh chờ maker được nằm trên sổ.</summary>
    /// <remarks>
    /// Quy đổi <see cref="Trading.Execution.TradeExecutionPlanner.LiveLimitExpiryBars"/> sang thời
    /// gian thật theo nến 15 phút của vòng chấm điểm. Hết hạn thì huỷ chứ không gia hạn: phiếu đã
    /// được chấm trên một cây nến cụ thể, và một setup quá một giờ tuổi không còn là setup đó nữa.
    /// </remarks>
    private static readonly TimeSpan LimitEntryLifetime =
        TimeSpan.FromMinutes(15 * Trading.Execution.TradeExecutionPlanner.LiveLimitExpiryBars);

    /// <summary>
    /// Đối soát các lệnh chờ maker đang treo: khớp rồi thì đặt SL/TP, hết hạn thì huỷ.
    /// </summary>
    /// <remarks>
    /// Đây là nửa còn lại của việc vào bằng lệnh chờ. Không có nó, một lệnh chờ không khớp sẽ nằm
    /// <c>Open</c> vĩnh viễn trong bảng mà không có vị thế nào tương ứng: nó ăn mất một suất trong
    /// trần lệnh/ngày, chặn gate chống trùng vị thế của chính symbol đó, và có thể khớp nhiều giờ
    /// sau vào một thị trường đã khác hẳn lúc chấm điểm.
    ///
    /// Phân biệt "đã khớp" với "bị huỷ ngoài hệ thống" bằng VỊ THẾ chứ không bằng việc lệnh biến
    /// mất khỏi sổ: cả hai trường hợp đều làm lệnh rời sổ, nhưng chỉ một trường hợp cần SL/TP.
    /// </remarks>
    private async Task ReconcilePendingEntriesAsync(CancellationToken cancellationToken)
    {
        var waiting = await _trades.FindListAsync(t =>
            t.IsLive && t.LiveStatus == LiveOrderStatus.EntryPending && t.Status == TradeStatus.Open);

        if (waiting.Count == 0) return;

        _logger.LogInformation("ReconcilePendingEntries: {Count} lệnh chờ đang treo.", waiting.Count);

        foreach (var trade in waiting)
        {
            var account = await _accounts.FindAsync(trade.TradingAccountId);
            if (account is null || string.IsNullOrWhiteSpace(account.ApiKey) || string.IsNullOrWhiteSpace(account.ApiSecret))
                continue;

            try
            {
                var provider = _orderFactory.Create(account.ApiKey!, account.ApiSecret!, _options.UseTestnet);

                var openOrders = await provider.GetOpenOrdersAsync(trade.Symbol, cancellationToken);
                var stillOnBook = trade.ExchangeOrderId is { } id
                                  && openOrders.Any(o => o.OrderId == id);

                if (stillOnBook)
                {
                    var age = DateTime.UtcNow - (trade.OpenedAt ?? trade.CreatedDate);
                    if (age < LimitEntryLifetime) continue;

                    await provider.CancelOrderAsync(trade.Symbol, trade.ExchangeOrderId!, cancellationToken);
                    await CloseUnfilledAsync(trade, account,
                        $"Lệnh chờ quá {LimitEntryLifetime.TotalMinutes:N0} phút chưa khớp — đã huỷ.",
                        cancellationToken);
                    continue;
                }

                // Rời sổ rồi: khớp hay bị huỷ? Vị thế mới là bằng chứng.
                var positions = await provider.GetOpenPositionsAsync(trade.Symbol, cancellationToken);
                var filled = positions.Any(p =>
                    p.Symbol == trade.Symbol
                    && (trade.Direction == TradeDirection.Long ? p.IsLong : p.IsShort));

                if (!filled)
                {
                    await CloseUnfilledAsync(trade, account,
                        "Lệnh chờ rời sổ mà không tạo vị thế — coi như đã huỷ ngoài hệ thống.",
                        cancellationToken);
                    continue;
                }

                await ProtectFilledEntryAsync(trade, account, provider, cancellationToken);
            }
            catch (Exception ex)
            {
                // Giữ nguyên EntryPending để vòng sau thử lại. Không tự huỷ khi lỗi mạng: huỷ nhầm
                // một lệnh đã khớp sẽ để lại vị thế không có dừng lỗ, tệ hơn nhiều so với chờ thêm.
                _logger.LogError(ex, "ReconcilePendingEntries: lệnh #{TradeId} {Symbol} lỗi — giữ nguyên.",
                    trade.Id, trade.Symbol);
            }
        }
    }

    /// <summary>Lệnh chờ không thành vị thế: đóng sổ để nó trả lại suất trong trần lệnh/ngày.</summary>
    private async Task CloseUnfilledAsync(
        Trade trade, TradingAccount account, string reason, CancellationToken cancellationToken)
    {
        trade.Status = TradeStatus.Cancelled;
        trade.LiveStatus = LiveOrderStatus.Canceled;
        trade.LiveNote = Truncate($"{reason} ({DateTime.UtcNow:HH:mm:ss} UTC)", 500);
        _trades.Update(trade);
        await _unitOfWork.CommitAsync(cancellationToken);

        _logger.LogInformation("ReconcilePendingEntries: lệnh #{TradeId} {Symbol} — {Reason}",
            trade.Id, trade.Symbol, reason);

        await NotifyAsync(account, NotificationSeverity.Info,
            $"Lệnh chờ #{trade.Id} không khớp · {trade.Symbol} {trade.Direction}",
            reason, trade.Symbol, cancellationToken);
    }

    /// <summary>Lệnh chờ đã khớp: đặt SL/TP ngay, và báo tin như một lệnh vào bình thường.</summary>
    private async Task ProtectFilledEntryAsync(
        Trade trade, TradingAccount account, IExchangeOrderProvider provider, CancellationToken cancellationToken)
    {
        var clientId = $"mmw-{trade.Id}-f{DateTime.UtcNow:HHmmss}";

        try
        {
            await PlaceProtectiveSetAsync(provider, trade, clientId, cancellationToken);
            trade.LiveStatus = LiveOrderStatus.Filled;
            trade.LiveNote = $"Lệnh chờ khớp lúc {DateTime.UtcNow:HH:mm:ss} UTC, SL/TP đã đặt.";
        }
        catch (Exception ex)
        {
            // Vị thế đã tồn tại thật mà chưa có SL — chuyển sang đường retry sẵn có, đừng bỏ lửng.
            _logger.LogWarning(ex, "ReconcilePendingEntries: lệnh #{TradeId} khớp nhưng SL/TP lỗi.", trade.Id);
            trade.LiveStatus = LiveOrderStatus.SltpPending;
            trade.LiveNote = Truncate($"Lệnh chờ đã khớp nhưng SL/TP lỗi: {ex.Message}", 500);
        }

        _trades.Update(trade);
        await _unitOfWork.CommitAsync(cancellationToken);

        var severity = trade.LiveStatus == LiveOrderStatus.Filled
            ? NotificationSeverity.Info
            : NotificationSeverity.Warning;
        var sltpText = trade.LiveStatus == LiveOrderStatus.Filled
            ? $"Dừng lỗ {trade.StopLoss?.ToString() ?? "CHƯA ĐẶT"} · Chốt lời {trade.TakeProfit?.ToString() ?? "CHƯA ĐẶT"}."
            : "SL/TP CHƯA ĐẶT ĐƯỢC — job sẽ retry.";

        await NotifyAsync(account, severity,
            $"Lệnh chờ đã khớp #{trade.Id} · {trade.Symbol} {trade.Direction}",
            $"Vào {trade.EntryPrice} · qty {trade.Quantity}. {sltpText}",
            trade.Symbol, cancellationToken);
    }

    /// <summary>
    /// Quét tất cả lệnh có LiveStatus = SltpPending, thử đặt lại SL/TP lên sàn.
    /// Gọi bởi Hangfire job định kỳ (vd mỗi 2 phút).
    /// </summary>
    public async Task RetryPendingSltpAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return;

        await ReconcilePendingEntriesAsync(cancellationToken);

        var pending = await _trades.FindListAsync(t =>
            t.IsLive && t.LiveStatus == LiveOrderStatus.SltpPending && t.Status == TradeStatus.Open);

        if (pending.Count == 0) return;

        _logger.LogInformation("RetryPendingSltp: tìm thấy {Count} lệnh cần retry SL/TP.", pending.Count);

        foreach (var trade in pending)
        {
            var account = await _accounts.FindAsync(trade.TradingAccountId);
            if (account is null || string.IsNullOrWhiteSpace(account.ApiKey) || string.IsNullOrWhiteSpace(account.ApiSecret))
            {
                _logger.LogWarning("RetryPendingSltp: Trade {TradeId} — tài khoản thiếu API key, bỏ qua.", trade.Id);
                continue;
            }

            try
            {
                _logger.LogInformation("RetryPendingSltp: Trade {TradeId} {Symbol} — thử đặt lại SL/TP.", trade.Id, trade.Symbol);
                var provider = _orderFactory.Create(account.ApiKey!, account.ApiSecret!, _options.UseTestnet);

                // Huỷ lệnh chờ cũ (nếu có lệnh SL/TP nửa vời) rồi đặt lại sạch.
                await provider.CancelAllOpenOrdersAsync(trade.Symbol, cancellationToken);

                await PlaceProtectiveSetAsync(
                    provider, trade, $"mmw-{trade.Id}-r{DateTime.UtcNow:HHmmss}", cancellationToken);

                trade.LiveStatus = LiveOrderStatus.Submitted;
                trade.LiveNote = $"SL/TP đặt lại thành công lúc {DateTime.UtcNow:HH:mm:ss} UTC.";
                _trades.Update(trade);
                await _unitOfWork.CommitAsync(cancellationToken);

                _logger.LogInformation("RetryPendingSltp: Trade {TradeId} — đặt lại SL/TP thành công.", trade.Id);
                await NotifyAsync(account, NotificationSeverity.Info,
                    $"SL/TP đặt lại thành công #{trade.Id}",
                    $"{trade.Symbol} SL={trade.StopLoss} TP={trade.TakeProfit}", trade.Symbol, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RetryPendingSltp: Trade {TradeId} — vẫn lỗi sau retry, giữ SltpPending.", trade.Id);
                trade.LiveNote = $"Retry SL/TP lúc {DateTime.UtcNow:HH:mm:ss} UTC vẫn lỗi: {Truncate(ex.Message, 200)}";
                _trades.Update(trade);
                await _unitOfWork.CommitAsync(cancellationToken);
                await NotifyAsync(account, NotificationSeverity.Critical,
                    $"SL/TP vẫn lỗi #{trade.Id}",
                    $"{trade.Symbol} — kiểm tra thủ công ngay! {Truncate(ex.Message, 150)}", trade.Symbol, cancellationToken);
            }
        }
    }

    /// <summary>Retry <paramref name="maxAttempts"/> lần với delay <paramref name="delayMs"/>ms giữa các lần.</summary>
    private static async Task RetryAsync(Func<Task> action, int maxAttempts, int delayMs, CancellationToken ct)
    {
        var attempt = 0;
        while (true)
        {
            try
            {
                await action();
                return;
            }
            catch when (++attempt < maxAttempts)
            {
                await Task.Delay(delayMs, ct);
            }
        }
    }
}
