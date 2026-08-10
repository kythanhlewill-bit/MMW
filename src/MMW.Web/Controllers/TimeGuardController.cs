using Microsoft.AspNetCore.Mvc;
using MMW.Application.Abstractions;
using MMW.Application.Interfaces;
using MMW.Application.Trading.TimeGuard;
using MMW.Domain.Entities;
using MMW.Shared.Interfaces;
using MMW.Web.Models;

namespace MMW.Web.Controllers;

/// <summary>
/// Xem tầng chặn theo khung giờ: các cửa sổ cấm sắp tới, cuốn lịch sự kiện, và tình trạng
/// cập nhật của phần lịch nạp tay.
/// </summary>
/// <remarks>
/// Màn hình này tồn tại để trader KIỂM CHỨNG được hệ thống, không phải để ngắm. Một tầng chặn
/// chạy ngầm mà không nhìn thấy được thì khi nó im lặng hỏng, không ai biết — và cách nó hỏng
/// (lịch quá hạn) lại không sinh ra lỗi nào cả.
/// </remarks>
public class TimeGuardController : Controller
{
    /// <summary>Tầm nhìn của bảng cửa sổ chặn.</summary>
    private const int HorizonHours = 48;

    private readonly ITimeGuardService _timeGuard;
    private readonly IScheduledEventCalendar _calendar;
    private readonly ISessionQualityProvider _sessionQuality;
    private readonly ISettingsService _settings;
    private readonly IBaseRepository<TradingAccount> _accounts;
    private readonly IClock _clock;

    public TimeGuardController(
        ITimeGuardService timeGuard,
        IScheduledEventCalendar calendar,
        ISessionQualityProvider sessionQuality,
        ISettingsService settings,
        IBaseRepository<TradingAccount> accounts,
        IClock clock)
    {
        _timeGuard = timeGuard;
        _calendar = calendar;
        _sessionQuality = sessionQuality;
        _settings = settings;
        _accounts = accounts;
        _clock = clock;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var utcNow = _clock.UtcNow;
        var account = await ResolveAccountAsync();

        var model = new TimeGuardViewModel
        {
            UtcNow = utcNow,
            HorizonHours = HorizonHours,
            AccountName = account?.Name,
            Freshness = await _timeGuard.GetCalendarFreshnessAsync(utcNow, ct),
            Events = await _calendar.GetBetweenAsync(utcNow, utcNow.AddHours(HorizonHours), ct),
        };

        if (account is not null)
        {
            model.Windows = await _timeGuard.GetWindowsAsync(
                account.Id, utcNow, utcNow.AddHours(HorizonHours), ct);

            model.Current = await _timeGuard.CheckAsync(account.Id, "BTCUSDT", utcNow, ct);
            model.SessionQuality = await _sessionQuality.GetAsync(account.Id, utcNow, ct);
        }

        return View(model);
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
