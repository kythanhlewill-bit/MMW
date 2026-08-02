using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MMW.Application.Interfaces;

namespace MMW.Infrastructure.Exchanges.Binance;

public class BinanceSymbolSearchService : ISymbolSearchService
{
    private const string CacheKey = "binance:futures:symbols:v1";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(12);

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BinanceSymbolSearchService> _logger;

    public BinanceSymbolSearchService(
        HttpClient http,
        IMemoryCache cache,
        ILogger<BinanceSymbolSearchService> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> SearchFuturesSymbolsAsync(
        string? term,
        int take = 30,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 100);
        var symbols = await _cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return await LoadSymbolsAsync(cancellationToken);
        }) ?? [];

        var keyword = (term ?? string.Empty).Trim().ToUpperInvariant();
        var query = symbols.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(s => s.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        return query
            .OrderBy(s => string.IsNullOrWhiteSpace(keyword) ? 1 : s.StartsWith(keyword, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(s => s.Length)
            .ThenBy(s => s)
            .Take(take)
            .ToList();
    }

    private async Task<IReadOnlyList<string>> LoadSymbolsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync("/fapi/v1/exchangeInfo", cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!doc.RootElement.TryGetProperty("symbols", out var symbols) || symbols.ValueKind != JsonValueKind.Array)
                return [];

            return symbols.EnumerateArray()
                .Where(IsTradableUsdtPerpetual)
                .Select(s => ReadString(s, "symbol"))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!.ToUpperInvariant())
                .Distinct()
                .OrderBy(s => s)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load Binance Futures exchangeInfo symbols");
            return [];
        }
    }

    private static bool IsTradableUsdtPerpetual(JsonElement symbol)
    {
        var status = ReadString(symbol, "status");
        var quoteAsset = ReadString(symbol, "quoteAsset");
        var contractType = ReadString(symbol, "contractType");

        return string.Equals(status, "TRADING", StringComparison.OrdinalIgnoreCase)
            && string.Equals(quoteAsset, "USDT", StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(contractType) || string.Equals(contractType, "PERPETUAL", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ReadString(JsonElement item, string name)
    {
        return item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
