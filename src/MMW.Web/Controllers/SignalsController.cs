using Microsoft.AspNetCore.Mvc;
using MMW.Application.Interfaces;
using MMW.Domain.Entities;
using MMW.Shared.Interfaces;

namespace MMW.Web.Controllers;

public class SignalsController : Controller
{
    private const int PageSize = 200;

    private readonly IBaseRepository<TradeSignal> _signals;
    private readonly IBaseRepository<TradingAccount> _accounts;
    private readonly ITradeService _tradeService;
    private readonly ISettingsService _settings;

    public SignalsController(
        IBaseRepository<TradeSignal> signals,
        IBaseRepository<TradingAccount> accounts,
        ITradeService tradeService,
        ISettingsService settings)
    {
        _signals = signals;
        _accounts = accounts;
        _tradeService = tradeService;
        _settings = settings;
    }

    public async Task<IActionResult> Index(string? symbol)
    {
        var query = _signals.GetAll();
        if (!string.IsNullOrWhiteSpace(symbol))
        {
            symbol = symbol.Trim().ToUpperInvariant();
            query = query.Where(s => s.Symbol == symbol);
        }

        var data = query
            .OrderByDescending(s => s.Id)
            .Take(PageSize)
            .ToList();

        ViewData["Symbol"] = symbol;
        ViewData["Confirm"] = (await _settings.GetAppSettingAsync()).ConfirmBeforeCreateTrade;
        return View(data);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTrade(long id)
    {
        var account = await ResolveAccountAsync();
        if (account is null)
        {
            TempData["Error"] = "Chưa có tài khoản giao dịch để tạo lệnh.";
            return RedirectToAction(nameof(Index));
        }

        var tradeId = await _tradeService.CreateFromSignalAsync(id, account.Id);
        TempData["Message"] = $"Đã tạo lệnh #{tradeId} từ đề xuất (tài khoản {account.Name}, auto-size theo % rủi ro). Đã chấm rule + behavior.";
        return RedirectToAction("Index", "Trades");
    }

    /// <summary>Tài khoản mặc định từ cấu hình; fallback tài khoản active đầu tiên.</summary>
    private async Task<TradingAccount?> ResolveAccountAsync()
    {
        var setting = await _settings.GetAppSettingAsync();
        if (setting.DefaultTradingAccountId is long defId)
        {
            var preferred = await _accounts.FindAsync(defId);
            if (preferred is { IsActive: true })
                return preferred;
        }
        return await _accounts.FirstOrDefaultAsync(a => a.IsActive);
    }
}
