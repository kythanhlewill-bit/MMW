using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly ILogger<BinanceMarketDataProvider> _log;
    private readonly string _futuresBase;

    public BinanceMarketDataProvider(
        HttpClient http,
        IMemoryCache cache,
        ILogger<BinanceMarketDataProvider> log,
        IOptions<BinanceOptions> options)
    {
        _http = http;
        _cache = cache;
        _log = log;
        _futuresBase = options.Value.FuturesApiBaseUrl.TrimEnd('/');
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

    public async Task<SymbolPriceFilter?> GetPriceFilterAsync(string symbol, CancellationToken cancellationToken = default)
    {
        symbol = symbol.ToUpperInvariant();
        var cacheKey = $"pricefilter:{symbol}";
        if (_cache.TryGetValue(cacheKey, out SymbolPriceFilter? cached))
            return cached; // cache cả null để tránh gọi lại symbol sai

        SymbolPriceFilter? filter = null;
        try
        {
            // exchangeInfo futures là public (không cần key). Dùng URL tuyệt đối để bỏ qua base spot.
            var json = await GetStringAsync($"{_futuresBase}/fapi/v1/exchangeInfo?symbol={symbol}", cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("symbols", out var symbols) && symbols.GetArrayLength() > 0)
            {
                foreach (var f in symbols[0].GetProperty("filters").EnumerateArray())
                {
                    if (f.GetProperty("filterType").GetString() == "PRICE_FILTER"
                        && f.TryGetProperty("tickSize", out var ts)
                        && decimal.TryParse(ts.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var tick)
                        && tick > 0m)
                    {
                        filter = new SymbolPriceFilter(tick, DecimalsOf(tick));
                    }
                }
            }
        }
        catch
        {
            filter = null; // không lấy được → client tự fallback theo độ lớn giá
        }

        _cache.Set(cacheKey, filter, TimeSpan.FromHours(6)); // tickSize gần như không đổi
        return filter;
    }

    /// <summary>Số chữ số thập phân của tickSize (vd 0.001 → 3, 0.10 → 1, 1 → 0).</summary>
    private static int DecimalsOf(decimal step)
    {
        var s = step.ToString(CultureInfo.InvariantCulture);
        var dot = s.IndexOf('.');
        if (dot < 0) return 0;
        return s.TrimEnd('0').Length - dot - 1;
    }

    private async Task<string> GetStringAsync(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Dữ liệu futures bổ sung (FR-003)
    //
    // Hợp đồng lỗi: trả `null`, không ném — trừ `period` sai, vốn là lỗi lập trình.
    // ─────────────────────────────────────────────────────────────────────

    public Task<FundingSnapshot?> GetFundingAsync(string symbol, CancellationToken cancellationToken = default) =>
        FetchAsync($"/fapi/v1/premiumIndex?symbol={Up(symbol)}", symbol, TimeSpan.FromSeconds(30),
            BinanceFuturesDataParser.ParseFunding, cancellationToken);

    public Task<OpenInterestSeries?> GetOpenInterestHistAsync(string symbol, string period, int limit, CancellationToken cancellationToken = default)
    {
        FuturesDataPeriods.Validate(period);
        var s = Up(symbol);
        return FetchAsync($"/futures/data/openInterestHist?symbol={s}&period={period}&limit={limit}", symbol,
            TimeSpan.FromMinutes(1),
            json => BinanceFuturesDataParser.ParseOpenInterestHist(s, period, json), cancellationToken);
    }

    public Task<LongShortRatio?> GetGlobalLongShortRatioAsync(string symbol, string period, CancellationToken cancellationToken = default)
    {
        FuturesDataPeriods.Validate(period);
        return FetchAsync($"/futures/data/globalLongShortAccountRatio?symbol={Up(symbol)}&period={period}&limit=1", symbol,
            TimeSpan.FromMinutes(1), BinanceFuturesDataParser.ParseLongShortRatio, cancellationToken);
    }

    public Task<DepthSnapshot?> GetDepthAsync(string symbol, int limit = 100, CancellationToken cancellationToken = default) =>
        FetchAsync($"/fapi/v1/depth?symbol={Up(symbol)}&limit={limit}", symbol, TimeSpan.FromSeconds(5),
            BinanceFuturesDataParser.ParseDepth, cancellationToken);

    public Task<TakerFlow?> GetTakerBuySellRatioAsync(string symbol, string period, CancellationToken cancellationToken = default)
    {
        FuturesDataPeriods.Validate(period);
        return FetchAsync($"/futures/data/takerlongshortRatio?symbol={Up(symbol)}&period={period}&limit=1", symbol,
            TimeSpan.FromMinutes(1), BinanceFuturesDataParser.ParseTakerFlow, cancellationToken);
    }

    /// <summary>
    /// Gọi một endpoint futures, bóc tách, cache ngắn. Mọi lỗi đều thành <c>null</c> kèm log
    /// có cấu trúc mang symbol — im lặng nuốt lỗi là vi phạm Nguyên tắc IV.
    /// </summary>
    private async Task<T?> FetchAsync<T>(
        string path, string symbol, TimeSpan cacheFor, Func<string, T?> parse, CancellationToken ct)
        where T : class
    {
        var cacheKey = $"futdata:{path}";
        if (_cache.TryGetValue(cacheKey, out T? cached)) return cached;   // cache cả null

        T? result = null;
        try
        {
            var json = await GetStringAsync(_futuresBase + path, ct);
            result = parse(json);

            if (result is null)
                _log.LogWarning("Không bóc tách được dữ liệu futures cho {Symbol} tại {Path}.", symbol, path);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Không lấy được dữ liệu futures cho {Symbol} tại {Path}.", symbol, path);
        }

        // Cache cả kết quả null, nhưng ngắn hơn nhiều: đủ để một chu kỳ chấm điểm không
        // đập vào endpoint đang lỗi nhiều lần, nhưng không kéo dài trạng thái thiếu dữ liệu.
        _cache.Set(cacheKey, result, result is null ? TimeSpan.FromSeconds(20) : cacheFor);
        return result;
    }

    private static string Up(string symbol) => symbol.ToUpperInvariant();
}
