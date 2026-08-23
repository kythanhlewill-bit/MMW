using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MMW.Application.Interfaces;
using MMW.Application.MarketData;
using MMW.Application.Models;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;
using MMW.Web.Models;

namespace MMW.Web.Controllers;

public class TradesController : Controller
{
    private readonly ITradeService _tradeService;
    private readonly IBaseRepository<TradeAnalysis> _analyses;
    private readonly IBaseRepository<Trade> _trades;
    private readonly IBaseRepository<TradingAccount> _accounts;
    private readonly IBaseRepository<Strategy> _strategies;
    private readonly IBaseRepository<TradeSignal> _signals;
    private readonly IBaseRepository<WatchItem> _watchItems;
    private readonly ISettingsService _settings;
    private readonly IMarketDataProvider _marketData;
    private readonly ITradePreflightAnalysisService _preflightAnalysis;
    private readonly ISymbolSearchService _symbolSearch;
    private readonly ILiveOrderService _liveOrders;
    private readonly ITradeResultSyncService _tradeSync;
    private readonly IExchangeOrderProviderFactory _orderProviderFactory;
    private readonly ILiveBalanceService _liveBalance;
    private readonly IUnitOfWork _unitOfWork;
    private readonly LiveTradingOptions _liveTradingOptions;
    private readonly IMemoryCache _cache;

    /// <summary>
    /// Bảng lệnh chờ trên sàn giữ lại bao lâu giữa hai lần mở trang.
    /// </summary>
    /// <remarks>
    /// Không phải tối ưu cho vui. <c>GET /fapi/v1/openOrders</c> KHÔNG kèm symbol có trọng số 40
    /// trên hạn mức IP, và từ khi phải hỏi thêm lệnh điều kiện thì mỗi lần mở trang tốn gấp đôi.
    /// Bấm F5 vài lần trong lúc theo dõi là đủ ăn -1003 Too many requests — đã gặp thật khi chạy
    /// thử. Hạn mức đó dùng CHUNG với lời gọi đặt lệnh, nên một trang xem không được phép tiêu
    /// vào phần của việc vào lệnh.
    ///
    /// 15 giây là khoảng nhìn thấy được nhưng không đáng kể: lệnh chờ đổi khi có người đặt hoặc
    /// huỷ, mà cả hai đường đó đều đi qua ứng dụng này.
    /// </remarks>
    private static readonly TimeSpan OpenOrdersCacheTtl = TimeSpan.FromSeconds(15);

    public TradesController(
        ITradeService tradeService,
        IBaseRepository<TradeAnalysis> analyses,
        IBaseRepository<Trade> trades,
        IBaseRepository<TradingAccount> accounts,
        IBaseRepository<Strategy> strategies,
        IBaseRepository<TradeSignal> signals,
        IBaseRepository<WatchItem> watchItems,
        ISettingsService settings,
        IMarketDataProvider marketData,
        ITradePreflightAnalysisService preflightAnalysis,
        ISymbolSearchService symbolSearch,
        ILiveOrderService liveOrders,
        ITradeResultSyncService tradeSync,
        IExchangeOrderProviderFactory orderProviderFactory,
        ILiveBalanceService liveBalance,
        IUnitOfWork unitOfWork,
        IOptions<LiveTradingOptions> liveTradingOptions,
        IMemoryCache cache)
    {
        _cache = cache;
        _tradeService = tradeService;
        _analyses = analyses;
        _trades = trades;
        _accounts = accounts;
        _strategies = strategies;
        _signals = signals;
        _watchItems = watchItems;
        _settings = settings;
        _marketData = marketData;
        _preflightAnalysis = preflightAnalysis;
        _symbolSearch = symbolSearch;
        _liveOrders = liveOrders;
        _tradeSync = tradeSync;
        _orderProviderFactory = orderProviderFactory;
        _liveBalance = liveBalance;
        _unitOfWork = unitOfWork;
        _liveTradingOptions = liveTradingOptions.Value;
    }

    /// <param name="style">
    /// Lọc theo nhóm lệnh. Bỏ trống là xem tất cả.
    /// </param>
    /// <remarks>
    /// Thống kê ở đầu trang luôn tính trên TOÀN BỘ sổ chứ không tính trên trang đang xem hay
    /// trên nhóm đang lọc — người xem cần thấy hai nhóm cạnh nhau để so, và một con số đổi theo
    /// bộ lọc thì không so được với chính nó của lần xem trước.
    /// </remarks>
    public async Task<IActionResult> Index(
        int page = 1, int pageSize = 20, TradeStyle? style = null, CancellationToken cancellationToken = default)
    {
        var all = await _tradeService.GetAllAsync();
        var filtered = style is { } s ? all.Where(t => t.Style == s).ToList() : all;

        var pager = PagerModel.Build(page, pageSize, filtered.Count);
        var trades = filtered
            .Skip((pager.CurrentPage - 1) * pager.PageSize)
            .Take(pager.PageSize)
            .ToList();

        var openTradeIds = trades.Where(t => t.Status == TradeStatus.Open).Select(t => t.Id).ToList();
        var analysisList = await _analyses.FindListAsync(a => openTradeIds.Contains(a.TradeId));
        var analysisMap = analysisList.ToDictionary(a => a.TradeId);

        ViewBag.Pager = pager;
        var vm = new TradeJournalViewModel
        {
            Trades = trades,
            Analyses = analysisMap,
            StyleFilter = style,
            StyleStats = TradeStyleStats.Split(all),
        };

        // Lệnh chờ trên sàn chỉ hiển thị ở trang đầu (đọc live, tránh làm chậm khi lật trang sâu).
        if (pager.CurrentPage == 1)
        {
            var (orders, loaded) = await LoadOpenOrdersAsync(cancellationToken);
            vm.OpenOrders = orders;
            vm.OpenOrdersLoaded = loaded;
        }

        return View(vm);
    }

    /// <summary>Huỷ 1 lệnh chờ trên sàn theo (accountId, symbol, orderId). Không động DB.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelOpenOrder(long accountId, string symbol, string orderId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(orderId))
        {
            TempData["Error"] = "Thiếu thông tin lệnh cần huỷ.";
            return RedirectToAction(nameof(Index));
        }

        var account = await _accounts.FindAsync(accountId);
        if (account is null || string.IsNullOrWhiteSpace(account.ApiKey) || string.IsNullOrWhiteSpace(account.ApiSecret))
        {
            TempData["Error"] = "Tài khoản không hợp lệ hoặc chưa cấu hình API key.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var provider = _orderProviderFactory.Create(account.ApiKey!, account.ApiSecret!, _liveTradingOptions.UseTestnet);
            await provider.CancelOrderAsync(symbol, orderId, cancellationToken);
            TempData["Message"] = $"Đã huỷ lệnh chờ {symbol} (orderId {orderId}).";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Huỷ lệnh {symbol} #{orderId} lỗi: {ex.Message}";
        }
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Đọc lệnh chờ (LIMIT đợi khớp + STOP/TP treo) trực tiếp từ Binance cho mọi tài khoản active.
    /// Read-only — không lưu DB. Lỗi 1 tài khoản không làm hỏng cả trang.
    /// </summary>
    private async Task<(IReadOnlyList<OpenOrderRow> Orders, bool Loaded)> LoadOpenOrdersAsync(CancellationToken ct)
    {
        var accounts = await _accounts.FindListAsync(a =>
            a.IsActive && a.ApiKey != null && a.ApiSecret != null);
        if (accounts.Count == 0) return (new List<OpenOrderRow>(), true);

        var rows = new List<OpenOrderRow>();
        var allOk = true;
        foreach (var account in accounts)
        {
            try
            {
                // Khoá theo cả venue: đổi testnet ↔ sàn thật phải thấy ngay bảng khác, không phải
                // đợi cache cũ hết hạn rồi mới biết mình đang nhìn sàn nào.
                var cacheKey = $"open-orders:{account.Id}:{(_liveTradingOptions.UseTestnet ? "test" : "live")}";
                if (!_cache.TryGetValue(cacheKey, out IReadOnlyList<MMW.Application.MarketData.Models.ExchangeOpenOrder>? orders) || orders is null)
                {
                    var provider = _orderProviderFactory.Create(account.ApiKey!, account.ApiSecret!, _liveTradingOptions.UseTestnet);
                    orders = await provider.GetOpenOrdersAsync(null, ct);
                    _cache.Set(cacheKey, orders, OpenOrdersCacheTtl);
                }
                rows.AddRange(orders.Select(o => new OpenOrderRow(account.Id, account.Name ?? "—", o)));
            }
            catch
            {
                allOk = false; // báo nhẹ ở view, không chặn nhật ký
            }
        }

        return (rows.OrderByDescending(r => r.Order.CreatedTimeUtc).ToList(), allOk);
    }

    // --- Form ghi nhận lệnh (tay HOẶC điền sẵn từ đề xuất) ---

    [HttpGet]
    public async Task<IActionResult> Create(long? signalId)
    {
        var accountId = await DefaultAccountIdAsync();
        var form = new CreateTradeForm { TradingAccountId = accountId };
        string? hint = null;

        // Điền sẵn từ đề xuất → user review/chỉnh rồi mới lưu (giống form nhập tay).
        if (signalId is long sid)
        {
            var signal = await _signals.FindAsync(sid);
            if (signal is not null)
            {
                form.Symbol = signal.Symbol;
                form.Direction = signal.Direction;
                form.OrderType = OrderType.Limit;
                form.Status = TradeStatus.Open;
                form.EntryPrice = signal.Entry;
                form.StopLoss = signal.StopLoss;
                form.TakeProfit = signal.TakeProfit;
                form.Quantity = await AutoSizeAsync(accountId, signal.Entry, signal.StopLoss);
                form.Note = $"Từ đề xuất #{signal.Id} ({signal.Symbol} {signal.Direction})";
                hint = $"đề xuất #{signal.Id}";
            }
        }

        return View(await BuildCreateViewModelAsync(form, hint));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateTradeForm form)
    {
        if (!ModelState.IsValid)
            return View(await BuildCreateViewModelAsync(form, null));

        var dto = ToDto(form);
        var id = await _tradeService.CreateAsync(dto);

        // Tạo lệnh tay cũng gửi lên sàn (LiveOrderService tự chặn nếu master switch tắt /
        // thiếu key / vượt cap / vi phạm rule Critical / lệnh Planned).
        await _liveOrders.PlaceForTradeAsync(id);

        TempData["Message"] = $"Đã ghi nhận lệnh #{id} ({dto.Symbol}). Đã chấm rule + behavior.";
        return RedirectToAction(nameof(Index));
    }

    // --- Sửa lệnh ---

    [HttpGet]
    public async Task<IActionResult> Edit(long id)
    {
        var trade = await _trades.FindAsync(id);
        if (trade is null) return NotFound();
        if (trade.Status == TradeStatus.Closed)
        {
            TempData["Message"] = "Không sửa được lệnh đã đóng.";
            return RedirectToAction(nameof(Index));
        }

        var form = new CreateTradeForm
        {
            Id = trade.Id,
            TradingAccountId = trade.TradingAccountId,
            StrategyId = trade.StrategyId,
            Symbol = trade.Symbol,
            Direction = trade.Direction,
            OrderType = trade.OrderType,
            Status = trade.Status,
            EntryPrice = trade.EntryPrice,
            StopLoss = trade.StopLoss,
            TakeProfit = trade.TakeProfit,
            Quantity = trade.Quantity,
            Leverage = trade.Leverage,
            Fee = trade.Fee,
            EmotionBefore = trade.EmotionBefore,
            Note = trade.Note,
        };
        return View("Create", await BuildCreateViewModelAsync(form, null));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(CreateTradeForm form)
    {
        if (!ModelState.IsValid)
            return View("Create", await BuildCreateViewModelAsync(form, null));

        await _tradeService.UpdateAsync(form.Id, ToDto(form));
        // Lệnh live: đồng bộ SL/TP mới lên sàn (huỷ SL/TP cũ, đặt lại). No-op nếu không live.
        await _liveOrders.SyncLevelsAsync(form.Id);
        TempData["Message"] = $"Đã cập nhật lệnh #{form.Id}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(long id)
    {
        // Lệnh live đang mở: đóng vị thế + huỷ SL/TP trên sàn TRƯỚC khi xoá khỏi hệ thống.
        await _liveOrders.CloseOnExchangeAsync(id);
        await _tradeService.DeleteAsync(id);
        TempData["Message"] = $"Đã xoá lệnh #{id}.";
        return RedirectToAction(nameof(Index));
    }

    // --- Đóng lệnh ---

    [HttpGet]
    public async Task<IActionResult> Close(long id)
    {
        var trade = await _trades.FindAsync(id);
        if (trade is null) return NotFound();
        if (trade.Status == TradeStatus.Closed)
        {
            TempData["Message"] = "Lệnh đã đóng.";
            return RedirectToAction(nameof(Index));
        }

        var form = new CloseTradeForm
        {
            TradeId = trade.Id,
            Symbol = trade.Symbol,
            Direction = trade.Direction,
            EntryPrice = trade.EntryPrice,
            Quantity = trade.Quantity,
            ExitPrice = trade.TakeProfit ?? trade.EntryPrice,
        };
        return View(form);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(CloseTradeForm form)
    {
        if (!ModelState.IsValid)
            return View(form);

        // Lệnh live: đóng vị thế + huỷ SL/TP trên sàn TRƯỚC, rồi ghi sổ kết quả.
        await _liveOrders.CloseOnExchangeAsync(form.TradeId);
        await _tradeService.CloseAsync(form.TradeId, form.ExitPrice, form.EmotionAfter);
        TempData["Message"] = $"Đã đóng lệnh #{form.TradeId} tại giá {form.ExitPrice:N4}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AnalyzeBeforeSave(CreateTradeForm form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var messages = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .ToList();

            return BadRequest(new { ok = false, messages });
        }

        var result = await _preflightAnalysis.AnalyzeAsync(ToPreflightRequest(form), cancellationToken);
        return Json(new { ok = true, analysis = result });
    }

    // --- Lấy giá live cho form (JSON) ---

    // --- Gợi ý SL/TP bằng AI (preflight) ---
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SuggestLevels(CreateTradeForm form, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _preflightAnalysis.AnalyzeAsync(ToPreflightRequest(form), cancellationToken);
            return Json(new
            {
                ok = true,
                stopLoss = result.SuggestedStopLoss,
                takeProfit = result.SuggestedTakeProfit,
                isAi = result.AiAnswered,
                advice = result.Advice,
            });
        }
        catch
        {
            return Json(new { ok = false });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Price(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return Json(new { ok = false });
        try
        {
            var sym = symbol.Trim().ToUpperInvariant();
            var ticker = await _marketData.GetTickerAsync(sym);
            var filter = await _marketData.GetPriceFilterAsync(sym);
            return Json(new
            {
                ok = true,
                price = ticker.Price,
                tickSize = filter?.TickSize,
                priceDecimals = filter?.PriceDecimals,
            });
        }
        catch
        {
            return Json(new { ok = false });
        }
    }

    /// <summary>Trả tickSize/độ chính xác giá của 1 symbol (cho client làm tròn Entry/SL/TP).</summary>
    [HttpGet]
    public async Task<IActionResult> PriceFilter(string symbol, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return Json(new { ok = false });
        try
        {
            var filter = await _marketData.GetPriceFilterAsync(symbol.Trim().ToUpperInvariant(), cancellationToken);
            return filter is null
                ? Json(new { ok = false })
                : Json(new { ok = true, tickSize = filter.TickSize, priceDecimals = filter.PriceDecimals });
        }
        catch
        {
            return Json(new { ok = false });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Sync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _tradeSync.SyncAllAccountsAsync(cancellationToken);
            TempData["Message"] = $"Đồng bộ từ Binance: {result.Synced} cập nhật, {result.Skipped} bỏ qua, {result.Failed} lỗi.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Lỗi đồng bộ: {ex.Message}";
        }
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Kích hoạt lại lệnh Blocked/Error/Cancelled, sau đó thử gửi lên sàn.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reactivate(long id, CancellationToken cancellationToken)
    {
        try
        {
            await _tradeService.ReactivateAsync(id, cancellationToken);
            await _liveOrders.PlaceForTradeAsync(id, cancellationToken);
            TempData["Message"] = $"Đã kích hoạt lại lệnh #{id}. Hệ thống sẽ thử gửi lên Binance nếu điều kiện phù hợp.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Lỗi kích hoạt lại #{id}: {ex.Message}";
        }
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Import vị thế đang mở từ Binance Futures vào nhật ký (cho lệnh đặt tay bên sàn).
    /// Dùng API key của từng tài khoản, gọi /fapi/v2/positionRisk, tạo entry nếu chưa có.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportFromBinance(CancellationToken cancellationToken)
    {
        var accounts = await _accounts.FindListAsync(a =>
            a.IsActive && a.ApiKey != null && a.ApiSecret != null);

        if (!accounts.Any())
        {
            TempData["Error"] = "Không có tài khoản nào cấu hình API key Binance.";
            return RedirectToAction(nameof(Index));
        }

        var totalImported = 0;
        var totalReconciled = 0;
        var errors = new List<string>();

        foreach (var account in accounts)
        {
            try
            {
                // Luôn dùng real network (không phải testnet) khi đọc vị thế thực.
                var provider = _orderProviderFactory.Create(account.ApiKey!, account.ApiSecret!, useTestnet: false);
                var positions = await provider.GetOpenPositionsAsync(null, cancellationToken);

                foreach (var pos in positions)
                {
                    var direction = pos.IsLong ? TradeDirection.Long : TradeDirection.Short;
                    var qty = Math.Abs(pos.PositionAmt);
                    var positionVersion = pos.UpdatedAtUtc.HasValue
                        ? new DateTimeOffset(DateTime.SpecifyKind(pos.UpdatedAtUtc.Value, DateTimeKind.Utc)).ToUnixTimeMilliseconds()
                        : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    var externalId = $"binance_pos_{pos.Symbol}_{direction}_{positionVersion}";

                    // Đã có lệnh Open trùng symbol + hướng → SYNC lại entry/khối lượng theo vị thế thật
                    // (vd đã nhồi thêm/bớt lệnh bên sàn) thay vì bỏ qua.
                    var existing = await _trades.FirstOrDefaultAsync(t =>
                        t.TradingAccountId == account.Id &&
                        t.Symbol == pos.Symbol &&
                        t.Direction == direction &&
                        t.Status == TradeStatus.Open);

                    if (existing is not null)
                    {
                        var changed = false;
                        if (pos.EntryPrice > 0m && existing.EntryPrice != pos.EntryPrice)
                        {
                            existing.EntryPrice = pos.EntryPrice;
                            changed = true;
                        }
                        if (qty > 0m && existing.Quantity != qty)
                        {
                            existing.Quantity = qty;
                            changed = true;
                        }
                        if (changed)
                        {
                            _trades.Update(existing);
                            totalReconciled++;
                        }
                        continue;
                    }

                    var dto = new TradeDto
                    {
                        TradingAccountId = account.Id,
                        Symbol = pos.Symbol,
                        Direction = direction,
                        Status = TradeStatus.Open,
                        Source = TradeSource.Import,
                        OrderType = OrderType.Market,
                        EntryPrice = pos.EntryPrice,
                        Quantity = qty,
                        OpenedAt = pos.UpdatedAtUtc ?? DateTime.UtcNow,
                        Note = "Import từ Binance Futures (vị thế thực tế)",
                        ExternalId = externalId,
                    };
                    await _tradeService.CreateAsync(dto);
                    totalImported++;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{account.Name}: {ex.Message}");
            }
        }

        if (totalReconciled > 0)
            await _unitOfWork.CommitAsync(cancellationToken);

        if (errors.Any())
            TempData["Error"] = "Lỗi khi import: " + string.Join("; ", errors);

        TempData["Message"] = (totalImported, totalReconciled) switch
        {
            ( > 0, > 0) => $"Đã import {totalImported} vị thế mới và đồng bộ {totalReconciled} lệnh có sẵn từ Binance.",
            ( > 0, 0) => $"Đã import {totalImported} vị thế từ Binance Futures.",
            (0, > 0) => $"Đã đồng bộ {totalReconciled} lệnh có sẵn theo vị thế thật trên Binance.",
            _ => "Không có vị thế mới để import (tất cả đã khớp với nhật ký).",
        };

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Symbols(string? q, CancellationToken cancellationToken)
    {
        var remoteSymbols = await _symbolSearch.SearchFuturesSymbolsAsync(q, 40, cancellationToken);
        var whitelist = (await _watchItems.GetAllAsync())
            .Select(w => w.Symbol?.Trim().ToUpperInvariant())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .ToHashSet();
        var keyword = (q ?? string.Empty).Trim().ToUpperInvariant();
        var symbols = whitelist
            .Concat(remoteSymbols.Select(s => s.Trim().ToUpperInvariant()))
            .Where(s => string.IsNullOrWhiteSpace(keyword) || s.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .OrderBy(s => whitelist.Contains(s) ? 0 : 1) // watchlist ưu tiên lên đầu
            .ThenBy(s => string.IsNullOrWhiteSpace(keyword) ? 1 : s.StartsWith(keyword, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(s => s.Length)
            .ThenBy(s => s)
            .Take(40)
            .ToList();

        return Json(new
        {
            results = symbols.Select(s => new
            {
                id = s,
                text = whitelist.Contains(s) ? $"★ {s}" : s, // đánh dấu symbol đang theo dõi
            })
        });
    }

    private static TradeDto ToDto(CreateTradeForm form) => new()
    {
        TradingAccountId = form.TradingAccountId,
        StrategyId = form.StrategyId,
        Symbol = form.Symbol.Trim().ToUpperInvariant(),
        Direction = form.Direction,
        OrderType = form.OrderType,
        Status = form.Status,
        Source = TradeSource.Manual,
        EntryPrice = form.EntryPrice,
        StopLoss = form.StopLoss,
        TakeProfit = form.TakeProfit,
        Quantity = form.Quantity,
        Leverage = form.Leverage,
        Fee = form.Fee,
        EmotionBefore = form.EmotionBefore,
        Note = form.Note,
        OpenedAt = DateTime.UtcNow,
    };

    private static TradePreflightAnalysisRequest ToPreflightRequest(CreateTradeForm form) => new()
    {
        TradingAccountId = form.TradingAccountId,
        Symbol = form.Symbol,
        Direction = form.Direction,
        OrderType = form.OrderType,
        Status = form.Status,
        EntryPrice = form.EntryPrice,
        StopLoss = form.StopLoss,
        TakeProfit = form.TakeProfit,
        Quantity = form.Quantity,
        Leverage = form.Leverage,
        Fee = form.Fee,
        EmotionBefore = form.EmotionBefore,
        Note = form.Note,
    };

    private async Task<CreateTradeViewModel> BuildCreateViewModelAsync(CreateTradeForm form, string? hint)
    {
        var accounts = (await _accounts.GetAllAsync()).Where(a => a.IsActive).OrderBy(a => a.Name).ToList();
        var strategies = (await _strategies.GetAllAsync()).OrderBy(s => s.Name).ToList();
        var symbols = (await _watchItems.GetAllAsync())
            .Select(w => w.Symbol).Distinct().OrderBy(s => s).ToList();

        var risks = new List<AccountRiskInfo>();
        foreach (var a in accounts)
        {
            var rs = await _settings.GetRiskSettingAsync(a.Id);
            risks.Add(new AccountRiskInfo
            {
                Id = a.Id,
                Balance = await _liveBalance.GetEffectiveBalanceAsync(a), // số dư thật từ Binance (fallback DB)
                MaxRiskPercent = rs.MaxRiskPerTradePercent,
                Rr = rs.MinRiskRewardRatio,
            });
        }

        return new CreateTradeViewModel
        {
            Form = form,
            Accounts = accounts,
            Strategies = strategies,
            Symbols = symbols,
            AccountRisks = risks,
            FromHint = hint,
        };
    }

    /// <summary>Khối lượng = (vốn × maxRisk%) / |Entry − StopLoss|.</summary>
    private async Task<decimal> AutoSizeAsync(long accountId, decimal entry, decimal stopLoss)
    {
        var account = await _accounts.FindAsync(accountId);
        var risk = await _settings.GetRiskSettingAsync(accountId);
        var stopDistance = Math.Abs(entry - stopLoss);
        if (account is null || stopDistance <= 0m)
            return 0m;
        var balance = await _liveBalance.GetEffectiveBalanceAsync(account); // số dư thật từ Binance
        if (balance <= 0m)
            return 0m;
        var riskAmount = balance * risk.MaxRiskPerTradePercent / 100m;
        return Math.Round(riskAmount / stopDistance, 8, MidpointRounding.AwayFromZero);
    }

    private async Task<long> DefaultAccountIdAsync()
    {
        var setting = await _settings.GetAppSettingAsync();
        if (setting.DefaultTradingAccountId is long id)
            return id;
        var first = await _accounts.FirstOrDefaultAsync(a => a.IsActive);
        return first?.Id ?? 0;
    }
}
