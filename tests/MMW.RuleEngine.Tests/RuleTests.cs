using MMW.Application.RuleEngine;
using MMW.Application.RuleEngine.Rules;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests;

public class RuleTests
{
    private static RuleEvaluationContext Ctx(
        Trade trade,
        RiskSetting? settings = null,
        decimal equity = 1000m,
        TradingDay? day = null) => new()
        {
            Trade = trade,
            Settings = settings ?? new RiskSetting(),
            AccountEquity = equity,
            Day = day,
        };

    [Fact]
    public void RequireStopLoss_Flags_When_Missing()
    {
        var rule = new RequireStopLossRule();
        var v = rule.Evaluate(Ctx(new Trade { Symbol = "X", StopLoss = null },
            new RiskSetting { RequireStopLoss = true }));

        Assert.NotNull(v);
        Assert.Equal(FlagType.NoStopLoss, v!.Type);
        Assert.Equal(FlagSeverity.Critical, v.Severity);
    }

    [Fact]
    public void RequireStopLoss_Passes_When_Present()
    {
        var rule = new RequireStopLossRule();
        var v = rule.Evaluate(Ctx(new Trade { Symbol = "X", StopLoss = 95m },
            new RiskSetting { RequireStopLoss = true }));

        Assert.Null(v);
    }

    [Fact]
    public void MaxRisk_Warning_When_Slightly_Over()
    {
        var rule = new MaxRiskPerTradeRule();
        var v = rule.Evaluate(Ctx(new Trade { Symbol = "X", RiskPercent = 1.5m },
            new RiskSetting { MaxRiskPerTradePercent = 1m }));

        Assert.NotNull(v);
        Assert.Equal(FlagSeverity.Warning, v!.Severity);
    }

    [Fact]
    public void MaxRisk_Critical_When_Double_Threshold()
    {
        var rule = new MaxRiskPerTradeRule();
        var v = rule.Evaluate(Ctx(new Trade { Symbol = "X", RiskPercent = 2m },
            new RiskSetting { MaxRiskPerTradePercent = 1m }));

        Assert.NotNull(v);
        Assert.Equal(FlagSeverity.Critical, v!.Severity);
    }

    [Fact]
    public void MaxRisk_Passes_When_Within_Limit()
    {
        var rule = new MaxRiskPerTradeRule();
        var v = rule.Evaluate(Ctx(new Trade { Symbol = "X", RiskPercent = 0.8m },
            new RiskSetting { MaxRiskPerTradePercent = 1m }));

        Assert.Null(v);
    }

    [Fact]
    public void MinRR_Flags_When_Below()
    {
        var rule = new MinRiskRewardRule();
        var v = rule.Evaluate(Ctx(new Trade { Symbol = "X", PlannedRiskReward = 1m },
            new RiskSetting { MinRiskRewardRatio = 1.5m }));

        Assert.NotNull(v);
        Assert.Equal(FlagType.LowRiskReward, v!.Type);
    }

    [Fact]
    public void MaxTradesPerDay_Flags_When_At_Limit()
    {
        var rule = new MaxTradesPerDayRule();
        var day = new TradingDay { TradeCount = 5 };
        var v = rule.Evaluate(Ctx(new Trade { Symbol = "X" },
            new RiskSetting { MaxTradesPerDay = 5 }, day: day));

        Assert.NotNull(v);
        Assert.Equal(FlagType.MaxTradesPerDayExceeded, v!.Type);
    }

    [Fact]
    public void MaxTradesPerDay_Passes_Below_Limit()
    {
        var rule = new MaxTradesPerDayRule();
        var day = new TradingDay { TradeCount = 3 };
        var v = rule.Evaluate(Ctx(new Trade { Symbol = "X" },
            new RiskSetting { MaxTradesPerDay = 5 }, day: day));

        Assert.Null(v);
    }

    [Fact]
    public void DailyLossLimit_Flags_When_Loss_Exceeds()
    {
        var rule = new DailyLossLimitRule();
        // equity 1000, max 3% → giới hạn lỗ 30; NetPnl -50 vượt ngưỡng
        var day = new TradingDay { NetPnl = -50m, StartingEquity = 1000m };
        var v = rule.Evaluate(Ctx(new Trade { Symbol = "X" },
            new RiskSetting { MaxDailyLossPercent = 3m }, equity: 1000m, day: day));

        Assert.NotNull(v);
        Assert.Equal(FlagSeverity.Critical, v!.Severity);
    }

    [Fact]
    public void DailyLossLimit_Passes_When_Within()
    {
        var rule = new DailyLossLimitRule();
        var day = new TradingDay { NetPnl = -20m, StartingEquity = 1000m };
        var v = rule.Evaluate(Ctx(new Trade { Symbol = "X" },
            new RiskSetting { MaxDailyLossPercent = 3m }, equity: 1000m, day: day));

        Assert.Null(v);
    }

    [Fact]
    public void Engine_Aggregates_Multiple_Violations()
    {
        var engine = new TradeRuleEngine(new ITradeRule[]
        {
            new RequireStopLossRule(),
            new MaxRiskPerTradeRule(),
            new MinRiskRewardRule(),
        });

        // Lệnh tệ: không SL + risk cao + RR thấp
        var trade = new Trade
        {
            Symbol = "X",
            StopLoss = null,
            RiskPercent = 3m,
            PlannedRiskReward = 0.5m,
        };
        var settings = new RiskSetting
        {
            RequireStopLoss = true,
            MaxRiskPerTradePercent = 1m,
            MinRiskRewardRatio = 1.5m,
        };

        var violations = engine.Evaluate(Ctx(trade, settings));

        Assert.Equal(3, violations.Count);
    }
}
