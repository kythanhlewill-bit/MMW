using MMW.Application.MarketData.Models;

namespace MMW.Application.Indicators;

public sealed record MacdResult(decimal? Macd, decimal? Signal, decimal? Histogram);

/// <summary>
/// Tính chỉ số kỹ thuật từ dữ liệu giá/nến. Thuần, deterministic — không gọi API ngoài.
/// Các hàm trả về GIÁ TRỊ MỚI NHẤT (null nếu chưa đủ dữ liệu).
/// </summary>
public interface IIndicatorService
{
    decimal? Sma(IReadOnlyList<decimal> values, int period);
    decimal? Ema(IReadOnlyList<decimal> values, int period);
    decimal? Rsi(IReadOnlyList<decimal> values, int period = 14);
    MacdResult Macd(IReadOnlyList<decimal> values, int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9);
    decimal? Atr(IReadOnlyList<Candle> candles, int period = 14);
}
