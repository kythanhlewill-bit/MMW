using System.Globalization;
using System.Text.Json;
using MMW.Application.MarketData.Models;

namespace MMW.Infrastructure.Exchanges.Binance;

/// <summary>
/// Parse JSON trả về từ Binance. Tách riêng để test được mà không cần gọi mạng.
/// </summary>
public static class BinanceParser
{
    public static Ticker ParseTickerPrice(string json, string symbol)
    {
        using var doc = JsonDocument.Parse(json);
        return new Ticker(symbol.ToUpperInvariant(), ParseDecimal(doc.RootElement.GetProperty("price").GetString()));
    }

    public static IReadOnlyList<Candle> ParseKlines(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var candles = new List<Candle>();
        foreach (var k in doc.RootElement.EnumerateArray())
        {
            // [ openTime, open, high, low, close, volume, closeTime, ... ]
            candles.Add(new Candle(
                FromUnixMs(k[0].GetInt64()),
                ParseDecimal(k[1].GetString()),
                ParseDecimal(k[2].GetString()),
                ParseDecimal(k[3].GetString()),
                ParseDecimal(k[4].GetString()),
                ParseDecimal(k[5].GetString()),
                FromUnixMs(k[6].GetInt64())));
        }
        return candles;
    }

    private static decimal ParseDecimal(string? s) =>
        decimal.Parse(s ?? "0", NumberStyles.Any, CultureInfo.InvariantCulture);

    private static DateTime FromUnixMs(long ms) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
}
