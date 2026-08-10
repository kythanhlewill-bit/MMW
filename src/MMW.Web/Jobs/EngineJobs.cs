using Hangfire;
using Microsoft.EntityFrameworkCore;
using MMW.Application.Abstractions;
using MMW.Application.Interfaces;
using MMW.Application.Services;
using MMW.Application.Backtest;
using MMW.Application.Ai;
using MMW.Application.Trading.DailyPlanning;
using MMW.Domain.DbContext;

namespace MMW.Web.Jobs;

/// <summary>
/// Vỏ bọc Hangfire cho các service của engine tất định.
/// </summary>
/// <remarks>
/// Tồn tại vì hai lý do. Một: các service của engine nhận <c>utcNow</c> làm THAM SỐ để kiểm thử
/// lịch sử chạy lại được đúng cùng đoạn mã, nhưng Hangfire chỉ gọi được phương thức không tham
/// số — chỗ nối giữa hai thế giới đó phải nằm ở đâu đó, và đây là chỗ đó. Hai: giữ phụ thuộc
/// Hangfire ở tầng Web, không kéo nó xuống tầng Application.
/// </remarks>
public interface IEngineJobs
{
    Task RunPositionManageAsync(CancellationToken ct = default);
    Task RunCalendarFreshnessAsync(CancellationToken ct = default);
    Task RunDailyPlanAsync(CancellationToken ct = default);
    Task RunDailyPlanCatchUpAsync(CancellationToken ct = default);
    Task RunSignalEvalAsync(CancellationToken ct = default);
    Task RunArchiveSnapshotAsync(CancellationToken ct = default);
    Task RunNewsScanAsync(CancellationToken ct = default);
}

public sealed class EngineJobs : IEngineJobs
{
    private readonly MmwDbContext _db;
    private readonly IClock _clock;
    private readonly IPositionManageService _positionManage;
    private readonly ICalendarFreshnessMonitor _calendarFreshness;
    private readonly IDailyPlanService _dailyPlan;
    private readonly ISignalEvalService _signalEval;
    private readonly IKlineArchiveService _archive;
    private readonly IDailyBriefEnricher _dailyBrief;
    private readonly IMarketContextService _marketContext;
    private readonly ILogger<EngineJobs> _logger;

    public EngineJobs(
        MmwDbContext db,
        IClock clock,
        IPositionManageService positionManage,
        ICalendarFreshnessMonitor calendarFreshness,
        IDailyPlanService dailyPlan,
        ISignalEvalService signalEval,
        IKlineArchiveService archive,
        IDailyBriefEnricher dailyBrief,
        IMarketContextService marketContext,
        ILogger<EngineJobs> logger)
    {
        _db = db;
        _clock = clock;
        _positionManage = positionManage;
        _calendarFreshness = calendarFreshness;
        _dailyPlan = dailyPlan;
        _signalEval = signalEval;
        _archive = archive;
        _dailyBrief = dailyBrief;
        _marketContext = marketContext;
        _logger = logger;
    }

    /// <summary>
    /// Rà soát vị thế đang mở của mọi tài khoản trước cửa sổ chặn (FR-013).
    /// </summary>
    /// <remarks>
    /// <c>DisableConcurrentExecution</c> là bắt buộc chứ không phải cho gọn: job chạy mỗi phút,
    /// và hai lượt chồng nhau sẽ cùng thấy một vị thế "chưa xử lý" rồi cùng ra lệnh đóng bớt —
    /// đóng hai lần thay vì một.
    /// </remarks>
    [DisableConcurrentExecution(timeoutInSeconds: 50)]
    public async Task RunPositionManageAsync(CancellationToken ct = default)
    {
        if (!await DeterministicEnabledAsync(ct)) return;
        var utcNow = _clock.UtcNow;

        var accountIds = await _db.EngineSettings
            .Where(s => s.TradingAccount.IsActive)
            .Select(s => s.TradingAccountId)
            .ToListAsync(ct);

        foreach (var accountId in accountIds)
        {
            try
            {
                await _positionManage.RunAsync(accountId, utcNow, ct);
            }
            catch (Exception ex)
            {
                // Một tài khoản lỗi không được kéo theo các tài khoản còn lại.
                _logger.LogError(ex, "Lỗi xử lý vị thế trước cửa sổ chặn cho tài khoản {AccountId}.", accountId);
            }
        }
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunCalendarFreshnessAsync(CancellationToken ct = default) =>
        await _calendarFreshness.RunAsync(_clock.UtcNow, ct);

    /// <summary>
    /// Sinh kế hoạch cho ngày UTC KẾ TIẾP. Chạy lúc 23:30 UTC (FR-016).
    /// </summary>
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunDailyPlanAsync(CancellationToken ct = default)
    {
        if (!await DeterministicEnabledAsync(ct)) return;
        await GenerateForAllAsync(DateOnly.FromDateTime(_clock.UtcNow).AddDays(1), ct);
    }

    /// <summary>
    /// Sinh bù kế hoạch của NGÀY HÔM NAY nếu còn thiếu. Chạy một lần khi ứng dụng khởi động.
    /// </summary>
    /// <remarks>
    /// Chỉ bù cho hôm nay, KHÔNG bù cho ngày mai. Kế hoạch là bất biến, nên nếu khởi động lúc
    /// 08:00 mà sinh luôn kế hoạch ngày mai thì bản ấy sẽ dựa trên dữ liệu của nửa ngày trước
    /// và job 23:30 sẽ không bao giờ thay được nó nữa.
    /// </remarks>
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunDailyPlanCatchUpAsync(CancellationToken ct = default)
    {
        if (!await DeterministicEnabledAsync(ct)) return;
        await GenerateForAllAsync(DateOnly.FromDateTime(_clock.UtcNow), ct);
    }

    /// <summary>
    /// Chấm điểm mọi mã của mọi tài khoản trên cây nến 15 phút vừa đóng.
    /// </summary>
    /// <remarks>
    /// Không có lời gọi mô hình ngôn ngữ nào trong chu kỳ này (SC-001). Cron đặt trễ một phút
    /// so với mốc nến đóng theo R-011: gọi đúng 00:00 thì sàn thường chưa chốt xong cây nến,
    /// và nến chưa đóng bị cắt bỏ sẽ khiến chu kỳ chấm trên cây nến CŨ.
    /// </remarks>
    [DisableConcurrentExecution(timeoutInSeconds: 240)]
    public async Task RunSignalEvalAsync(CancellationToken ct = default)
    {
        if (!await DeterministicEnabledAsync(ct)) return;
        var utcNow = _clock.UtcNow;

        foreach (var accountId in await ActiveAccountIdsAsync(ct))
        {
            try
            {
                await _signalEval.EvaluateAllAsync(accountId, utcNow, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi chấm điểm cho tài khoản {AccountId}.", accountId);
            }
        }
    }

    /// <summary>
    /// Chụp dữ liệu nến và phí vốn vào kho, mỗi giờ một lần (T139).
    /// </summary>
    /// <remarks>
    /// Dựng DẦN kho lịch sử ngay từ bây giờ, để về sau kiểm thử chạy được đủ 100 điểm thay vì
    /// 90. Lượng hợp đồng mở chỉ có 30 ngày lịch sử công khai và sổ lệnh thì không có lịch sử
    /// nào — nghĩa là dữ liệu KHÔNG lấy lại được về sau. Không chụp từ hôm nay thì vĩnh viễn
    /// mất phần đó (giảm thiểu rủi ro R-003).
    /// </remarks>
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public async Task RunArchiveSnapshotAsync(CancellationToken ct = default)
    {
        var utcNow = _clock.UtcNow;
        var symbols = await _db.EngineSettings
            .Where(s => s.TradingAccount.IsActive)
            .Select(s => s.Symbols)
            .ToListAsync(ct);

        var distinct = symbols
            .SelectMany(s => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(s => s.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal);

        foreach (var symbol in distinct)
        {
            try
            {
                await _archive.BackfillAsync(symbol, "15m", utcNow.AddDays(-2), utcNow, ct);
                await _archive.BackfillFundingAsync(symbol, utcNow.AddDays(-2), utcNow, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi chụp kho lịch sử cho {Symbol}.", symbol);
            }
        }
    }

    [DisableConcurrentExecution(timeoutInSeconds: 240)]
    public async Task RunNewsScanAsync(CancellationToken ct = default)
    {
        if (!await DeterministicEnabledAsync(ct)) return;
        await _marketContext.ClassifyNewsAsync(ct);
    }

    private async Task GenerateForAllAsync(DateOnly planDateUtc, CancellationToken ct)
    {
        foreach (var accountId in await ActiveAccountIdsAsync(ct))
        {
            try
            {
                var plan = await _dailyPlan.GenerateAsync(accountId, planDateUtc, ct);
                await _dailyBrief.EnrichAsync(plan, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Lỗi sinh kế hoạch ngày {PlanDate} cho tài khoản {AccountId}.", planDateUtc, accountId);
            }
        }
    }

    private async Task<List<long>> ActiveAccountIdsAsync(CancellationToken ct) =>
        await _db.EngineSettings
            .Where(s => s.TradingAccount.IsActive)
            .Select(s => s.TradingAccountId)
            .ToListAsync(ct);

    private async Task<bool> DeterministicEnabledAsync(CancellationToken ct) =>
        await _db.AppSettings.AsNoTracking()
            .Select(x => (bool?)x.DeterministicEngineEnabled)
            .FirstOrDefaultAsync(ct) ?? false;
}
