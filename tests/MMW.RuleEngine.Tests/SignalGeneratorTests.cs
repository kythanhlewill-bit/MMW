using MMW.Application.MarketData;
using MMW.Application.MarketData.Models;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests;

public class SignalGeneratorTests
{
    private readonly SignalGenerator _gen = new();

    private static MarketAnalysis Analysis(MarketBias bias, int score, decimal price = 100m, decimal? atr = 2m) =>
        new(price, 50m, price, price, 1m, 0.5m, 0.5m, atr, bias, score, "test");

    [Fact]
    public void Strong_Bullish_Produces_Long_With_Atr_Levels()
    {
        var s = _gen.Generate(Analysis(MarketBias.Bullish, 2, price: 100m, atr: 2m));

        Assert.NotNull(s);
        Assert.Equal(TradeDirection.Long, s!.Direction);
        Assert.Equal(100m, s.Entry);
        Assert.Equal(97m, s.StopLoss);     // 100 - 1.5*2
        Assert.Equal(106m, s.TakeProfit);  // 100 + 2*(1.5*2)
        Assert.Equal(2m, s.RiskReward);
        Assert.True(s.StopLoss < s.Entry && s.TakeProfit > s.Entry);
    }

    [Fact]
    public void Strong_Bearish_Produces_Short()
    {
        var s = _gen.Generate(Analysis(MarketBias.Bearish, -2, price: 100m, atr: 2m));

        Assert.NotNull(s);
        Assert.Equal(TradeDirection.Short, s!.Direction);
        Assert.Equal(103m, s.StopLoss);    // 100 + 3
        Assert.Equal(94m, s.TakeProfit);   // 100 - 6
        Assert.True(s.StopLoss > s.Entry && s.TakeProfit < s.Entry);
    }

    [Fact]
    public void Weak_Signal_Returns_Null()
    {
        Assert.Null(_gen.Generate(Analysis(MarketBias.Bullish, 1)));
    }

    [Fact]
    public void Neutral_Returns_Null()
    {
        Assert.Null(_gen.Generate(Analysis(MarketBias.Neutral, 0)));
    }

    [Fact]
    public void No_Atr_Returns_Null()
    {
        Assert.Null(_gen.Generate(Analysis(MarketBias.Bullish, 2, atr: null)));
    }
}
