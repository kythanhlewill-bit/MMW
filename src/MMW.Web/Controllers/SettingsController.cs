using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using MMW.Application.Interfaces;
using MMW.Application.Models;
using MMW.Domain.Entities;
using MMW.Shared.Interfaces;
using MMW.Web.Models;

namespace MMW.Web.Controllers;

public class SettingsController : Controller
{
    private readonly ISettingsService _settings;
    private readonly INotificationPreferenceService _notificationPreferences;
    private readonly IBaseRepository<TradingAccount> _accounts;

    public SettingsController(
        ISettingsService settings,
        INotificationPreferenceService notificationPreferences,
        IBaseRepository<TradingAccount> accounts)
    {
        _settings = settings;
        _notificationPreferences = notificationPreferences;
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
                AllowOverrideRisk = app.AllowOverrideRisk,
                DeterministicEngineEnabled = app.DeterministicEngineEnabled,
                ShadowComparisonEnabled = app.ShadowComparisonEnabled,
            },
            Accounts = accounts,
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveGeneral([Bind(Prefix = "General")] SettingsGeneralForm model)
    {
        await _settings.UpdateAppSettingAsync(
            model.DefaultTradingAccountId,
            model.ConfirmBeforeCreateTrade,
            model.MinSignalScore,
            model.AllowOverrideRisk,
            model.DeterministicEngineEnabled,
            model.ShadowComparisonEnabled);
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
            MaxTradesPerDayHtf = rs.MaxTradesPerDayHtf,
            MaxDailyLossPercentHtf = rs.MaxDailyLossPercentHtf,
            MaxRiskPerTradePercentHtf = rs.MaxRiskPerTradePercentHtf,
            LossStreakThresholdHtf = rs.LossStreakThresholdHtf,
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
            MaxTradesPerDayHtf = model.MaxTradesPerDayHtf,
            MaxDailyLossPercentHtf = model.MaxDailyLossPercentHtf,
            MaxRiskPerTradePercentHtf = model.MaxRiskPerTradePercentHtf,
            LossStreakThresholdHtf = model.LossStreakThresholdHtf,
            RequireStopLoss = model.RequireStopLoss,
            RevengeTradeWindowMinutes = model.RevengeTradeWindowMinutes,
            LossStreakThreshold = model.LossStreakThreshold,
            TiltSizeIncreasePercent = model.TiltSizeIncreasePercent,
        });
        TempData["Message"] = $"Đã lưu cấu hình rủi ro cho {model.AccountName}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Notifications(CancellationToken cancellationToken)
    {
        var settings = await _notificationPreferences.GetSettingsAsync(CurrentUserId(), cancellationToken);
        var vm = new NotificationSettingsForm
        {
            Email = settings.Email,
            Preferences = settings.Preferences.Select(p => new NotificationPreferenceForm
            {
                Type = p.Type,
                Name = p.Name,
                Description = p.Description,
                InAppEnabled = p.InAppEnabled,
                EmailEnabled = p.EmailEnabled,
                MinSeverity = p.MinSeverity,
            }).ToList(),
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveNotifications(NotificationSettingsForm model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View("Notifications", model);

        var updates = model.Preferences.Select(p => new NotificationPreferenceUpdateModel
        {
            Type = p.Type,
            InAppEnabled = p.InAppEnabled,
            EmailEnabled = p.EmailEnabled,
            MinSeverity = p.MinSeverity,
        }).ToList();

        await _notificationPreferences.UpdateSettingsAsync(CurrentUserId(), model.Email, updates, cancellationToken);
        TempData["Message"] = "Đã lưu cấu hình thông báo.";
        return RedirectToAction(nameof(Notifications));
    }

    private long CurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(value, out var userId) ? userId : 0;
    }
}
