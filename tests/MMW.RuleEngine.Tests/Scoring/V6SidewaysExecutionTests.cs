using MMW.Application.MarketData.Models;
using MMW.Application.Trading.Execution;
using MMW.Application.Trading.Scoring;
using MMW.Application.Trading.Sizing;
using MMW.Application.Trading.Structure;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.Scoring;

public sealed class V6SidewaysExecutionTests
{
    private readonly SidewaysPatternAnalyzer _patterns = new(ScoringFixtures.Swings);

    [Fact]
    public void Rectangle_duoc_dung_tu_nen_dung_truoc_event()
    {
        var candles = RectangleBars(32);
        var settings = V6Settings();

        var pattern = _patterns.Detect(
            candles, candles.Count, settings, atr: 3m, SidewaysPatternKind.Rectangle);

        Assert.NotNull(pattern);
        Assert.Equal(SidewaysPatternKind.Rectangle, pattern!.Kind);
        Assert.True(pattern.FloorTouches >= 2);
        Assert.True(pattern.UpperTouches >= 2);
        Assert.True(pattern.ContainmentPercent >= settings.V6PatternContainmentPercent);
    }

    [Fact]
    public void Detector_khong_doi_ket_qua_khi_caller_gan_them_nen_tuong_lai_nhung_giu_endExclusive()
    {
        var candles = RectangleBars(32);
        var settings = V6Settings();
        var before = _patterns.Detect(
            candles, 32, settings, atr: 3m, SidewaysPatternKind.Rectangle);
        var at = candles[^1].CloseTime.AddTicks(1);
        candles.Add(Bar(at, 100m, 150m, 50m, 120m, 1_000m));

        var after = _patterns.Detect(
            candles, 32, settings, atr: 3m, SidewaysPatternKind.Rectangle);

        Assert.Equal(before, after);
    }

    [Fact]
    public void Range_fade_cho_phep_sweep_mot_nen_confirmation_o_nen_sau()
    {
        var candles = RectangleBars(32);
        var at = candles[^1].CloseTime.AddTicks(1);
        candles.Add(Bar(at, open: 98m, high: 99m, low: 95m, close: 96.5m, volume: 120m));
        at = candles[^1].CloseTime.AddTicks(1);
        candles.Add(Bar(at, open: 96.5m, high: 99.5m, low: 96.2m, close: 99m, volume: 160m));

        var settings = V6Settings(s =>
        {
            s.V6RangeConfirmationMinRelativeVolume = 0.8m;
            s.V6MinSetupQuality = 50;
        });
        var policy = Policy();
        var context = ScoringFixtures.Context(
            entry: candles,
            direction: TradeDirection.Long,
            plan: ScoringFixtures.Plan(regime: DayRegime.Range),
            settings: settings);

        var result = policy.Evaluate(context, null);

        Assert.True(result.Passed, result.DetailVi);
        Assert.Equal(SetupType.RectangleRangeFade, result.SetupType);
        Assert.Equal(SetupFunnelStage.Confirmed, result.Stage);
        Assert.NotNull(result.SuggestedStopLoss);
        Assert.NotNull(result.SuggestedFirstTakeProfit);
        Assert.NotNull(result.SuggestedRunnerTakeProfit);
    }

    [Fact]
    public void Confirmation_khong_duoc_phat_lai_o_nen_ke_tiep()
    {
        var candles = RectangleBars(32);
        var at = candles[^1].CloseTime.AddTicks(1);
        candles.Add(Bar(at, 98m, 99m, 95m, 96.5m, 120m));
        at = candles[^1].CloseTime.AddTicks(1);
        candles.Add(Bar(at, 96.5m, 99.5m, 96.2m, 99m, 160m));
        at = candles[^1].CloseTime.AddTicks(1);
        candles.Add(Bar(at, 99m, 101m, 98.8m, 100.5m, 140m));

        var settings = V6Settings(s => s.V6MinSetupQuality = 50);
        var result = Policy().Evaluate(
            ScoringFixtures.Context(
                entry: candles,
                direction: TradeDirection.Long,
                plan: ScoringFixtures.Plan(regime: DayRegime.Range),
                settings: settings),
            null);

        Assert.False(result.Passed);
    }

    [Fact]
    public void Triangle_co_hep_duoc_nhan_dien_tu_pivot_da_xac_nhan()
    {
        var candles = TriangleBars(36);
        var settings = V6Settings(s =>
        {
            s.SwingPivotBars = 1;
            s.V6PatternLookbackBars = 32;
            s.V6PatternContainmentPercent = 70m;
        });

        var pattern = _patterns.Detect(
            candles, candles.Count, settings, atr: 2m, SidewaysPatternKind.Triangle);

        Assert.NotNull(pattern);
        Assert.Equal(SidewaysPatternKind.Triangle, pattern!.Kind);
        Assert.True(pattern.EndWidth < pattern.InitialWidth);
    }

    [Fact]
    public void Triangle_breakout_chi_xac_nhan_sau_khi_nen_dong_ngoai_bien_co_volume()
    {
        var candles = TriangleBars(32);
        var at = candles[^1].CloseTime.AddTicks(1);
        candles.Add(Bar(at, 102m, 107m, 101.5m, 106.5m, 220m));
        var settings = V6Settings(s =>
        {
            s.SwingPivotBars = 1;
            s.V6PatternContainmentPercent = 70m;
            s.V6MinSetupQuality = 50;
            s.V6BreakoutMinRelativeVolume = 1.2m;
        });

        var result = Policy().Evaluate(
            ScoringFixtures.Context(
                entry: candles,
                direction: TradeDirection.Long,
                plan: ScoringFixtures.Plan(regime: DayRegime.Range),
                settings: settings),
            null);

        Assert.True(result.Passed, result.DetailVi);
        Assert.Equal(SetupType.TriangleBreakout, result.SetupType);
        Assert.Equal(SetupFunnelStage.Confirmed, result.Stage);
    }

    [Fact]
    public void V6_range_size_bi_cap_va_nhan_quality()
    {
        var settings = V6Settings();
        var score = Score(90);
        var result = new ScoreBasedPositionSizer().Calculate(
            score,
            ScoringFixtures.Plan(),
            GateAggregate.Neutral,
            1m,
            settings,
            new SetupSizingProfile(SetupType.RectangleRangeFade, 75));

        Assert.Equal(settings.V6RangeRiskCap, result.BaseSizeR);
        Assert.Equal(settings.V6QualityFullMultiplier, result.SetupMultiplier);
        Assert.Equal(0.45m, result.FinalSizeR);
    }

    [Fact]
    public void Planner_range_V6_chot_midpoint_va_giu_runner_toi_bien_doi_dien()
    {
        var settings = V6Settings();
        var plan = ScoringFixtures.Plan(regime: DayRegime.Range);
        var card = new EntryScorecard
        {
            Outcome = ScorecardOutcome.Entered,
            Direction = TradeDirection.Long,
            StrategyVersion = TradingStrategyVersion.AdaptiveSidewaysV6,
            SetupType = SetupType.RectangleRangeFade,
            SuggestedEntry = 100m,
            SuggestedStopLoss = 96m,
            SuggestedLimitEntry = 98m,
            SuggestedFirstTakeProfit = 104m,
            SuggestedRunnerTakeProfit = 108m,
            DailyPlan = plan,
        };

        var execution = new TradeExecutionPlanner().Plan(card, plan, settings);

        Assert.Equal("RectangleRangeFadeV6", execution.Mode);
        Assert.Equal(2, execution.Entries.Count);
        Assert.Equal(104m, execution.FirstTakeProfit);
        Assert.Equal(108m, execution.RunnerTakeProfit);
        Assert.Equal(0.60m, execution.FirstTakeProfitFraction);
        Assert.True(execution.MoveRunnerStopToBreakeven);
    }

    /// <summary>
    /// Hai cách để cỡ lệnh về 0 phải PHÂN BIỆT ĐƯỢC qua <c>SetupMultiplier</c>.
    /// </summary>
    /// <remarks>
    /// Đây là thứ mà nhãn <see cref="ScorecardOutcome.SetupMissing"/> dựa vào. Nếu hai đường đều
    /// trả về cùng một hệ số thì nhãn kia sẽ nói dối một cách âm thầm — không có gì báo lỗi, chỉ
    /// là mọi phiếu "đủ điểm nhưng thiếu kèo" tiếp tục bị đếm nhầm vào "điểm thấp". Kiểm ở đây
    /// chứ không ở lớp gọi, vì lớp gọi chỉ đọc lại con số này.
    /// </remarks>
    [Fact]
    public void Diem_thieu_va_setup_thieu_cho_hai_he_so_setup_khac_nhau()
    {
        var settings = V6Settings(s => s.V6MinSetupQuality = 50);
        var sizer = new ScoreBasedPositionSizer();
        var plan = ScoringFixtures.Plan();

        var lowScore = sizer.Calculate(
            Score(settings.MinScoreToEnter - 10), plan, GateAggregate.Neutral, 1m, settings,
            new SetupSizingProfile(SetupType.MaPullback, 80));

        var noSetup = sizer.Calculate(
            Score(71), plan, GateAggregate.Neutral, 1m, settings,
            new SetupSizingProfile(SetupType.MaPullback, 0));

        Assert.Equal(0m, lowScore.FinalSizeR);
        Assert.Equal(0m, noSetup.FinalSizeR);

        // Đường điểm-thiếu thoát SỚM, trước cả khi chạm tới hệ số setup — nên nó giữ nguyên 1.
        Assert.Equal(1m, lowScore.SetupMultiplier);
        Assert.Equal(0m, noSetup.SetupMultiplier);
    }

    private static SetupTriggerPolicy Policy() => new(
        ScoringFixtures.Structure,
        new SidewaysPatternAnalyzer(ScoringFixtures.Swings), ScoringFixtures.Htf, ScoringFixtures.Swings);

    private static EngineSetting V6Settings(Action<EngineSetting>? configure = null) =>
        ScoringFixtures.Settings(s =>
        {
            s.StrategyVersion = TradingStrategyVersion.AdaptiveSidewaysV6;
            s.V6PatternLookbackBars = 32;
            s.V6PatternContainmentPercent = 70m;
            s.V6RectangleMinWidthAtr = 1m;
            s.V6RectangleMaxWidthAtr = 10m;
            configure?.Invoke(s);
        });

    private static ScoringOutcome Score(int total) => new(
        total, total, 0, 0, 0, false, null, null, Array.Empty<ScoredLine>());

    private static List<Candle> RectangleBars(int count)
    {
        var closes = new[] { 97m, 99m, 101m, 103m, 101m, 99m };
        var start = ScoringFixtures.Now.AddMinutes(-15 * count);
        return Enumerable.Range(0, count)
            .Select(i =>
            {
                var close = closes[i % closes.Length];
                return Bar(start.AddMinutes(15 * i), close - 0.5m, close + 1m, close - 1m, close, 100m);
            })
            .ToList();
    }

    private static List<Candle> TriangleBars(int count)
    {
        var start = ScoringFixtures.Now.AddMinutes(-15 * count);
        var bars = new List<Candle>(count);
        for (var i = 0; i < count; i++)
        {
            var progress = (decimal)i / Math.Max(1, count - 1);
            var amplitude = 6m - progress * 4m;
            var phase = i % 4;
            var close = phase switch
            {
                1 => 100m + amplitude,
                3 => 100m - amplitude,
                _ => 100m,
            };
            bars.Add(Bar(start.AddMinutes(15 * i), close - 0.25m, close + 0.5m, close - 0.5m, close, 100m));
        }
        return bars;
    }

    private static Candle Bar(
        DateTime openTime,
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        decimal volume) => new(
        openTime,
        open,
        high,
        low,
        close,
        volume,
        openTime.AddMinutes(15).AddTicks(-1));
}

public sealed class V5AdmissionTests
{
    [Fact]
    public void V5_loai_TrendPullback_du_trigger_da_confirm()
    {
        var trigger = new SetupTriggerDecision(
            true, SetupType.TrendPullback, SetupTriggerState.Confirmed, "ok");

        var result = new StrategyAdmissionPolicy().Evaluate(
            TradingStrategyVersion.CalibratedV5,
            trigger,
            Score(),
            new DateTime(2026, 8, 7, 10, 0, 0, DateTimeKind.Utc));

        Assert.False(result.Passed);
    }

    [Fact]
    public void V5_loai_khi_hai_tieu_chi_exhaustion_dat_toi_da()
    {
        var trigger = new SetupTriggerDecision(
            true, SetupType.StrongTrendBreakout, SetupTriggerState.Confirmed, "ok");

        var result = new StrategyAdmissionPolicy().Evaluate(
            TradingStrategyVersion.CalibratedV5,
            trigger,
            Score(exhaustion: 2),
            new DateTime(2026, 8, 7, 10, 0, 0, DateTimeKind.Utc));

        Assert.False(result.Passed);
        Assert.Equal(2, result.ExhaustionCount);
    }

    private static ScoringOutcome Score(int exhaustion = 0)
    {
        var keys = new[]
        {
            "technical.htf_alignment",
            "technical.momentum",
            "market.volatility_regime",
        };
        var lines = keys.Select((key, index) => new ScoredLine(
            key,
            index == 2 ? ScoreGroup.Market : ScoreGroup.Technical,
            10,
            new CriterionResult(index < exhaustion ? 10 : 5, "test"))).ToList();
        return new ScoringOutcome(75, 50, 25, 0, 0, false, null, null, lines);
    }
}
