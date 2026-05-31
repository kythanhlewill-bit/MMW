using Microsoft.AspNetCore.Mvc;
using MMW.Application.Interfaces;
using MMW.Domain.Entities;
using MMW.Shared.Interfaces;
using MMW.Web.Models;

namespace MMW.Web.Controllers;

public class SettingsController : Controller
{
    private readonly ISettingsService _settings;
    private readonly IBaseRepository<TradingAccount> _accounts;

    public SettingsController(ISettingsService settings, IBaseRepository<TradingAccount> accounts)
    {
        _settings = settings;
        _accounts = accounts;
    }

    public async Task<IActionResult> Index()
    {
        var app = await _settings.GetAppSettingAsync();
        var accounts = (await _accounts.GetAllAsync()).OrderBy(a => a.Name).ToList();

        var vm = new SettingsViewModel
        {
            General = new SettingsGeneralForm
            {
                DefaultTradingAccountId = app.DefaultTradingAccountId,
                ConfirmBeforeCreateTrade = app.ConfirmBeforeCreateTrade,
                MinSignalScore = app.MinSignalScore,
            },
            Accounts = accounts,
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveGeneral([Bind(Prefix = "General")] SettingsGeneralForm model)
    {
        await _settings.UpdateAppSettingAsync(model.DefaultTradingAccountId, model.ConfirmBeforeCreateTrade, model.MinSignalScore);
        TempData["Message"] = "Đã lưu cấu hình chung.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Account(long id)
    {
        var account = await _accounts.FindAsync(id);
        if (account is null) return NotFound();

        var rs = await _settings.GetRiskSettingAsync(id);
        var vm = new RiskSettingForm
        {
            AccountId = id,
            AccountName = account.Name,
            MaxRiskPerTradePercent = rs.MaxRiskPerTradePercent,
            MinRiskRewardRatio = rs.MinRiskRewardRatio,
            MaxTradesPerDay = rs.MaxTradesPerDay,
            MaxDailyLossPercent = rs.MaxDailyLossPercent,
            RequireStopLoss = rs.RequireStopLoss,
            RevengeTradeWindowMinutes = rs.RevengeTradeWindowMinutes,
            LossStreakThreshold = rs.LossStreakThreshold,
            TiltSizeIncreasePercent = rs.TiltSizeIncreasePercent,
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveAccount(RiskSettingForm model)
    {
        await _settings.UpsertRiskSettingAsync(model.AccountId, new RiskSetting
        {
            MaxRiskPerTradePercent = model.MaxRiskPerTradePercent,
            MinRiskRewardRatio = model.MinRiskRewardRatio,
            MaxTradesPerDay = model.MaxTradesPerDay,
            MaxDailyLossPercent = model.MaxDailyLossPercent,
            RequireStopLoss = model.RequireStopLoss,
            RevengeTradeWindowMinutes = model.RevengeTradeWindowMinutes,
            LossStreakThreshold = model.LossStreakThreshold,
            TiltSizeIncreasePercent = model.TiltSizeIncreasePercent,
        });
        TempData["Message"] = $"Đã lưu cấu hình rủi ro cho {model.AccountName}.";
        return RedirectToAction(nameof(Index));
    }
}
