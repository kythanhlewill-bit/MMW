using MMW.Application.Backtest;
using MMW.Application.Trading.Execution;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.Backtest;

public class EntryFillTelemetryTests
{
    private static readonly DateTime Start = new(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc);

    private static EngineSetting Settings() => new()
    {
        BacktestTakerFeePercent = 0m,
        BacktestMakerFeePercent = 0m,
        BacktestEntrySlippageBps = 0m,
        BacktestStopSlippageBps = 0m,
        BacktestLimitFillRequiresThrough = true,
        LimitEntryExpiryBars = 6,
    };

    private static SimulatedTradePosition Open(params PlannedEntryTranche[] entries)
    {
        var plan = new TradeExecutionPlan(
            entries,
            StopLoss: 90m,
            FirstTakeProfit: 130m,
            RunnerTakeProfit: null,
            FirstTakeProfitFraction: 1m,
            MoveRunnerStopToBreakeven: false,
            Mode: "TelemetryTest");

        return SimulatedTradePosition.Open(
            "BTCUSDT", TradeDirection.Long, Start, 1m, DayRegime.TrendUp, plan, Settings());
    }

    private static MMW.Application.MarketData.Models.Candle Candle(decimal low, decimal high)
    {
        var open = Start.AddMinutes(15);
        return new MMW.Application.MarketData.Models.Candle(
            open, high, high, low, high, 100m, open.AddMinutes(15).AddTicks(-1));
    }

    [Fact]
    public void Market_khop_con_limit_chua_khop_duoc_phan_loai_market_only()
    {
        var trade = Open(
            new PlannedEntryTranche(100m, 0.6m),
            new PlannedEntryTranche(95m, 0.4m, IsLimit: true));

        Assert.Equal(EntryFillState.MarketOnly, EntryFillClassifier.Classify(trade));
    }

    [Fact]
    public void Market_va_limit_cung_khop_duoc_phan_loai_market_plus_limit()
    {
        var trade = Open(
            new PlannedEntryTranche(100m, 0.6m),
            new PlannedEntryTranche(95m, 0.4m, IsLimit: true));

        trade.Advance(Candle(low: 94.99m, high: 101m), Settings());

        Assert.Equal(EntryFillState.MarketPlusLimit, EntryFillClassifier.Classify(trade));
    }

    [Fact]
    public void Ke_hoach_chi_co_market_duoc_phan_loai_no_limit_planned()
    {
        var trade = Open(new PlannedEntryTranche(100m, 1m));

        Assert.Equal(EntryFillState.NoLimitPlanned, EntryFillClassifier.Classify(trade));
    }

    [Fact]
    public void Ke_hoach_chi_co_limit_phan_biet_chua_khop_va_da_khop()
    {
        var trade = Open(new PlannedEntryTranche(95m, 1m, IsLimit: true));
        Assert.Equal(EntryFillState.NoFills, EntryFillClassifier.Classify(trade));

        trade.Advance(Candle(low: 94.99m, high: 101m), Settings());

        Assert.Equal(EntryFillState.LimitOnly, EntryFillClassifier.Classify(trade));
    }
}
