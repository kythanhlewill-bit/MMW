using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MMW.Application.Abstractions;
using MMW.Application.Interfaces;
using MMW.Application.Trading.DailyPlanning;
using MMW.Domain.DbContext;
using MMW.Domain.Entities;
using MMW.Shared.Interfaces;
using MMW.Web.Models;

namespace MMW.Web.Controllers;

/// <summary>
/// Xem kế hoạch ngày: ràng buộc đang áp, đầu vào đã dùng, thành phần thiếu, và lịch sử.
/// </summary>
/// <remarks>
/// Cột "thành phần thiếu" là phần đáng xem nhất của màn hình này. Kế hoạch vẫn sinh được khi
/// thiếu dữ liệu, chỉ là thận trọng hơn — nên nếu không hiện ra thì trader sẽ thấy hệ số 0.5
/// suốt nhiều ngày mà không hiểu vì sao, và kết luận rằng hệ thống quá rụt rè.
/// </remarks>
public class DailyPlanController : Controller
{
    private const int HistoryDays = 30;

    private readonly IDailyPlanService _dailyPlan;
    private readonly MmwDbContext _db;
    private readonly ISettingsService _settings;
    private readonly IBaseRepository<TradingAccount> _accounts;
    private readonly IClock _clock;

    public DailyPlanController(
        IDailyPlanService dailyPlan,
        MmwDbContext db,
        ISettingsService settings,
        IBaseRepository<TradingAccount> accounts,
        IClock clock)
    {
        _dailyPlan = dailyPlan;
        _db = db;
        _settings = settings;
        _accounts = accounts;
        _clock = clock;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var account = await ResolveAccountAsync();
        var today = DateOnly.FromDateTime(_clock.UtcNow);

        var model = new DailyPlanViewModel
        {
            UtcNow = _clock.UtcNow,
            TodayUtc = today,
            HistoryDays = HistoryDays,
            AccountName = account?.Name,
        };

        if (account is not null)
        {
            model.Today = await _dailyPlan.GetCurrentAsync(account.Id, ct);

            model.Tomorrow = await _db.DailyPlans.AsNoTracking()
                .FirstOrDefaultAsync(p => p.TradingAccountId == account.Id && p.PlanDateUtc == today.AddDays(1), ct);

            model.History = await _db.DailyPlans.AsNoTracking()
                .Where(p => p.TradingAccountId == account.Id && p.PlanDateUtc >= today.AddDays(-HistoryDays))
                .OrderByDescending(p => p.PlanDateUtc)
                .ToListAsync(ct);
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
