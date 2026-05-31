using MMW.Application.MarketData.Models;

namespace MMW.Application.MarketData;

/// <summary>
/// Cổng lấy dữ liệu thị trường công khai (không cần API key).
/// </summary>
public interface IMarketDataProvider
{
    Task<Ticker> GetTickerAsync(string symbol, CancellationToken cancellationToken = default);

    /// <param name="interval">VD: "1m", "5m", "1h", "4h", "1d".</param>
    Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, string interval, int limit = 100, CancellationToken cancellationToken = default);
}
