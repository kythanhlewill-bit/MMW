using Microsoft.Extensions.Caching.Memory;
using MMW.Application.MarketData;
using MMW.Application.MarketData.Models;

namespace MMW.Infrastructure.Exchanges.Binance;

/// <summary>
/// Lấy dữ liệu thị trường công khai từ Binance (ticker, klines). Có cache ngắn để né rate limit.
/// </summary>
public class BinanceMarketDataProvider : IMarketDataProvider
{
    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;

    public BinanceMarketDataProvider(HttpClient http, IMemoryCache cache)
    {
        _http = http;
        _cache = cache;
    }

    public async Task<Ticker> GetTickerAsync(string symbol, CancellationToken cancellationToken = default)
    {
        symbol = symbol.ToUpperInvariant();
        var cacheKey = $"ticker:{symbol}";
        if (_cache.TryGetValue(cacheKey, out Ticker? cached) && cached is not null)
            return cached;

        var json = await GetStringAsync($"/api/v3/ticker/price?symbol={symbol}", cancellationToken);
        var ticker = BinanceParser.ParseTickerPrice(json, symbol);

        _cache.Set(cacheKey, ticker, TimeSpan.FromSeconds(5));
        return ticker;
    }

    public async Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, string interval, int limit = 100, CancellationToken cancellationToken = default)
    {
        symbol = symbol.ToUpperInvariant();
        var cacheKey = $"klines:{symbol}:{interval}:{limit}";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<Candle>? cached) && cached is not null)
            return cached;

        var json = await GetStringAsync(
            $"/api/v3/klines?symbol={symbol}&interval={interval}&limit={limit}", cancellationToken);
        var candles = BinanceParser.ParseKlines(json);

        _cache.Set(cacheKey, candles, TimeSpan.FromSeconds(15));
        return candles;
    }

    private async Task<string> GetStringAsync(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
