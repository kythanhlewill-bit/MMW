using MMW.Application.Indicators;
using MMW.Application.MarketData.Models;
using Xunit;

namespace MMW.RuleEngine.Tests;

public class IndicatorTests
{
    private readonly IndicatorService _ind = new();

    private static List<decimal> Increasing(int n, decimal start = 100m, decimal step = 1m)
    {
        var list = new List<decimal>(n);
        for (var i = 0; i < n; i++) list.Add(start + step * i);
        return list;
    }

    [Fact]
    public void Sma_Exact()
    {
        var v = new List<decimal> { 1, 2, 3, 4, 5 };
        Assert.Equal(3m, _ind.Sma(v, 5));
        Assert.Equal(4m, _ind.Sma(v, 3)); // (3+4+5)/3
    }

    [Fact]
    public void Sma_Null_When_Insufficient()
    {
        Assert.Null(_ind.Sma(new List<decimal> { 1, 2 }, 5));
    }

    [Fact]
    public void Ema_Lags_Within_Range_For_Increasing()
    {
        var v = Increasing(30);
        var ema = _ind.Ema(v, 10);
        Assert.NotNull(ema);
        Assert.True(ema! < v[^1]);  // EMA trễ hơn giá khi tăng
        Assert.True(ema! > v[0]);
    }

    [Fact]
    public void Rsi_Is_100_When_Only_Gains()
    {
        var v = Increasing(30);
        Assert.Equal(100m, _ind.Rsi(v, 14));
    }

    [Fact]
    public void Rsi_Is_0_When_Only_Losses()
    {
        var v = Increasing(30, start: 200m, step: -1m); // giảm dần
        Assert.Equal(0m, _ind.Rsi(v, 14));
    }

    [Fact]
    public void Rsi_Null_When_Insufficient()
    {
        Assert.Null(_ind.Rsi(Increasing(10), 14));
    }

    [Fact]
    public void Macd_Positive_For_Uptrend()
    {
        var v = Increasing(60);
        var macd = _ind.Macd(v);
        Assert.NotNull(macd.Macd);
        Assert.True(macd.Macd! > 0);          // fast EMA > slow EMA khi tăng
        Assert.NotNull(macd.Signal);
        Assert.NotNull(macd.Histogram);
    }

    [Fact]
    public void Atr_Is_Positive()
    {
        var candles = new List<Candle>();
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        decimal price = 100m;
        for (var i = 0; i < 30; i++)
        {
            var open = price;
            var close = price + (i % 2 == 0 ? 2m : -1m);
            var high = Math.Max(open, close) + 1m;
            var low = Math.Min(open, close) - 1m;
            candles.Add(new Candle(baseTime.AddHours(i), open, high, low, close, 10m, baseTime.AddHours(i + 1)));
            price = close;
        }

        var atr = _ind.Atr(candles, 14);
        Assert.NotNull(atr);
        Assert.True(atr! > 0);
    }

    [Fact]
    public void Atr_Null_When_Insufficient()
    {
        var candles = new List<Candle>
        {
            new(DateTime.UtcNow, 100, 101, 99, 100, 1, DateTime.UtcNow),
        };
        Assert.Null(_ind.Atr(candles, 14));
    }
}
