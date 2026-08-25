using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using MMW.Application.MarketData;
using MMW.Application.MarketData.Models;

namespace MMW.Infrastructure.Exchanges.Binance;

/// <summary>
/// Đọc dữ liệu tài khoản Binance bằng API key READ-ONLY (ký HMAC-SHA256).
/// Chỉ GET — không bao giờ đặt/huỷ lệnh.
/// </summary>
public class BinanceAccountProvider : IExchangeAccountProvider
{
    private readonly HttpClient _http;
    private readonly BinanceOptions _options;

    public BinanceAccountProvider(HttpClient http, IOptions<BinanceOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<ExchangeBalance>> GetBalancesAsync(CancellationToken cancellationToken = default)
    {
        using var doc = await SignedGetAsync("/api/v3/account", "", cancellationToken);
        var balances = new List<ExchangeBalance>();
        foreach (var b in doc.RootElement.GetProperty("balances").EnumerateArray())
        {
            var free = ParseDecimal(b.GetProperty("free").GetString());
            var locked = ParseDecimal(b.GetProperty("locked").GetString());
            if (free + locked > 0m)
                balances.Add(new ExchangeBalance(b.GetProperty("asset").GetString()!, free, locked));
        }
        return balances;
    }

    public async Task<decimal?> GetFuturesBalanceAsync(
        string quoteAsset, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(quoteAsset)) return null;

        var futuresBase = string.IsNullOrWhiteSpace(_options.FuturesApiBaseUrl)
            ? "https://fapi.binance.com"
            : _options.FuturesApiBaseUrl;
        using var doc = await SignedGetAsync("/fapi/v2/balance", "", cancellationToken, absoluteBase: futuresBase);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (string.Equals(item.GetProperty("asset").GetString(), quoteAsset, StringComparison.OrdinalIgnoreCase))
                return ParseDecimal(item.GetProperty("balance").GetString());
        }
        return null;
    }

    public async Task<IReadOnlyList<ExchangeTrade>> GetMyTradesAsync(string symbol, int limit = 500, CancellationToken cancellationToken = default)
    {
        symbol = symbol.ToUpperInvariant();
        var futuresBase = string.IsNullOrWhiteSpace(_options.FuturesApiBaseUrl)
            ? "https://fapi.binance.com"
            : _options.FuturesApiBaseUrl;
        // Futures trades: /fapi/v1/userTrades — trả "side":"BUY"/"SELL" thay vì "isBuyer"
        using var doc = await SignedGetAsync("/fapi/v1/userTrades", $"symbol={symbol}&limit={limit}", cancellationToken, absoluteBase: futuresBase);
        var trades = new List<ExchangeTrade>();
        foreach (var t in doc.RootElement.EnumerateArray())
        {
            var isBuyer = string.Equals(
                t.TryGetProperty("side", out var sideProp) ? sideProp.GetString() : null,
                "BUY", StringComparison.OrdinalIgnoreCase);
            var orderId = t.TryGetProperty("orderId", out var oidProp)
                ? oidProp.GetRawText().Trim('"')
                : null;
            trades.Add(new ExchangeTrade(
                t.GetProperty("id").GetRawText().Trim('"'),
                t.GetProperty("symbol").GetString()!,
                isBuyer,
                ParseDecimal(t.TryGetProperty("price", out var p) ? p.GetString() : "0"),
                ParseDecimal(t.TryGetProperty("qty", out var q) ? q.GetString() : "0"),
                ParseDecimal(t.TryGetProperty("commission", out var c) ? c.GetString() : "0"),
                t.TryGetProperty("commissionAsset", out var ca) ? ca.GetString() ?? "USDT" : "USDT",
                DateTimeOffset.FromUnixTimeMilliseconds(t.GetProperty("time").GetInt64()).UtcDateTime,
                orderId));
        }
        return trades;
    }

    private async Task<JsonDocument> SignedGetAsync(string path, string query, CancellationToken ct, string? absoluteBase = null)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.ApiSecret))
            throw new InvalidOperationException(
                "Chưa cấu hình Binance ApiKey/ApiSecret (read-only). Thêm vào User Secrets mục 'Binance'.");

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var baseQuery = string.IsNullOrEmpty(query)
            ? $"recvWindow=5000&timestamp={timestamp}"
            : $"{query}&recvWindow=5000&timestamp={timestamp}";

        var signature = BinanceSigner.Sign(baseQuery, _options.ApiSecret!);

        // Nếu có absoluteBase (ví dụ Futures API), dùng URL tuyệt đối để bỏ qua BaseAddress của HttpClient.
        var url = absoluteBase is not null
            ? $"{absoluteBase.TrimEnd('/')}{path}?{baseQuery}&signature={signature}"
            : $"{path}?{baseQuery}&signature={signature}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-MBX-APIKEY", _options.ApiKey);

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private static decimal ParseDecimal(string? s) =>
        decimal.Parse(s ?? "0", NumberStyles.Any, CultureInfo.InvariantCulture);
}
