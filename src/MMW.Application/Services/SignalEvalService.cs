using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MMW.Application.Abstractions;
using MMW.Application.Ai;
using MMW.Application.Indicators;
using MMW.Application.MarketData;
using MMW.Application.MarketData.Models;
using MMW.Application.Trading.DailyPlanning;
using MMW.Application.Trading.Discipline;
using MMW.Application.Trading.Execution;
using MMW.Application.Trading.Scoring;
using MMW.Application.Trading.Sizing;
using MMW.Application.Trading.TimeGuard;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;

namespace MMW.Application.Services;

public interface ISignalEvalService
{
    /// <summary>Chấm điểm một mã tại thời điểm cho trước và LƯU phiếu, dù có vào lệnh hay không.</summary>
    Task<EntryScorecard> EvaluateAsync(long tradingAccountId, string symbol, DateTime utcNow, CancellationToken ct = default);

    /// <summary>
    /// Chấm điểm bằng cùng luồng nghiệp vụ nhưng không ghi từng phiếu xuống SQL.
    /// Backtest dùng đường này để không biến 70.000 mốc thành hơn một triệu INSERT.
    /// </summary>
    Task<EntryScorecard> EvaluateTransientAsync(long tradingAccountId, string symbol, DateTime utcNow, CancellationToken ct = default);

    /// <summary>
    /// Chấm với thống kê trạng thái do caller cung cấp. Backtest dùng nó để gate kỷ luật nhìn
    /// thấy chính các lệnh mô phỏng thay vì đọc nhầm bảng lệnh production.
    /// </summary>
    Task<EntryScorecard> EvaluateWithStatisticsAsync(
        long tradingAccountId,
        string symbol,
        DateTime utcNow,
        TraderStatistics statistics,
        bool persistResult,
        CancellationToken ct = default,
        EngineSetting? settingsOverride = null);

    /// <summary>Chấm điểm mọi mã mà tài khoản theo dõi.</summary>
    Task<IReadOnlyList<EntryScorecard>> EvaluateAllAsync(long tradingAccountId, DateTime utcNow, CancellationToken ct = default);
}

/// <summary>
/// Một chu kỳ đánh giá vào lệnh: dựng bối cảnh → chặn theo khung giờ → chấm điểm → tính kích thước.
/// </summary>
/// <remarks>
/// KHÔNG có lời gọi mô hình ngôn ngữ nào trong lớp này, và đó là điều kiện chấp nhận SC-001:
/// vòng quyết định phải chạy trọn vẹn khi AI không được cấu hình hoặc chết hoàn toàn.
///
/// Phiếu chấm điểm được lưu MỌI LẦN, kể cả khi kết luận là không vào lệnh (FR-039, SC-012).
/// Những phiếu "không vào" mới là phần có giá trị nhất: chúng trả lời câu hỏi "tại sao hôm nay
/// hệ thống đứng ngoài", câu sẽ được hỏi nhiều nhất.
///
/// Phạm vi hiện tại dừng ở việc ghi nhật ký một lệnh <c>Planned</c>. Gửi lệnh thật đi qua
/// <c>ILiveOrderService</c> và vẫn nằm sau cổng <c>LiveTrading.Enabled</c> đang TẮT.
/// </remarks>
public sealed class SignalEvalService : ISignalEvalService
{
    /// <summary>Số nến 15m cần cho chỉ báo dài nhất (EMA200) cộng một quãng dư.</summary>
    private const int EntryCandleLimit = 300;
    private const int BiasCandleLimit = 300;
    private const int DailyCandleLimit = 120;

    /// <summary>Khung nhanh, chỉ để bắt cú MA cắt sớm hơn khung vào lệnh.</summary>
    /// <remarks>
    /// Cố định 5m thay vì suy ra từ <c>EntryTimeframe</c>: nhánh dùng nó đọc MA7/MA25 với chu kỳ
    /// đã chốt theo cách giao dịch thật, nên khung phải cố định thì chu kỳ mới có nghĩa. Một
    /// khung suy diễn sẽ làm "MA7" nghĩa khác nhau tuỳ cấu hình mà không ai nhận ra.
    /// </remarks>
    private const string FastTimeframe = "5m";

    /// <summary>Đủ cho MA25 cộng cửa sổ dò điểm cắt trên khung 5m.</summary>
    private const int FastCandleLimit = 120;

    /// <summary>
    /// Số nến dùng để đo tương quan với mã dẫn dắt. 96 nến 15m = đúng 24 giờ.
    /// </summary>
    /// <remarks>
    /// Một ngày tròn là ĐỊNH NGHĨA của "đang đi cùng pha với thị trường hôm nay", không phải một
    /// tham số khẩu vị. Ngắn hơn thì hệ số nhảy theo vài nến lẻ; dài hơn thì nó nhớ một chế độ
    /// thị trường đã kết thúc.
    /// </remarks>
    private const int LeaderCorrelationCandles = 96;

    private readonly IDailyPlanService _dailyPlan;
    private readonly ITimeGuardService _timeGuard;
    private readonly ISessionQualityProvider _sessionQuality;
    private readonly IEntryScorer _scorer;
    private readonly IPositionSizer _sizer;
    private readonly IMarketContextService _marketContext;
    private readonly IMarketContextApplier _contextApplier;
    private readonly Trading.Discipline.IDisciplineGateRunner _gates;
    private readonly Trading.Discipline.ITraderStatisticsProvider _traderStats;
    private readonly IMarketDataProvider _marketData;
    private readonly IIndicatorService _indicators;
    private readonly Trading.Structure.IStructuralLevelPlanner _levelPlanner;
    private readonly Trading.Scoring.PriceActionAnalyzer _priceAction;
    private readonly Trading.Scoring.IDirectionPolicy _directionPolicy;
    private readonly ITradeExecutionPlanner _executionPlanner;
    private readonly ISetupTriggerPolicy _setupTrigger;
    private readonly IStrategyAdmissionPolicy _strategyAdmission;
    private readonly IExecutionViabilityPolicy _executionViability;
    private readonly IClock _clock;
    private readonly IBaseRepository<EngineSetting> _settings;
    private readonly IBaseRepository<EntryScorecard> _scorecards;
    private readonly IBaseRepository<RiskSetting> _riskSettings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SignalEvalService> _logger;
    private readonly Dictionary<long, EngineSetting> _transientSettings = new();
    private readonly Dictionary<long, RiskSetting> _transientRiskSettings = new();
    private readonly Dictionary<(long AccountId, DateOnly Date), TraderStatistics> _transientStats = new();
    private readonly Dictionary<long, TraderStatistics> _transientNoTradeStats = new();
    private readonly Dictionary<(long AccountId, int Hour), SessionQuality> _transientSessionQuality = new();
    private readonly Dictionary<string, IReadOnlyList<MarketContextRecord>> _transientAiContext = new(StringComparer.OrdinalIgnoreCase);
    private (DateTime CandleClose, string Interval, IReadOnlyList<Candle> Candles)? _leaderCandles;

    public SignalEvalService(
        IDailyPlanService dailyPlan,
        ITimeGuardService timeGuard,
        ISessionQualityProvider sessionQuality,
        IEntryScorer scorer,
        IPositionSizer sizer,
        IMarketContextService marketContext,
        IMarketContextApplier contextApplier,
        Trading.Discipline.IDisciplineGateRunner gates,
        Trading.Discipline.ITraderStatisticsProvider traderStats,
        IMarketDataProvider marketData,
        IIndicatorService indicators,
        Trading.Structure.IStructuralLevelPlanner levelPlanner,
        Trading.Scoring.PriceActionAnalyzer priceAction,
        Trading.Scoring.IDirectionPolicy directionPolicy,
        ITradeExecutionPlanner executionPlanner,
        ISetupTriggerPolicy setupTrigger,
        IStrategyAdmissionPolicy strategyAdmission,
        IExecutionViabilityPolicy executionViability,
        IClock clock,
        IBaseRepository<EngineSetting> settings,
        IBaseRepository<EntryScorecard> scorecards,
        IBaseRepository<RiskSetting> riskSettings,
        IUnitOfWork unitOfWork,
        ILogger<SignalEvalService> logger)
    {
        _dailyPlan = dailyPlan;
        _timeGuard = timeGuard;
        _sessionQuality = sessionQuality;
        _scorer = scorer;
        _sizer = sizer;
        _marketContext = marketContext;
        _contextApplier = contextApplier;
        _gates = gates;
        _traderStats = traderStats;
        _marketData = marketData;
        _indicators = indicators;
        _levelPlanner = levelPlanner;
        _priceAction = priceAction;
        _directionPolicy = directionPolicy;
        _executionPlanner = executionPlanner;
        _setupTrigger = setupTrigger;
        _strategyAdmission = strategyAdmission;
        _executionViability = executionViability;
        _clock = clock;
        _settings = settings;
        _scorecards = scorecards;
        _riskSettings = riskSettings;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<IReadOnlyList<EntryScorecard>> EvaluateAllAsync(
        long tradingAccountId, DateTime utcNow, CancellationToken ct = default)
    {
        var setting = await LoadSettingAsync(tradingAccountId, ct);
        var results = new List<EntryScorecard>();

        foreach (var symbol in setting.SymbolList())
        {
            try
            {
                results.Add(await EvaluateAsync(tradingAccountId, symbol, utcNow, ct));
            }
            catch (Exception ex)
            {
                // Một mã lỗi không được kéo theo các mã còn lại.
                _logger.LogError(ex, "Lỗi chấm điểm {Symbol} cho tài khoản {AccountId}.", symbol, tradingAccountId);
            }
        }

        return results;
    }

    public async Task<EntryScorecard> EvaluateAsync(
        long tradingAccountId, string symbol, DateTime utcNow, CancellationToken ct = default)
        => await EvaluateCoreAsync(tradingAccountId, symbol, utcNow, persist: true, statisticsOverride: null, null, ct);

    public async Task<EntryScorecard> EvaluateTransientAsync(
        long tradingAccountId, string symbol, DateTime utcNow, CancellationToken ct = default)
        => await EvaluateCoreAsync(tradingAccountId, symbol, utcNow, persist: false, statisticsOverride: null, null, ct);

    public async Task<EntryScorecard> EvaluateWithStatisticsAsync(
        long tradingAccountId,
        string symbol,
        DateTime utcNow,
        TraderStatistics statistics,
        bool persistResult,
        CancellationToken ct = default,
        EngineSetting? settingsOverride = null)
    {
        ArgumentNullException.ThrowIfNull(statistics);
        return await EvaluateCoreAsync(
            tradingAccountId, symbol, utcNow, persistResult, statistics, settingsOverride, ct);
    }

    private async Task<EntryScorecard> EvaluateCoreAsync(
        long tradingAccountId,
        string symbol,
        DateTime utcNow,
        bool persist,
        TraderStatistics? statisticsOverride,
        EngineSetting? settingsOverride,
        CancellationToken ct)
    {
        var setting = settingsOverride ?? (persist
            ? await LoadSettingAsync(tradingAccountId, ct)
            : await TransientSettingAsync(tradingAccountId, ct));
        var plan = await _dailyPlan.GetCurrentAsync(tradingAccountId, ct);

        // FR-023: chưa có kế hoạch thì chặn, và không có đường nào dựng kế hoạch mặc định.
        if (plan is null)
        {
            return await SaveAsync(new EntryScorecard
            {
                TradingAccountId = tradingAccountId,
                Symbol = symbol,
                Interval = setting.EntryTimeframe,
                CandleCloseTimeUtc = utcNow,
                EvaluatedAtUtc = utcNow,
                StrategyVersion = setting.StrategyVersion,
                Outcome = ScorecardOutcome.Vetoed,
                VetoReason = VetoReason.NoDailyPlan,
                VetoDetail = "Chưa có kế hoạch ngày hợp lệ — mọi lệnh mới bị chặn (FR-023).",
                InputSnapshotJson = "{}",
            }, persist, ct);
        }

        var entryCandles = await ClosedCandlesAsync(symbol, setting.EntryTimeframe, EntryCandleLimit, ct);
        var candleClose = entryCandles.Count > 0 ? entryCandles[^1].CloseTime : utcNow;

        // FR-051: khoá logic là (Symbol, CandleCloseTimeUtc, IsBacktest). Job chạy chồng lấn
        // hoặc chạy lại bù sẽ gặp đúng cây nến cũ — trả phiếu đã có thay vì sinh bản trùng.
        var existing = persist
            ? await _scorecards
                .Get(s => s.TradingAccountId == tradingAccountId
                          && s.Symbol == symbol
                          && s.CandleCloseTimeUtc == candleClose
                          && !s.IsBacktest)
                .Include(s => s.Lines)
                .FirstOrDefaultAsync(ct)
            : null;

        if (existing is not null) return existing;

        // Hỏi cổng chặn giờ ở ĐÂY nhưng áp ở CUỐI. Trước đây chỗ này return thẳng, và phiếu sinh ra
        // rỗng hoàn toàn: 0 dòng tiêu chí, điểm thành phần bằng 0, không có entry/dừng lỗ/mục tiêu.
        // Hệ quả là blackout thành cổng DUY NHẤT mà ScorecardOutcomeReview không đo được — không có
        // ba mức giá thì không mô phỏng được, nên đúng câu hỏi "cổng này chặn đúng hay chặn nhầm"
        // lại là câu nó miễn nhiễm. Ngày 2026-08-12 có 16 phiếu như vậy, gồm cả cửa sổ CPI 90 phút.
        //
        // Chấm trước rồi chặn sau tốn thêm một lượt chấm tất định (0 lời gọi AI) và đổi lại phiếu
        // đủ dữ kiện để chạy tiếp trên giá. Việc CHẶN không hề nới ra: xem đoạn áp veto ở cuối hàm.
        var blackout = await _timeGuard.CheckAsync(tradingAccountId, symbol, utcNow, ct);

        var stats = statisticsOverride ?? (persist
            ? await SafeAsync(() => _traderStats.GetAsync(tradingAccountId, utcNow, ct), TraderStatistics.Empty, "traderStats")
            : await TransientStatsAsync(tradingAccountId, utcNow, ct));
        var riskSetting = persist
            ? await LoadRiskSettingAsync(tradingAccountId, ct)
            : await TransientRiskSettingAsync(tradingAccountId, ct);

        var market = await LoadMarketAsync(
            tradingAccountId, symbol, utcNow, candleClose, entryCandles, setting, transient: !persist, ct);

        var intradayRegime = IntradayRegimeOverridePolicy.Resolve(plan, entryCandles, setting);
        var effectivePlan = IntradayRegimeOverridePolicy.Apply(plan, intradayRegime);

        // ── Chọn chiều TRƯỚC khi chấm (§4) ──────────────────────────────
        // Trên ngày đi ngang, vị trí trong biên độ quyết định chiều nào còn được phép; để EMA
        // 4h quyết định chiều trên một ngày đan xen là tung đồng xu có trả phí.
        var candidates = _directionPolicy.Candidates(effectivePlan, setting, market.BiasCandles, market.Price);

        if (candidates.Veto is { } locationVeto)
        {
            return await SaveAsync(new EntryScorecard
            {
                TradingAccountId = tradingAccountId,
                DailyPlanId = plan.Id,
                Symbol = symbol,
                Interval = setting.EntryTimeframe,
                CandleCloseTimeUtc = candleClose,
                EvaluatedAtUtc = utcNow,
                StrategyVersion = setting.StrategyVersion,
                Outcome = ScorecardOutcome.Vetoed,
                VetoReason = locationVeto,
                VetoDetail = candidates.Detail,
                DayRiskMultiplier = effectivePlan.RiskMultiplier,
                RangePositionPercent = candidates.Range?.Percent,
                SuggestedEntry = market.Price > 0m ? market.Price : null,
                EffectiveDayRegime = effectivePlan.DayRegime,
                IsIntradayRegimeOverride = intradayRegime.IsOverride,
                IntradayRegimeReason = intradayRegime.ReasonVi,
                InputSnapshotJson = "{}",
            }, persist, ct);
        }

        // Một lần quét price action dùng cho CẢ hai chiều — bản ghi không phụ thuộc chiều lệnh.
        // Không có §2.7 thì mỗi chiều sẽ phát hiện lại điểm xoay và tính lại RSI ba lần, và chi
        // phí của việc chấm hai chiều nhân lên theo đó.
        var priceAction = _priceAction.Analyze(entryCandles, setting.SwingPivotBars, market.Price);

        var scored = new List<(TradeDirection Direction, ScoringOutcome Score)>(candidates.Allowed.Count);
        var contexts = new Dictionary<TradeDirection, ScoringContext>(candidates.Allowed.Count);

        foreach (var candidate in candidates.Allowed)
        {
            var candidateContext = BuildContext(
                symbol, utcNow, candleClose, entryCandles, market, effectivePlan, setting, candidate,
                riskSetting.MinRiskRewardRatio, priceAction) with
            { TraderStats = stats };

            contexts[candidate] = candidateContext;
            scored.Add((candidate, _scorer.Score(candidateContext)));
        }

        var v6Triggers = new Dictionary<TradeDirection, SetupTriggerDecision>();
        if (setting.StrategyVersion.UsesSidewaysV6())
        {
            foreach (var candidate in candidates.Allowed)
                v6Triggers[candidate] = _setupTrigger.Evaluate(contexts[candidate], candidates.Range);
        }

        DirectionChoice choice;
        var confirmedDirection = setting.StrategyVersion.UsesSidewaysV6()
            ? scored
                .Where(x => v6Triggers[x.Direction].Passed)
                .OrderByDescending(x => !x.Score.IsVetoed
                                        || CanTriggerOverrideLegacyVeto(
                                            x.Score, v6Triggers[x.Direction], contexts[x.Direction]))
                .ThenByDescending(x => v6Triggers[x.Direction].SetupQualityScore)
                .ThenByDescending(x => x.Score.DirectionalScore)
                .ThenBy(x => (int)x.Direction)
                .FirstOrDefault()
            : default;

        if (confirmedDirection != default)
        {
            var other = scored.FirstOrDefault(x => x.Direction != confirmedDirection.Direction);
            choice = new DirectionChoice(
                confirmedDirection.Direction,
                confirmedDirection.Score,
                other == default ? null : other.Score,
                other == default
                    ? null
                    : confirmedDirection.Score.DirectionalScore - other.Score.DirectionalScore,
                $"V6 chọn chiều {confirmedDirection.Direction} vì trigger " +
                $"{v6Triggers[confirmedDirection.Direction].SetupType} đã xác nhận " +
                $"quality={v6Triggers[confirmedDirection.Direction].SetupQualityScore}.");
        }
        else
        {
            choice = Trading.Scoring.DirectionSelector.Select(scored);
        }

        var context = contexts[choice.Direction];
        var score = choice.Score;

        // Chiều bị VỊ TRÍ loại vẫn được chấm, và chỉ để ghi vào phiếu. Trên ngày đi ngang, vị trí
        // trong biên độ chốt chiều trước khi chấm nên biên hai chiều không còn gì để so — không
        // ghi lại điểm của bên bị loại thì sau này không trả lời được câu "quy tắc biên độ có
        // đang chọn nhầm bên không", và đó đúng là câu mà lần backtest kế tiếp phải trả lời.
        var excludedScore = candidates.ExcludedOrEmpty
            .Select(d => _scorer.Score(BuildContext(
                symbol, utcNow, candleClose, entryCandles, market, effectivePlan, setting, d,
                riskSetting.MinRiskRewardRatio, priceAction) with
            { TraderStats = stats }))
            .FirstOrDefault(s => !s.IsVetoed);

        var opposite = choice.OppositeScore ?? excludedScore;

        // V3 dùng trigger làm điều kiện lõi; score cũ chỉ còn đánh giá chất lượng bối cảnh.
        // Hai veto được phép nhường cho trigger/cost gate là các phép đo mà chính trigger mới
        // cung cấp bằng chứng tốt hơn: close đã rời biên sau sweep, và gross structural RR được
        // thay bằng net RR sau chi phí. Mọi veto an toàn khác vẫn giữ nguyên.
        var trigger = setting.StrategyVersion.UsesTriggerFirst()
            ? v6Triggers.GetValueOrDefault(choice.Direction)
              ?? _setupTrigger.Evaluate(context, candidates.Range)
            : new SetupTriggerDecision(
                true,
                SetupType.LegacyV2,
                SetupTriggerState.LegacyAccepted,
                "V2 dùng score chung; core trigger setup-specific chưa được áp dụng.");
        var triggerOverridesLegacyVeto = CanTriggerOverrideLegacyVeto(score, trigger, context);
        var scoreForSizing = score;
        if (setting.StrategyVersion.UsesTriggerFirst()
            && trigger.Passed
            && (!score.IsVetoed || triggerOverridesLegacyVeto))
        {
            scoreForSizing = score with
            {
                TotalScore = Math.Max(score.TotalScore, setting.MinScoreToEnter),
                IsVetoed = false,
                VetoReason = null,
                VetoDetail = null,
            };
        }

        // Gate oversize phải so với rủi ro THỰC SỰ dự kiến của setup, không phải trần rủi ro
        // tài khoản. Tính một sizing sơ bộ với gate/AI trung tính; các hệ số chạy sau chỉ có
        // thể giảm tiếp, nên đây là cận trên an toàn và không tạo vòng phụ thuộc sizing↔gate.
        var setupSizing = setting.StrategyVersion.UsesSidewaysV6()
            ? new SetupSizingProfile(trigger.SetupType, trigger.SetupQualityScore)
            : null;
        var projectedSizing = _sizer.Calculate(
            scoreForSizing, effectivePlan, GateAggregate.Neutral, aiMultiplier: 1m, setting, setupSizing);
        var projectedRiskPercent = riskSetting.MaxRiskPerTradePercent * projectedSizing.FinalSizeR;

        // Gate kỷ luật chạy TRƯỚC bước tính kích thước: kết quả của nó là một trong ba hệ số
        // nhân, và nó cũng có quyền chặn thẳng bất kể điểm số bao nhiêu.
        var discipline = _gates.Run(new DisciplineContext
        {
            TradingAccountId = tradingAccountId,
            EvaluatedAtUtc = utcNow,
            Symbol = symbol,
            Direction = context.Direction,
            PlannedRiskPercent = projectedRiskPercent,
            ProjectedSizeR = projectedSizing.FinalSizeR,
            LeaderCorrelation = context.LeaderCorrelation,
            IsLeaderSymbol = context.IsLeaderSymbol,
            DailyPlan = effectivePlan,
            Settings = setting,
            RiskSettings = riskSetting,
            Stats = stats,
        });

        var aiMultiplier = _contextApplier.GetSizeMultiplier(
            context.ActiveAiContext, symbol, context.Direction);
        var sizing = _sizer.Calculate(
            scoreForSizing, effectivePlan, discipline.Aggregate, aiMultiplier, setting, setupSizing);

        var blocked = discipline.Aggregate.IsBlocked;

        var blockingScoreVeto = score.IsVetoed && !triggerOverridesLegacyVeto;
        var provisionalOutcome = blockingScoreVeto || blocked
            ? ScorecardOutcome.Vetoed
            : sizing.FinalSizeR > 0m ? ScorecardOutcome.Entered
            // Cỡ lệnh bằng 0 có hai nguyên nhân hoàn toàn khác nhau, và chỉ hệ số setup mới phân
            // biệt được: sizer trả về Zero() khi điểm thiếu (SetupMultiplier giữ nguyên 1) nhưng
            // trả về SetupMultiplier = 0 khi điểm đủ mà chất lượng setup dưới sàn.
            : sizing.SetupMultiplier <= 0m ? ScorecardOutcome.SetupMissing
            : ScorecardOutcome.BelowThreshold;
        var triggerBlocked = setting.StrategyVersion.UsesTriggerFirst()
                             && provisionalOutcome == ScorecardOutcome.Entered
                             && !trigger.Passed;
        var admission = _strategyAdmission.Evaluate(
            setting.StrategyVersion, trigger, score, candleClose);
        var admissionBlocked = provisionalOutcome == ScorecardOutcome.Entered
                               && trigger.Passed
                               && !admission.Passed;
        var outcome = triggerBlocked || admissionBlocked
            ? ScorecardOutcome.Vetoed
            : provisionalOutcome;

        var card = new EntryScorecard
        {
            TradingAccountId = tradingAccountId,
            DailyPlanId = plan.Id,
            Symbol = symbol,
            Interval = setting.EntryTimeframe,
            CandleCloseTimeUtc = candleClose,
            EvaluatedAtUtc = utcNow,
            Direction = context.Direction,
            StrategyVersion = setting.StrategyVersion,
            SetupType = trigger.SetupType,
            TriggerState = trigger.State,
            TriggerDetail = trigger.DetailVi,
            SetupStage = trigger.Stage,
            SetupEventId = trigger.EventId,
            SetupQualityScore = trigger.SetupQualityScore,

            TechnicalScore = score.TechnicalScore,
            MarketScore = score.MarketScore,
            LiquidityScore = score.LiquidityScore,
            DisciplinePenalty = discipline.Aggregate.ScorePenalty,
            TotalScore = Math.Clamp(score.TotalScore + discipline.Aggregate.ScorePenalty, 0, 100),

            DirectionalScore = score.DirectionalScore,
            OppositeScore = opposite?.TotalScore,
            OppositeDirectionalScore = opposite?.DirectionalScore,
            RangePositionPercent = candidates.Range?.Percent,

            Outcome = outcome,
            VetoReason = triggerBlocked
                ? VetoReason.SetupTriggerMissing
                : admissionBlocked
                    ? VetoReason.StrategyAdmissionRejected
                : (blockingScoreVeto ? score.VetoReason : null) ?? discipline.Aggregate.VetoReason,
            VetoDetail = triggerBlocked
                ? trigger.DetailVi
                : admissionBlocked
                    ? admission.DetailVi
                : (blockingScoreVeto ? score.VetoDetail : null) ?? discipline.Aggregate.Detail ?? sizing.ReasonVi,

            BaseSizeR = sizing.BaseSizeR,
            DayRiskMultiplier = sizing.DayRiskMultiplier,
            DisciplineMultiplier = sizing.DisciplineMultiplier,
            AiMultiplier = sizing.AiMultiplier,
            DataMultiplier = sizing.DataMultiplier,
            SetupSizeMultiplier = sizing.SetupMultiplier,
            AvailableMaxPoints = score.AvailableMaxPoints,
            TotalMaxPoints = score.TotalMaxPoints,
            FinalSizeR = triggerBlocked || admissionBlocked ? 0m : sizing.FinalSizeR,

            SuggestedEntry = context.CurrentPrice,
            SuggestedStopLoss = trigger.SuggestedStopLoss ?? context.PlannedStopLoss,
            SuggestedTakeProfit = trigger.SuggestedRunnerTakeProfit
                                  ?? trigger.SuggestedFirstTakeProfit
                                  ?? context.PlannedTakeProfit,
            SuggestedFirstTakeProfit = trigger.SuggestedFirstTakeProfit ?? context.PlannedFirstTakeProfit,
            SuggestedRunnerTakeProfit = trigger.SuggestedRunnerTakeProfit ?? context.PlannedRunnerTakeProfit,
            SuggestedLimitEntry = trigger.SuggestedLimitEntry ?? context.PlannedLimitEntry,
            RiskReward = RiskReward(
                context.CurrentPrice,
                trigger.SuggestedStopLoss ?? context.PlannedStopLoss,
                trigger.SuggestedRunnerTakeProfit
                ?? trigger.SuggestedFirstTakeProfit
                ?? context.PlannedTakeProfit),
            EffectiveDayRegime = effectivePlan.DayRegime,
            IsIntradayRegimeOverride = intradayRegime.IsOverride,
            IntradayRegimeReason = intradayRegime.ReasonVi,

            InputSnapshotJson = Snapshot(context, choice, opposite, candidates.Range),
        };

        foreach (var line in score.Lines)
        {
            card.Lines.Add(new EntryScorecardLine
            {
                CriterionKey = line.Key,
                Group = line.Group,
                MaxPoints = line.MaxPoints,
                AwardedPoints = line.Result.DataAvailable ? line.Result.AwardedPoints : 0,
                IsHardVeto = line.Result.IsHardVeto,
                Reason = line.Result.Reason,
                DataAvailable = line.Result.DataAvailable,
                IsApproximation = line.Result.IsApproximation,
                StateCode = line.Result.StateCode,
            });
        }

        // Mỗi gate một dòng phiếu, kể cả gate cho qua. Chỉ ghi gate đang chặn thì phiếu không
        // trả lời được câu "những rào nào đã được kiểm và đều ổn".
        foreach (var gate in discipline.Lines)
        {
            card.Lines.Add(new EntryScorecardLine
            {
                CriterionKey = gate.Key,
                Group = ScoreGroup.Discipline,
                MaxPoints = 0,
                AwardedPoints = Math.Min(0, gate.Result.ScorePenalty),
                IsHardVeto = gate.Result.Action is GateAction.BlockTrade or GateAction.StopForDay,
                Reason = gate.Result.Reason,
                DataAvailable = true,
            });
        }

        // Cổng chặn giờ chạy ngoài _gates nên nó không tự có dòng — thêm tay, kể cả khi cho qua,
        // theo đúng nguyên tắc ngay trên. Không có dòng này thì phiếu không phân biệt được "đã kiểm
        // giờ và ngoài mọi cửa sổ" với "chưa bao giờ kiểm giờ".
        card.Lines.Add(new EntryScorecardLine
        {
            CriterionKey = "discipline.time_guard",
            Group = ScoreGroup.Discipline,
            MaxPoints = 0,
            AwardedPoints = 0,
            IsHardVeto = blackout.IsBlocked,
            Reason = blackout.ReasonVi ?? "Ngoài mọi cửa sổ chặn giờ.",
            DataAvailable = true,
        });

        // P0 đo economics cho cả V2 nhưng chỉ V3 được quyền dùng nó làm gate. Planner thuần nên
        // gọi ở đây và gọi lại ở backtest/live cho cùng kết quả; không có nhánh mô phỏng riêng.
        //
        // Phiếu chạy thật đo trên `PlanLive` — kế hoạch mà bộ đặt lệnh thực hiện được nguyên
        // văn — còn phiếu backtest đo trên `Plan` vì trình mô phỏng thực hiện đủ nhiều chân.
        // Trước 2026-08-14 cả hai cùng dùng `Plan`, nên cổng chi phí của đường thật chấm một kế
        // hoạch 2 chân trong khi sàn chỉ nhận một lệnh thị trường: netRR bị đo cao hơn thực tế
        // 26% trên phiếu 13:31 ngày 14/08. Xem chú thích `ITradeExecutionPlanner.PlanLive`.
        if (card.Outcome == ScorecardOutcome.Entered && card.Direction is { } plannedDirection)
        {
            var execution = card.IsBacktest
                ? _executionPlanner.Plan(card, effectivePlan, setting)
                : _executionPlanner.PlanLive(card);

            if (execution is null)
            {
                // Không dựng nổi kế hoạch chạy thật ⟹ ScorecardExecutionService cũng sẽ bỏ qua
                // phiếu này. Kết luận "vào lệnh" mà không có lệnh nào là trạng thái sai lệch
                // nhất có thể ghi vào nhật ký, nên chặn thẳng thay vì để nó nằm im ở Entered.
                _logger.LogWarning(
                    "Phiếu {Symbol} kết luận vào lệnh nhưng thiếu mức giá để dựng kế hoạch chạy thật "
                    + "(entry={Entry}, sl={Sl}, tp={Tp}) — chuyển sang veto.",
                    symbol, card.SuggestedEntry, card.SuggestedStopLoss, card.SuggestedTakeProfit);

                card.Outcome = ScorecardOutcome.Vetoed;
                card.VetoReason = VetoReason.InsufficientRoom;
                card.VetoDetail = "Thiếu mức giá vào/dừng lỗ/chốt lời nên không dựng được lệnh thật.";
                card.FinalSizeR = 0m;
            }
            else
            {
                var economics = _executionViability.Evaluate(
                    execution,
                    plannedDirection,
                    setting,
                    enforceV3Gates: setting.StrategyVersion.UsesTriggerFirst(),
                    setupType: card.SetupType);

                card.ExpectedCostR = economics.ExpectedCostR;
                card.NetRiskReward = economics.NetRiskReward;
                card.StopDistanceBps = economics.StopDistanceBps;

                if (!economics.Passed)
                {
                    card.Outcome = ScorecardOutcome.Vetoed;
                    card.VetoReason = VetoReason.ExecutionCostTooHigh;
                    card.VetoDetail = economics.DetailVi;
                    card.TriggerState = SetupTriggerState.CostRejected;
                    card.TriggerDetail = economics.DetailVi;
                    card.FinalSizeR = 0m;
                }
            }
        }

        // ── Cổng chặn giờ: áp SAU CÙNG, thắng mọi lý do khác ────────────────
        // Nó là ràng buộc ngoài cùng — trong cửa sổ tin thì không setup nào được vào, bất kể phễu
        // dừng ở đâu. Vì vậy nó ghi đè VetoReason thay vì xếp hàng sau, và FinalSizeR về 0.
        //
        // Đặt sau khối economics là có chủ ý, không phải tiện tay: khối đó chỉ chạy khi phiếu đang
        // là Entered, nên trên phiếu bị chặn giờ, ExpectedCostR khác null chính là dấu hiệu "phiếu
        // này LẼ RA đã vào lệnh". Không có dấu đó thì sau này không tách được phiếu bị blackout
        // chặn thật khỏi phiếu dù sao cũng trượt, và bảng thống kê theo cổng sẽ đổ hết cho blackout.
        if (blackout.IsBlocked)
        {
            var alsoBlockedBy = card.Outcome == ScorecardOutcome.Vetoed && card.VetoReason is { } prior
                ? $" Ngoài ra phiếu cũng bị chặn bởi {prior}: {card.VetoDetail}"
                : null;

            card.Outcome = ScorecardOutcome.Vetoed;
            card.VetoReason = VetoReason.InBlackoutWindow;
            card.VetoDetail = (blackout.ReasonVi ?? "Đang trong cửa sổ chặn giờ.") + alsoBlockedBy;
            card.FinalSizeR = 0m;
        }

        _logger.LogDebug(
            "Chấm điểm xong. symbol={Symbol} candleCloseUtc={CandleClose:o} direction={Direction} " +
            "score={Score} directional={Directional} margin={Margin} outcome={Outcome} veto={Veto} finalSizeR={FinalSizeR}",
            symbol, candleClose, context.Direction, score.TotalScore, score.DirectionalScore,
            choice.Margin, card.Outcome, card.VetoReason, card.FinalSizeR);

        return await SaveAsync(card, persist, ct);
    }

    private static bool CanTriggerOverrideLegacyVeto(
        ScoringOutcome score,
        SetupTriggerDecision trigger,
        ScoringContext context)
    {
        if (!trigger.Passed || !score.IsVetoed) return false;

        if (score.VetoReason == VetoReason.NotAtRangeEdge
            && trigger.SetupType is SetupType.RangeRejection
                or SetupType.RectangleRangeFade
                or SetupType.RectangleBreakout
                or SetupType.TriangleBreakout)
            return true;

        return score.VetoReason == VetoReason.InsufficientRoom
               && context.PlannedStopLoss is not null
               && context.PlannedTakeProfit is not null
               && context.PlannedStopLoss != context.CurrentPrice;
    }

    // ── Dựng bối cảnh ───────────────────────────────────────────────────

    /// <summary>
    /// Mọi thứ KHÔNG phụ thuộc chiều lệnh. Nạp đúng một lần cho cả hai chiều.
    /// </summary>
    /// <remarks>
    /// Tách khỏi <see cref="BuildContext"/> chính là điều làm cho §4 khả thi: chấm hai chiều mà
    /// vẫn chỉ một lượt gọi sàn. Nếu dựng nguyên bối cảnh hai lần thì mỗi cây nến sẽ tốn hai lần
    /// đọc ticker, funding, sổ lệnh và tương quan — chi phí ngoài mạng, không phải CPU.
    /// </remarks>
    private sealed record MarketSnapshot(
        IReadOnlyList<Candle> BiasCandles,
        IReadOnlyList<Candle> DailyCandles,
        IReadOnlyList<Candle> FastCandles,
        decimal Price,
        decimal Atr,
        IReadOnlyList<MarketContextRecord> ActiveAiContext,
        FundingSnapshot? Funding,
        OpenInterestSeries? OpenInterest,
        DepthSnapshot? Depth,
        LongShortRatio? LongShort,
        decimal? LeaderCorrelation,
        SessionQuality? SessionQuality);

    private async Task<MarketSnapshot> LoadMarketAsync(
        long tradingAccountId, string symbol, DateTime utcNow, DateTime candleClose,
        IReadOnlyList<Candle> entryCandles, EngineSetting setting, bool transient, CancellationToken ct)
    {
        var biasCandles = await ClosedCandlesAsync(symbol, setting.BiasTimeframe, BiasCandleLimit, ct);
        var dailyCandles = await ClosedCandlesAsync(symbol, "1d", DailyCandleLimit, ct);

        // Khung nhanh chỉ phục vụ nhánh vào-ngay-khi-MA-cắt. Lỗi nguồn ⟹ rỗng, và nhánh đó tự
        // đứng ngoài — không được để nó kéo sập cả vòng chấm điểm của khung 15m.
        var fastCandles = await ClosedCandlesAsync(symbol, FastTimeframe, FastCandleLimit, ct);

        var price = await SafeAsync(async () => (await _marketData.GetTickerAsync(symbol, ct)).Price, 0m, "ticker");
        if (price <= 0m && entryCandles.Count > 0) price = entryCandles[^1].Close;

        return new MarketSnapshot(
            BiasCandles: biasCandles,
            DailyCandles: dailyCandles,
            FastCandles: fastCandles,
            Price: price,
            Atr: _indicators.Atr(entryCandles, 14) ?? 0m,
            ActiveAiContext: transient
                ? await TransientAiContextAsync(symbol, ct)
                : await SafeAsync(
                    () => _marketContext.GetActiveAsync(symbol, ct),
                    Array.Empty<MarketContextRecord>(), "aiContext"),
            Funding: await SafeAsync(() => _marketData.GetFundingAsync(symbol, ct), null, "funding"),
            OpenInterest: await SafeAsync(() => _marketData.GetOpenInterestHistAsync(symbol, "1h", 30, ct), null, "openInterest"),
            Depth: await SafeAsync(() => _marketData.GetDepthAsync(symbol, 100, ct), null, "depth"),
            LongShort: await SafeAsync(() => _marketData.GetGlobalLongShortRatioAsync(symbol, "1h", ct), null, "longShort"),
            LeaderCorrelation: await LeaderCorrelationAsync(
                symbol, candleClose, entryCandles, setting.EntryTimeframe, ct),
            SessionQuality: transient
                ? await TransientSessionQualityAsync(tradingAccountId, utcNow, ct)
                : await SafeAsync(
                    () => _sessionQuality.GetAsync(tradingAccountId, utcNow, ct)!, null, "sessionQuality"));
    }

    /// <summary>Bối cảnh cho MỘT chiều cụ thể. Thuần: không I/O, không đồng hồ.</summary>
    private ScoringContext BuildContext(
        string symbol, DateTime utcNow, DateTime candleClose, IReadOnlyList<Candle> entryCandles,
        MarketSnapshot market, DailyPlan plan, EngineSetting setting, TradeDirection direction,
        decimal minRiskReward, PriceActionSignals priceAction)
    {
        // Mức dừng lỗ/mục tiêu phải dựng RIÊNG cho từng chiều: điểm phủ định của một lệnh mua là
        // đáy xoay dưới giá, của một lệnh bán là đỉnh xoay trên giá. Dùng chung một bộ mức cho cả
        // hai chiều sẽ chấm `technical.structural_room` bằng cấu trúc của chiều kia.
        var levels = market.Atr > 0m && market.Price > 0m
            ? _levelPlanner.Plan(new Trading.Structure.StructuralLevelRequest
            {
                Entry = market.Price,
                Direction = direction,
                Atr = market.Atr,
                Settings = setting,
                EntryCandles = entryCandles,
                BiasCandles = market.BiasCandles,
                DailyCandles = market.DailyCandles,
                FallbackRiskReward = minRiskReward,
            })
            : null;

        var ema20 = _indicators.Ema(entryCandles.Select(c => c.Close).ToList(), 20);
        var limitEntry = plan.DayRegime == DayRegime.Range
            ? (direction == TradeDirection.Long
                ? market.Price - market.Atr * 0.25m
                : market.Price + market.Atr * 0.25m)
            : levels?.RetestEntry ?? ema20;

        // Lệnh chờ phải cải thiện giá và vẫn nằm đúng phía stop. Nếu EMA/pivot đã ở phía bất
        // lợi thì không gọi nó là retest; để planner lùi về market thay vì đặt lệnh vô nghĩa.
        var validLimit = limitEntry is { } candidate && levels is { } planned
            && (direction == TradeDirection.Long
                ? candidate < market.Price && candidate > planned.StopLoss
                : candidate > market.Price && candidate < planned.StopLoss);

        return new ScoringContext
        {
            Symbol = symbol,
            EvaluatedAtUtc = utcNow,
            CandleCloseTimeUtc = candleClose,
            Direction = direction,
            EntryCandles = entryCandles,
            BiasCandles = market.BiasCandles,
            DailyCandles = market.DailyCandles,
            FastCandles = market.FastCandles,
            CurrentPrice = market.Price,
            DailyPlan = plan,
            Settings = setting,
            TraderStats = TraderStatistics.Empty,
            ActiveAiContext = market.ActiveAiContext,

            Funding = market.Funding,
            OpenInterest = market.OpenInterest,
            Depth = market.Depth,
            LongShort = market.LongShort,
            LeaderCorrelation = market.LeaderCorrelation,
            SessionQuality = market.SessionQuality,
            PriceAction = priceAction,

            PlannedStopLoss = levels?.StopLoss,
            PlannedTakeProfit = levels?.TakeProfit,
            PlannedFirstTakeProfit = levels?.FirstTakeProfit,
            PlannedRunnerTakeProfit = levels?.RunnerTakeProfit,
            PlannedLimitEntry = validLimit ? limitEntry : null,
            StructuralLevels = levels,
        };
    }

    /// <summary>
    /// Tương quan lợi suất với mã dẫn dắt, ghép theo THỜI ĐIỂM ĐÓNG NẾN.
    /// </summary>
    /// <remarks>
    /// Trước đây trường này được gán cứng <c>null</c>, nên <c>market.leader_correlation</c> luôn
    /// trả "thiếu dữ liệu" ⟹ 0/4 điểm cho mọi mã không phải BTC, trong khi BTC nhận đủ 4/4 qua
    /// nhánh <c>IsLeaderSymbol</c>. Kết quả là ETHUSDT khởi điểm thấp hơn BTCUSDT đúng 4 điểm
    /// trên thang điểm, vĩnh viễn — một thiên lệch chưa ai chọn, và một tiêu chí chết vẫn tiêu CPU
    /// mỗi lần chấm.
    ///
    /// Ghép theo <c>CloseTime</c> chứ không lấy N nến cuối của mỗi chuỗi: kho nến có thể thiếu
    /// cây, và khi đó hai chuỗi lệch pha nhau một nến sẽ cho ra một hệ số tương quan hoàn toàn
    /// bịa đặt mà không có dấu hiệu gì.
    /// </remarks>
    private async Task<decimal?> LeaderCorrelationAsync(
        string symbol,
        DateTime candleClose,
        IReadOnlyList<Candle> entryCandles,
        string interval,
        CancellationToken ct)
    {
        // Chính mã dẫn dắt thì không có gì để đo — tiêu chí đã xử lý bằng nhánh riêng.
        if (string.Equals(symbol, ScoringContext.LeaderSymbol, StringComparison.OrdinalIgnoreCase))
            return null;

        if (entryCandles.Count < LeaderCorrelationCandles) return null;

        var leader = await LeaderCandlesAsync(candleClose, interval, ct);
        if (leader.Count == 0) return null;

        var leaderByTime = new Dictionary<DateTime, decimal>(leader.Count);
        foreach (var c in leader) leaderByTime[c.CloseTime] = c.Close;

        var aligned = entryCandles
            .Where(c => leaderByTime.ContainsKey(c.CloseTime))
            .TakeLast(LeaderCorrelationCandles)
            .ToList();

        if (aligned.Count < LeaderCorrelationCandles) return null;

        var symbolReturns = _indicators.LogReturns(aligned.Select(c => c.Close).ToList());
        var leaderReturns = _indicators.LogReturns(aligned.Select(c => leaderByTime[c.CloseTime]).ToList());

        return _indicators.Correlation(symbolReturns, leaderReturns);
    }

    /// <summary>
    /// Nến mã dẫn dắt, nhớ đúng MỘT mốc thời gian.
    /// </summary>
    /// <remarks>
    /// Một ô nhớ là đủ cho cả hai môi trường vì đồng hồ chỉ tiến: chạy thật chấm mỗi mã một lần
    /// cho mỗi cây nến, kiểm thử lịch sử duyệt tuần tự. Nhờ vậy một vòng quét N mã chỉ tốn một
    /// lần đọc nến BTC thay vì N lần — khác biệt đáng kể khi backtest chạy 70.000 mốc.
    /// </remarks>
    private async Task<IReadOnlyList<Candle>> LeaderCandlesAsync(
        DateTime candleClose, string interval, CancellationToken ct)
    {
        if (_leaderCandles is { } cached && cached.CandleClose == candleClose && cached.Interval == interval)
            return cached.Candles;

        var candles = await ClosedCandlesAsync(ScoringContext.LeaderSymbol, interval, EntryCandleLimit, ct);
        _leaderCandles = (candleClose, interval, candles);
        return candles;
    }

    private static decimal? RiskReward(decimal entry, decimal? stopValue, decimal? targetValue)
    {
        if (stopValue is not { } stop || targetValue is not { } target) return null;

        var risk = Math.Abs(entry - stop);
        return risk <= 0m ? null : Math.Abs(target - entry) / risk;
    }

    private static string Snapshot(
        ScoringContext context, Trading.Scoring.DirectionChoice choice,
        ScoringOutcome? opposite, RangeLocation? range) => JsonSerializer.Serialize(new
    {
        context.Symbol,
        context.CurrentPrice,
        Direction = context.Direction.ToString(),
        DirectionReason = choice.ReasonVi,
        choice.Margin,
        DirectionalScore = choice.Score.DirectionalScore,
        DirectionalMaxPoints = choice.Score.DirectionalMaxPoints,
        OppositeDirectionalScore = opposite?.DirectionalScore,
        RangeLow = range?.Low,
        RangeHigh = range?.High,
        RangePercent = range?.Percent,
        EntryCandles = context.EntryCandles.Count,
        BiasCandles = context.BiasCandles.Count,
        Funding = context.Funding?.LastFundingRate,
        SpreadBps = context.Depth?.SpreadBps,
        context.LeaderCorrelation,
        SessionScore = context.SessionQuality?.Score,
        context.PlannedStopLoss,
        context.PlannedTakeProfit,
        PlanAtrPercentile = context.DailyPlan.AtrPercentile,
        PlanRiskMultiplier = context.DailyPlan.RiskMultiplier,
    });

    // ── Hạ tầng ─────────────────────────────────────────────────────────

    private async Task<EngineSetting> LoadSettingAsync(long tradingAccountId, CancellationToken ct) =>
        await _settings.Get(s => s.TradingAccountId == tradingAccountId).AsNoTracking().FirstOrDefaultAsync(ct)
        ?? throw new InvalidOperationException($"Tài khoản {tradingAccountId} chưa có cấu hình engine (EngineSetting).");

    private async Task<RiskSetting> LoadRiskSettingAsync(long tradingAccountId, CancellationToken ct) =>
        await _riskSettings.Get(r => r.TradingAccountId == tradingAccountId).AsNoTracking().FirstOrDefaultAsync(ct)
        ?? new RiskSetting { TradingAccountId = tradingAccountId };

    private async Task<EngineSetting> TransientSettingAsync(long accountId, CancellationToken ct)
    {
        if (_transientSettings.TryGetValue(accountId, out var cached)) return cached;
        cached = await LoadSettingAsync(accountId, ct);
        _transientSettings[accountId] = cached;
        return cached;
    }

    private async Task<RiskSetting> TransientRiskSettingAsync(long accountId, CancellationToken ct)
    {
        if (_transientRiskSettings.TryGetValue(accountId, out var cached)) return cached;
        cached = await LoadRiskSettingAsync(accountId, ct);
        _transientRiskSettings[accountId] = cached;
        return cached;
    }

    private async Task<TraderStatistics> TransientStatsAsync(long accountId, DateTime utcNow, CancellationToken ct)
    {
        if (_transientNoTradeStats.TryGetValue(accountId, out var noTrades)) return noTrades;

        var key = (accountId, DateOnly.FromDateTime(utcNow));
        if (_transientStats.TryGetValue(key, out var cached)) return cached;
        cached = await SafeAsync(() => _traderStats.GetAsync(accountId, utcNow, ct), TraderStatistics.Empty, "traderStats");
        if (cached.ClosedTradeCount == 0 && cached.TradesToday == 0)
            _transientNoTradeStats[accountId] = cached;
        _transientStats[key] = cached;
        return cached;
    }

    private async Task<SessionQuality?> TransientSessionQualityAsync(long accountId, DateTime utcNow, CancellationToken ct)
    {
        var key = (accountId, utcNow.Hour);
        if (_transientSessionQuality.TryGetValue(key, out var cached)) return cached;
        cached = await SafeAsync(() => _sessionQuality.GetAsync(accountId, utcNow, ct)!, null, "sessionQuality");
        if (cached is not null) _transientSessionQuality[key] = cached;
        return cached;
    }

    private async Task<IReadOnlyList<MarketContextRecord>> TransientAiContextAsync(string symbol, CancellationToken ct)
    {
        if (_transientAiContext.TryGetValue(symbol, out var cached)) return cached;
        cached = await SafeAsync(
            () => _marketContext.GetActiveAsync(symbol, ct),
            Array.Empty<MarketContextRecord>(), "aiContext");
        _transientAiContext[symbol] = cached;
        return cached;
    }

    /// <summary>Nến đã đóng. Cắt đuôi nến hở là chốt chặn duy nhất chống lỗi repaint (FR-001).</summary>
    private async Task<IReadOnlyList<Candle>> ClosedCandlesAsync(
        string symbol, string interval, int limit, CancellationToken ct)
    {
        var candles = await SafeAsync(
            () => _marketData.GetCandlesAsync(symbol, interval, limit, ct),
            (IReadOnlyList<Candle>)Array.Empty<Candle>(), $"candles:{interval}");

        return candles.ClosedOnly(_clock);
    }

    private async Task<T> SafeAsync<T>(Func<Task<T>> call, T fallback, string source)
    {
        try
        {
            return await call();
        }
        catch (Exception ex)
        {
            // Nguồn chết ⟹ tiêu chí liên quan nhận 0 điểm (FR-006), chu kỳ đánh giá vẫn chạy.
            _logger.LogWarning(ex, "Nguồn {Source} lỗi khi dựng bối cảnh chấm điểm.", source);
            return fallback;
        }
    }

    private async Task<EntryScorecard> SaveAsync(EntryScorecard card, bool persist, CancellationToken ct)
    {
        if (!persist)
        {
            card.IsBacktest = true;
            return card;
        }

        await _scorecards.AddAsync(card);
        await _unitOfWork.CommitAsync(ct);
        return card;
    }
}
