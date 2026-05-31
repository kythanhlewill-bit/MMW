using MMW.Infrastructure.Exchanges.Binance;
using Xunit;

namespace MMW.RuleEngine.Tests;

public class BinanceParserTests
{
    [Fact]
    public void ParseTickerPrice_Works()
    {
        const string json = """{"symbol":"BTCUSDT","price":"65000.12000000"}""";
        var t = BinanceParser.ParseTickerPrice(json, "btcusdt");

        Assert.Equal("BTCUSDT", t.Symbol);
        Assert.Equal(65000.12m, t.Price);
    }

    [Fact]
    public void ParseKlines_Works_With_Real_Shape()
    {
        // Định dạng thật của Binance /api/v3/klines (mảng các mảng).
        const string json = """
        [
          [1499040000000,"0.01634790","0.80000000","0.01575800","0.01577100","148976.11427815",1499644799999,"2434.19055334",308,"1756.87402397","28.46694368","0"],
          [1499644800000,"0.01577100","0.02000000","0.01500000","0.01800000","100000.00000000",1500249599999,"1800.00000000",200,"900.00000000","9.00000000","0"]
        ]
        """;

        var candles = BinanceParser.ParseKlines(json);

        Assert.Equal(2, candles.Count);

        var c0 = candles[0];
        Assert.Equal(0.01634790m, c0.Open);
        Assert.Equal(0.80000000m, c0.High);
        Assert.Equal(0.01575800m, c0.Low);
        Assert.Equal(0.01577100m, c0.Close);
        Assert.Equal(148976.11427815m, c0.Volume);
        Assert.Equal(new DateTime(2017, 7, 3, 0, 0, 0, DateTimeKind.Utc), c0.OpenTime);

        Assert.Equal(0.018m, candles[1].Close);
    }
}
