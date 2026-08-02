using System.Globalization;
using MMW.Application.Abstractions;
using MMW.Application.Indicators;
using MMW.Application.MarketData.Models;
using MMW.Domain.Enums;

namespace MMW.Application.MarketData;

/// <summary>
/// Tính indicator rồi suy ra bias bằng quy tắc rõ ràng:
///   +1 nếu giá > EMA20 > EMA50 (uptrend), -1 nếu ngược lại.
///   +1 nếu MACD histogram > 0, -1 nếu &lt; 0.
///   RSI &gt;70 / &lt;30 chỉ ghi chú (quá mua/quá bán), không tự lật bias.
/// Tổng điểm > 0 → Bullish, &lt; 0 → Bearish, = 0 → Neutral.
/// </summary>
/// <remarks>
/// Mọi phép tính chạy trên chuỗi đã qua <c>ClosedOnly()</c>. Giá hiện tại đến từ tham số
/// riêng chứ không từ nến cuối chuỗi — nến cuối có thể đang chạy, và dùng nó để tính chỉ báo
/// làm chỉ báo đổi giá trị theo từng tick (repaint).
/// </remarks>
public class MarketAnalyzer : IMarketAnalyzer
{
    private readonly IIndicatorService _ind;
    private readonly IClock _clock;

    public MarketAnalyzer(IIndicatorService indicators, IClock clock)
    {
        _ind = indicators;
        _clock = clock;
    }

    public MarketAnalysis Analyze(IReadOnlyList<Candle> candles, decimal currentPrice)
    {
        var closed = candles.ClosedOnly(_clock);

        var closes = closed.Select(c => c.Close).ToList();
        var price = currentPrice;

        var rsi = _ind.Rsi(closes, 14);
        var ema20 = _ind.Ema(closes, 20);
        var ema50 = _ind.Ema(closes, 50);
        var macd = _ind.Macd(closes);
        var atr = _ind.Atr(closed, 14);

        var score = 0;
        var notes = new List<string>();

        if (ema20.HasValue && ema50.HasValue)
        {
            if (price > ema20 && ema20 > ema50) { score++; notes.Add("Giá trên EMA20 > EMA50 (xu hướng tăng)"); }
            else if (price < ema20 && ema20 < ema50) { score--; notes.Add("Giá dưới EMA20 < EMA50 (xu hướng giảm)"); }
        }

        if (macd.Histogram.HasValue)
        {
            if (macd.Histogram > 0) { score++; notes.Add("MACD histogram dương (động lượng tăng)"); }
            else if (macd.Histogram < 0) { score--; notes.Add("MACD histogram âm (động lượng giảm)"); }
        }

        if (rsi.HasValue)
        {
            if (rsi > 70) notes.Add($"RSI {Fmt(rsi)} — quá mua");
            else if (rsi < 30) notes.Add($"RSI {Fmt(rsi)} — quá bán");
        }

        var bias = score > 0 ? MarketBias.Bullish : score < 0 ? MarketBias.Bearish : MarketBias.Neutral;
        if (notes.Count == 0) notes.Add("Chưa đủ dữ liệu hoặc tín hiệu trung tính");

        return new MarketAnalysis(
            price, rsi, ema20, ema50, macd.Macd, macd.Signal, macd.Histogram, atr,
            bias, score, string.Join("; ", notes));
    }

    private static string Fmt(decimal? v) =>
        v?.ToString("0.#", CultureInfo.InvariantCulture) ?? "—";
}
