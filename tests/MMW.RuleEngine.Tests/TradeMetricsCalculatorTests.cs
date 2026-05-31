using MMW.Application.RuleEngine;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests;

public class TradeMetricsCalculatorTests
{
    private readonly TradeMetricsCalculator _calc = new();

    [Fact]
    public void Computes_RiskAmount_And_RiskPercent()
    {
        // Long BTC: entry 100, SL 95 → khoảng cách 5; qty 2 → risk 10; equity 1000 → 1%
        var trade = new Trade
        {
            Symbol = "BTCUSDT",
            Direction = TradeDirection.Long,
            EntryPrice = 100m,
            StopLoss = 95m,
            Quantity = 2m,
        };

        _calc.Compute(trade, accountEquity: 1000m);

        Assert.Equal(10m, trade.RiskAmount);
        Assert.Equal(1m, trade.RiskPercent);
    }

    [Fact]
    public void Computes_PlannedRiskReward()
    {
        // entry 100, SL 95 (risk 5), TP 110 (reward 10) → RR = 2
        var trade = new Trade
        {
            Symbol = "BTCUSDT",
            EntryPrice = 100m,
            StopLoss = 95m,
            TakeProfit = 110m,
            Quantity = 1m,
        };

        _calc.Compute(trade, 1000m);

        Assert.Equal(2m, trade.PlannedRiskReward);
    }

    [Fact]
    public void Computes_RMultiple_And_Outcome_OnWin()
    {
        var trade = new Trade
        {
            Symbol = "BTCUSDT",
            EntryPrice = 100m,
            StopLoss = 95m,
            Quantity = 2m,       // risk = 10
            RealizedPnl = 25m,   // +2.5R
        };

        _calc.Compute(trade, 1000m);

        Assert.Equal(TradeOutcome.Win, trade.Outcome);
        Assert.Equal(2.5m, trade.RMultiple);
    }

    [Fact]
    public void NoStopLoss_LeavesRiskMetricsNull()
    {
        var trade = new Trade
        {
            Symbol = "BTCUSDT",
            EntryPrice = 100m,
            StopLoss = null,
            Quantity = 2m,
        };

        _calc.Compute(trade, 1000m);

        Assert.Null(trade.RiskAmount);
        Assert.Null(trade.RiskPercent);
        Assert.Null(trade.PlannedRiskReward);
    }
}
