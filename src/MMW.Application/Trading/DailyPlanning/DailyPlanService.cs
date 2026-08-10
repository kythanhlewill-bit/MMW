using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MMW.Application.Abstractions;
using MMW.Application.MarketData;
using MMW.Application.MarketData.Models;
using MMW.Application.Trading.TimeGuard;
using MMW.Domain.Entities;
using MMW.Shared.Interfaces;

namespace MMW.Application.Trading.DailyPlanning;

public interface IDailyPlanService
{
    /// <summary>
    /// Sinh kế hoạch cho ngày UTC. BẤT BIẾN theo <c>(tradingAccountId, planDate)</c>:
    /// gọi lại trong cùng ngày trả về bản đã có, KHÔNG ghi đè.
    /// </summary>
    Task<DailyPlan> GenerateAsync(long tradingAccountId, DateOnly planDateUtc, CancellationToken ct = default);

    /// <summary>Kế hoạch của ngày hiện tại. Null ⟹ mọi lệnh mới bị chặn (FR-023).</summary>
    Task<DailyPlan?> GetCurrentAsync(long tradingAccountId, CancellationToken ct = default);
}

/// <summary>
/// Thu thập đầu vào, gọi bộ phân loại thuần, rồi lưu kế hoạch bất biến của ngày.
/// </summary>
/// <remarks>
/// Ranh giới trách nhiệm: lớp này chạm mạng và cơ sở dữ liệu nhưng KHÔNG chứa một quy tắc
/// nghiệp vụ nào. Toàn bộ phần quyết định nằm ở <see cref="IDayRegimeClassifier"/>, vốn thuần
/// và tất định — nhờ vậy kiểm thử lịch sử chạy lại được đúng cùng đoạn mã đó.
///
/// Mọi nguồn dữ liệu đều được bọc: một nguồn lỗi làm hụt một thành phần, không làm chết cả
/// kế hoạch (FR-022, bất biến 6). Kế hoạch neo theo BTC — cấu trúc, biến động và các mức giá
/// tham chiếu đều đọc từ BTC, đúng như cấu hình đã chốt.
/// </remarks>
public sealed class DailyPlanService : IDailyPlanService
{
    /// <summary>Mã neo của kế hoạch ngày.</summary>
    public const string AnchorSymbol = "BTCUSDT";

    /// <summary>Đủ cho 90 mẫu phân vị (cần thêm 14 phiên khởi tạo ATR) và dư một quãng an toàn.</summary>
    private const int DailyCandleLimit = 120;

    private const string OpenInterestPeriod = "1h";
    private const string LongShortPeriod = "1h";

    private readonly IDayRegimeClassifier _classifier;
    private readonly IMarketDataProvider _marketData;
    private readonly IMarketSentimentProvider _sentiment;
    private readonly IScheduledEventCalendar _calendar;
    private readonly IBaseRepository<EngineSetting> _settings;
    private readonly IBaseRepository<DailyPlan> _plans;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly ILogger<DailyPlanService> _logger;
    private readonly Dictionary<(long AccountId, DateOnly Date), DailyPlan> _scopeCache = new();
    private readonly Dictionary<long, EngineSetting> _settingCache = new();

    public DailyPlanService(
        IDayRegimeClassifier classifier,
        IMarketDataProvider marketData,
        IMarketSentimentProvider sentiment,
        IScheduledEventCalendar calendar,
        IBaseRepository<EngineSetting> settings,
        IBaseRepository<DailyPlan> plans,
        IUnitOfWork unitOfWork,
        IClock clock,
        ILogger<DailyPlanService> logger)
    {
        _classifier = classifier;
        _marketData = marketData;
        _sentiment = sentiment;
        _calendar = calendar;
        _settings = settings;
        _plans = plans;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _logger = logger;
    }

    public async Task<DailyPlan> GenerateAsync(
        long tradingAccountId, DateOnly planDateUtc, CancellationToken ct = default)
        => await GenerateCoreAsync(tradingAccountId, planDateUtc, persist: true, ct);

    internal async Task<DailyPlan> GenerateTransientAsync(
        long tradingAccountId, DateOnly planDateUtc, CancellationToken ct = default)
        => await GenerateCoreAsync(tradingAccountId, planDateUtc, persist: false, ct);

    private async Task<DailyPlan> GenerateCoreAsync(
        long tradingAccountId, DateOnly planDateUtc, bool persist, CancellationToken ct)
    {
        if (_scopeCache.TryGetValue((tradingAccountId, planDateUtc), out var scoped)) return scoped;

        var existing = persist ? await FindAsync(tradingAccountId, planDateUtc, ct) : null;
        if (existing is not null)
        {
            // Bất biến 4. Trả bản cũ nguyên vẹn — cập nhật "cho mới" cũng là ghi đè, và nó
            // làm mọi phiếu chấm điểm sinh trước đó mất ngữ cảnh.
            _logger.LogInformation(
                "Kế hoạch ngày {PlanDate} của tài khoản {AccountId} đã có (Id={PlanId}); không sinh lại.",
                planDateUtc, tradingAccountId, existing.Id);
            _scopeCache[(tradingAccountId, planDateUtc)] = existing;
            return existing;
        }

        if (!_settingCache.TryGetValue(tradingAccountId, out var setting))
        {
            setting = await _settings
                .Get(s => s.TradingAccountId == tradingAccountId)
                .AsNoTracking()
                .FirstOrDefaultAsync(ct)
                ?? throw new InvalidOperationException(
                    $"Tài khoản {tradingAccountId} chưa có cấu hình engine (EngineSetting).");
            _settingCache[tradingAccountId] = setting;
        }

        var candles = await SafeAsync(
            () => _marketData.GetCandlesAsync(AnchorSymbol, "1d", DailyCandleLimit, ct),
            Array.Empty<Candle>(), nameof(IMarketDataProvider.GetCandlesAsync));

        var dayStart = planDateUtc.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var events = await SafeAsync(
            () => _calendar.GetBetweenAsync(dayStart, dayStart.AddDays(1), ct),
            Array.Empty<ScheduledEvent>(), nameof(IScheduledEventCalendar.GetBetweenAsync));

        var funding = await SafeAsync(
            () => _marketData.GetFundingAsync(AnchorSymbol, ct), null, nameof(IMarketDataProvider.GetFundingAsync));

        var openInterest = await SafeAsync(
            () => _marketData.GetOpenInterestHistAsync(AnchorSymbol, OpenInterestPeriod, 25, ct),
            null, nameof(IMarketDataProvider.GetOpenInterestHistAsync));

        var longShort = await SafeAsync(
            () => _marketData.GetGlobalLongShortRatioAsync(AnchorSymbol, LongShortPeriod, ct),
            null, nameof(IMarketDataProvider.GetGlobalLongShortRatioAsync));

        var fearGreed = await SafeAsync(
            () => _sentiment.GetFearGreedIndexAsync(ct), null, nameof(IMarketSentimentProvider.GetFearGreedIndexAsync));

        var inputs = new DailyPlanInputs
        {
            BtcDailyCandles = candles,
            SymbolDailyCandles = candles,
            TodayEvents = events,
            FundingRate = funding?.LastFundingRate,
            OpenInterestChange24hPercent = openInterest?.ChangePercent(TimeSpan.FromHours(24)),
            LongShortAccountRatio = longShort?.LongShortRatioValue,
            FearGreedIndex = fearGreed,
        };

        var classification = _classifier.Classify(inputs, setting);
        var levels = KeyLevels.From(candles, planDateUtc);
        var adaptive = AdaptiveRegimePolicy.Apply(
            planDateUtc,
            classification.Regime,
            classification.RiskMultiplier,
            classification.MaxTradesToday);

        var plan = new DailyPlan
        {
            TradingAccountId = tradingAccountId,
            PlanDateUtc = planDateUtc,
            GeneratedAtUtc = _clock.UtcNow,

            DayRegime = classification.Regime,
            VolatilityRegime = classification.Volatility,
            AllowedDirections = classification.AllowedDirections,
            RiskMultiplier = adaptive.RiskMultiplier,
            MaxTradesToday = adaptive.MaxTradesToday,

            PreviousDayHigh = levels.PreviousDayHigh,
            PreviousDayLow = levels.PreviousDayLow,
            WeeklyOpen = levels.WeeklyOpen,
            DailyOpen = levels.DailyOpen,

            BtcStructure = classification.BtcStructure,
            AtrPercentile = classification.AtrPercentile,
            FundingRate = inputs.FundingRate,
            OpenInterestChange24hPercent = inputs.OpenInterestChange24hPercent,
            LongShortAccountRatio = inputs.LongShortAccountRatio,
            FearGreedIndex = inputs.FearGreedIndex,

            MissingInputs = classification.MissingInputs.Count == 0
                ? null
                : string.Join(", ", classification.MissingInputs),
            IsComplete = classification.MissingInputs.Count == 0,
        };

        if (persist)
        {
            await _plans.AddAsync(plan);
            await _unitOfWork.CommitAsync(ct);
        }
        _scopeCache[(tradingAccountId, planDateUtc)] = plan;

        _logger.LogDebug(
            "Đã sinh kế hoạch ngày. accountId={AccountId} planDate={PlanDate} regime={Regime} " +
            "volatility={Volatility} directions={Directions} risk={Risk} maxTrades={MaxTrades} missing={Missing}",
            tradingAccountId, planDateUtc, plan.DayRegime, plan.VolatilityRegime,
            plan.AllowedDirections, plan.RiskMultiplier, plan.MaxTradesToday, plan.MissingInputs ?? "-");

        return plan;
    }

    public async Task<DailyPlan?> GetCurrentAsync(long tradingAccountId, CancellationToken ct = default)
    {
        // FR-024: ngày giao dịch neo tại 00:00 UTC, nên "hôm nay" là ngày UTC của đồng hồ.
        var date = DateOnly.FromDateTime(_clock.UtcNow);
        if (_scopeCache.TryGetValue((tradingAccountId, date), out var cached)) return cached;

        var plan = await FindAsync(tradingAccountId, date, ct);
        if (plan is not null) _scopeCache[(tradingAccountId, date)] = plan;
        return plan;
    }

    private async Task<DailyPlan?> FindAsync(long tradingAccountId, DateOnly planDateUtc, CancellationToken ct) =>
        await _plans
            .Get(p => p.TradingAccountId == tradingAccountId && p.PlanDateUtc == planDateUtc)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Gọi một nguồn dữ liệu; lỗi thì ghi vết và trả giá trị dự phòng.
    /// </summary>
    /// <remarks>
    /// Năm phương thức futures của <c>IMarketDataProvider</c> đã hứa trả <c>null</c> thay vì
    /// ném, nhưng lời hứa đó chỉ phủ lỗi phía sàn — hết thời gian chờ, DNS hỏng hay JSON rác
    /// vẫn ném được. Bọc ở đây để bất biến 6 không phụ thuộc vào việc mọi nguồn đều cư xử đẹp.
    /// </remarks>
    private async Task<T> SafeAsync<T>(Func<Task<T>> call, T fallback, string source)
    {
        try
        {
            return await call();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nguồn {Source} lỗi khi sinh kế hoạch ngày; dùng giá trị dự phòng.", source);
            return fallback;
        }
    }
}
