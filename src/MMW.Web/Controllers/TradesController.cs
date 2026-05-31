using Microsoft.AspNetCore.Mvc;
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

    public TradesController(
        ITradeService tradeService,
        IBaseRepository<TradeAnalysis> analyses,
        IBaseRepository<Trade> trades,
        IBaseRepository<TradingAccount> accounts,
        IBaseRepository<Strategy> strategies,
        IBaseRepository<TradeSignal> signals,
        IBaseRepository<WatchItem> watchItems,
        ISettingsService settings,
        IMarketDataProvider marketData)
    {
        _tradeService = tradeService;
        _analyses = analyses;
        _trades = trades;
        _accounts = accounts;
        _strategies = strategies;
        _signals = signals;
        _watchItems = watchItems;
        _settings = settings;
        _marketData = marketData;
    }

    public async Task<IActionResult> Index()
    {
        var trades = await _tradeService.GetAllAsync();

        var openTradeIds = trades.Where(t => t.Status == TradeStatus.Open).Select(t => t.Id).ToList();
        var analysisList = await _analyses.FindListAsync(a => openTradeIds.Contains(a.TradeId));
        var analysisMap = analysisList.ToDictionary(a => a.TradeId);

        var vm = new TradeJournalViewModel
        {
            Trades = trades,
            Analyses = analysisMap,
        };

        return View(vm);
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
        TempData["Message"] = $"Đã cập nhật lệnh #{form.Id}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(long id)
    {
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

        await _tradeService.CloseAsync(form.TradeId, form.ExitPrice, form.EmotionAfter);
        TempData["Message"] = $"Đã đóng lệnh #{form.TradeId} tại giá {form.ExitPrice:N4}.";
        return RedirectToAction(nameof(Index));
    }

    // --- Lấy giá live cho form (JSON) ---

    [HttpGet]
    public async Task<IActionResult> Price(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return Json(new { ok = false });
        try
        {
            var ticker = await _marketData.GetTickerAsync(symbol.Trim().ToUpperInvariant());
            return Json(new { ok = true, price = ticker.Price });
        }
        catch
        {
            return Json(new { ok = false });
        }
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
            risks.Add(new AccountRiskInfo { Id = a.Id, Balance = a.CurrentBalance, MaxRiskPercent = rs.MaxRiskPerTradePercent });
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
        if (account is null || stopDistance <= 0m || account.CurrentBalance <= 0m)
            return 0m;
        var riskAmount = account.CurrentBalance * risk.MaxRiskPerTradePercent / 100m;
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
