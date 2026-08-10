using MMW.Application.MarketData.Models;
using MMW.Application.Trading.Execution;
using MMW.Application.Trading.Scoring;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.Scoring;

public sealed class V3ExecutionPolicyTests
{
    private readonly SetupTriggerPolicy _triggers = new(
        ScoringFixtures.Structure,
        new MMW.Application.Trading.Structure.SidewaysPatternAnalyzer(ScoringFixtures.Swings));
    private readonly ExecutionViabilityPolicy _economics = new();

    [Fact]
    public void Range_phai_sweep_dong_lai_trong_bien_va_co_volume_moi_xac_nhan()
    {
        var candles = ScoringFixtures.Flat(21, price: 100m, range: 4m, volume: 100m);
        var open = ScoringFixtures.Now.AddMinutes(-15);
        candles.Add(new Candle(
            open, Open: 94m, High: 105m, Low: 89m, Close: 104m, Volume: 200m,
            CloseTime: open.AddMinutes(15).AddTicks(-1)));
        var context = ScoringFixtures.Context(
            entry: candles,
            direction: TradeDirection.Long,
            plan: ScoringFixtures.Plan(regime: DayRegime.Range));

        var result = _triggers.Evaluate(context, new RangeLocation(90m, 110m, 70m, 4));

        Assert.True(result.Passed);
        Assert.Equal(SetupType.RangeRejection, result.SetupType);
        Assert.Equal(SetupTriggerState.Confirmed, result.State);
        Assert.Equal(90m, result.SuggestedLimitEntry);
    }

    [Fact]
    public void O_bien_range_nhung_khong_sweep_thi_khong_duoc_vao()
    {
        var context = ScoringFixtures.Context(
            entry: ScoringFixtures.Flat(30, price: 92m, range: 2m, volume: 100m),
            direction: TradeDirection.Long,
            plan: ScoringFixtures.Plan(regime: DayRegime.Range));

        var result = _triggers.Evaluate(context, new RangeLocation(90m, 110m, 10m, 4));

        Assert.False(result.Passed);
        Assert.Equal(SetupTriggerState.RangeNotSwept, result.State);
    }

    [Fact]
    public void Cost_gate_loai_stop_qua_hep_du_gross_RR_nhin_co_ve_du()
    {
        var settings = ScoringFixtures.Settings(s => s.StrategyVersion = TradingStrategyVersion.TriggerFirstV3);
        var tight = Plan(entry: 100m, stop: 99.9m, target: 100.16m);

        var result = _economics.Evaluate(tight, TradeDirection.Long, settings, enforceV3Gates: true);

        Assert.False(result.Passed);
        Assert.True(result.CostToTargetPercent > settings.V3MaxCostToTargetPercent);
    }

    [Fact]
    public void Cost_gate_cho_qua_ke_hoach_co_RR_rong_du_lon()
    {
        var settings = ScoringFixtures.Settings(s => s.StrategyVersion = TradingStrategyVersion.TriggerFirstV3);
        var viable = Plan(entry: 100m, stop: 90m, target: 120m);

        var result = _economics.Evaluate(viable, TradeDirection.Long, settings, enforceV3Gates: true);

        Assert.True(result.Passed);
        Assert.True(result.NetRiskReward >= settings.V3MinNetRiskReward);
        Assert.True(result.CostToTargetPercent <= settings.V3MaxCostToTargetPercent);
    }

    private static TradeExecutionPlan Plan(decimal entry, decimal stop, decimal target) => new(
        [new PlannedEntryTranche(entry, 1m)],
        stop,
        target,
        RunnerTakeProfit: null,
        FirstTakeProfitFraction: 1m,
        MoveRunnerStopToBreakeven: false,
        Mode: "test");
}
