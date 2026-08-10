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

    // ─────────────────────────────────────────────────────────────────────
    // Deterministic Intraday Trading Engine
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Phân vị theo thứ hạng gần nhất, KHÔNG nội suy (R-009):
    /// <c>rank = ceil(p/100 × n)</c>, <c>value = sorted[rank-1]</c>.
    /// </summary>
    /// <remarks>
    /// Trả <c>null</c> khi có dưới <see cref="IndicatorService.MinPercentileSamples"/> mẫu.
    /// Ném <see cref="ArgumentOutOfRangeException"/> khi <paramref name="percentile"/> ngoài (0, 100].
    /// </remarks>
    decimal? Percentile(IReadOnlyList<decimal> values, int percentile);

    /// <summary>Phân vị của một giá trị: <c>(số phần tử ≤ v) / n × 100</c>. Null khi thiếu mẫu.</summary>
    decimal? PercentileOf(IReadOnlyList<decimal> values, decimal value);

    /// <summary>
    /// VWAP neo theo ngày UTC của nến CUỐI chuỗi, khởi động lại tại 00:00 UTC (R-008).
    /// Dùng giá điển hình <c>(H+L+C)/3</c>. Null khi chuỗi rỗng hoặc khối lượng ngày bằng 0.
    /// </summary>
    decimal? AnchoredVwap(IReadOnlyList<Candle> candles);

    /// <summary>Trung bình khối lượng <paramref name="period"/> nến gần nhất. Null khi thiếu dữ liệu.</summary>
    decimal? VolumeSma(IReadOnlyList<Candle> candles, int period);

    /// <summary>
    /// Hệ số tương quan Pearson của hai chuỗi, kết quả trong <c>[-1, 1]</c>.
    /// </summary>
    /// <remarks>
    /// Null khi hai chuỗi lệch độ dài, dưới <see cref="IndicatorService.MinCorrelationSamples"/>
    /// mẫu, hoặc một chuỗi phẳng tuyệt đối (phương sai bằng 0 — tương quan không xác định, và
    /// trả 0 ở đó sẽ bị đọc nhầm thành "đã đo được và bằng 0").
    /// </remarks>
    decimal? Correlation(IReadOnlyList<decimal> a, IReadOnlyList<decimal> b);

    /// <summary>
    /// Chuỗi log-return của giá đóng. Độ dài bằng <c>closes.Count - 1</c>.
    /// </summary>
    /// <remarks>
    /// Tương quan phải tính trên LỢI SUẤT, không phải trên giá. Hai chuỗi giá bất kỳ cùng có xu
    /// hướng tăng sẽ cho tương quan gần 1 dù chuyển động ngày qua ngày chẳng liên quan gì nhau —
    /// đó là tương quan giả kinh điển, và nó sẽ báo "ETH bám sát BTC" trong đúng lúc ETH đang
    /// tách đoàn.
    /// </remarks>
    IReadOnlyList<decimal> LogReturns(IReadOnlyList<decimal> closes);
}
