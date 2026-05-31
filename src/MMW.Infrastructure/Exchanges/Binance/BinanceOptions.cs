namespace MMW.Infrastructure.Exchanges.Binance;

public class BinanceOptions
{
    public const string SectionName = "Binance";

    /// <summary>Endpoint dữ liệu công khai (không cần key, ít bị chặn địa lý).</summary>
    public string MarketDataBaseUrl { get; set; } = "https://data-api.binance.vision";

    /// <summary>Endpoint REST chính (dùng cho API cần ký).</summary>
    public string ApiBaseUrl { get; set; } = "https://api.binance.com";

    /// <summary>API key READ-ONLY. Để trong User Secrets, không commit.</summary>
    public string? ApiKey { get; set; }

    public string? ApiSecret { get; set; }
}
