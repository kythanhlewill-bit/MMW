using MMW.Application.Indicators;
using MMW.Application.MarketData;
using MMW.Application.MarketData.Models;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests;

public class MarketAnalyzerTests
{
    private static readonly DateTime BaseTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // Đồng hồ đặt xa về tương lai để mọi nến trong các test này đều đã đóng —
    // các test ở đây kiểm tra logic bias, không kiểm tra việc cắt nến hở.
    private readonly MarketAnalyzer _analyzer =
        new(new IndicatorService(), TestClock.At(BaseTime.AddYears(1)));

    private static List<Candle> Trend(int n, decimal start, decimal step)
    {
        var baseTime = BaseTime;
        var list = new List<Candle>(n);
        for (var i = 0; i < n; i++)
        {
            var close = start + step * i;
            var open = close - step / 2;
            var high = Math.Max(open, close) + 0.5m;
            var low = Math.Min(open, close) - 0.5m;
            list.Add(new Candle(baseTime.AddHours(i), open, high, low, close, 10m, baseTime.AddHours(i + 1)));
        }
        return list;
    }

    [Fact]
    public void Uptrend_Is_Bullish()
    {
        var candles = Trend(60, 100m, 1m);
        var a = _analyzer.Analyze(candles, candles[^1].Close);

        Assert.Equal(MarketBias.Bullish, a.Bias);
        Assert.NotNull(a.Ema20);
        Assert.NotNull(a.Ema50);
        Assert.True(a.Macd > 0); // MACD line dương khi EMA nhanh > EMA chậm (uptrend)
    }

    [Fact]
    public void Downtrend_Is_Bearish()
    {
        var candles = Trend(60, 200m, -1m);
        var a = _analyzer.Analyze(candles, candles[^1].Close);

        Assert.Equal(MarketBias.Bearish, a.Bias);
        Assert.True(a.Macd < 0); // MACD line âm khi EMA nhanh < EMA chậm (downtrend)
    }

    [Fact]
    public void Returns_Price_As_Given_Current_Price()
    {
        // Trước đây Price lấy từ closes[^1]. Giờ nó là tham số truyền vào — đúng nguồn,
        // vì nến cuối chuỗi khi chạy thật là cây nến chưa đóng.
        var candles = Trend(60, 100m, 1m);
        var a = _analyzer.Analyze(candles, currentPrice: 12_345m);
        Assert.Equal(12_345m, a.Price);
    }
}
