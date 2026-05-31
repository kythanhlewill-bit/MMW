using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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

    public async Task<IReadOnlyList<ExchangeTrade>> GetMyTradesAsync(string symbol, int limit = 500, CancellationToken cancellationToken = default)
    {
        symbol = symbol.ToUpperInvariant();
        using var doc = await SignedGetAsync("/api/v3/myTrades", $"symbol={symbol}&limit={limit}", cancellationToken);
        var trades = new List<ExchangeTrade>();
        foreach (var t in doc.RootElement.EnumerateArray())
        {
            trades.Add(new ExchangeTrade(
                t.GetProperty("id").GetRawText().Trim('"'),
                t.GetProperty("symbol").GetString()!,
                t.GetProperty("isBuyer").GetBoolean(),
                ParseDecimal(t.GetProperty("price").GetString()),
                ParseDecimal(t.GetProperty("qty").GetString()),
                ParseDecimal(t.GetProperty("commission").GetString()),
                t.GetProperty("commissionAsset").GetString()!,
                DateTimeOffset.FromUnixTimeMilliseconds(t.GetProperty("time").GetInt64()).UtcDateTime));
        }
        return trades;
    }

    private async Task<JsonDocument> SignedGetAsync(string path, string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.ApiSecret))
            throw new InvalidOperationException(
                "Chưa cấu hình Binance ApiKey/ApiSecret (read-only). Thêm vào User Secrets mục 'Binance'.");

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var baseQuery = string.IsNullOrEmpty(query)
            ? $"recvWindow=5000&timestamp={timestamp}"
            : $"{query}&recvWindow=5000&timestamp={timestamp}";

        var signature = Sign(baseQuery, _options.ApiSecret!);
        var url = $"{path}?{baseQuery}&signature={signature}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-MBX-APIKEY", _options.ApiKey);

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private static string Sign(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static decimal ParseDecimal(string? s) =>
        decimal.Parse(s ?? "0", NumberStyles.Any, CultureInfo.InvariantCulture);
}
