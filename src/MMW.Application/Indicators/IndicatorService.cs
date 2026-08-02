using MMW.Application.MarketData.Models;

namespace MMW.Application.Indicators;

/// <summary>
/// Cài đặt chỉ số kỹ thuật chuẩn (EMA seed bằng SMA, RSI/ATR theo Wilder smoothing).
/// </summary>
public class IndicatorService : IIndicatorService
{
    /// <summary>
    /// Số mẫu tối thiểu để một phân vị có ý nghĩa (R-009). Dưới ngưỡng thì trả <c>null</c>
    /// và tiêu chí liên quan nhận 0 điểm theo FR-006 — thà không có kết luận còn hơn có
    /// một kết luận dựa trên quá ít mẫu.
    /// </summary>
    public const int MinPercentileSamples = 60;

    public decimal? Percentile(IReadOnlyList<decimal> values, int percentile)
    {
        if (percentile is <= 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percentile), percentile,
                "Phân vị phải nằm trong khoảng (0, 100].");
        }

        if (values.Count < MinPercentileSamples) return null;

        var sorted = values.ToArray();
        Array.Sort(sorted);

        // Thứ hạng gần nhất, không nội suy: rank = ceil(p/100 × n).
        // Dùng số nguyên để tránh hoàn toàn sai số dấu phẩy động ở vùng biên.
        var rank = (percentile * sorted.Length + 99) / 100;
        return sorted[rank - 1];
    }

    public decimal? PercentileOf(IReadOnlyList<decimal> values, decimal value)
    {
        if (values.Count < MinPercentileSamples) return null;

        var atOrBelow = 0;
        foreach (var v in values) if (v <= value) atOrBelow++;

        return (decimal)atOrBelow / values.Count * 100m;
    }

    public decimal? AnchoredVwap(IReadOnlyList<Candle> candles)
    {
        if (candles.Count == 0) return null;

        // Neo bám theo nến CUỐI chuỗi chứ không theo đồng hồ: giữ cho hàm thuần,
        // nhờ vậy kiểm thử lịch sử dùng được y hệt chạy thật.
        var anchorDay = candles[^1].OpenTime.Date;

        decimal pv = 0m, volume = 0m;
        for (var i = candles.Count - 1; i >= 0; i--)
        {
            var c = candles[i];
            if (c.OpenTime.Date != anchorDay) break;

            var typical = (c.High + c.Low + c.Close) / 3m;
            pv += typical * c.Volume;
            volume += c.Volume;
        }

        return volume == 0m ? null : pv / volume;
    }

    public decimal? VolumeSma(IReadOnlyList<Candle> candles, int period)
    {
        if (period <= 0 || candles.Count < period) return null;

        decimal sum = 0m;
        for (var i = candles.Count - period; i < candles.Count; i++) sum += candles[i].Volume;
        return sum / period;
    }

    public decimal? Sma(IReadOnlyList<decimal> values, int period)
    {
        if (period <= 0 || values.Count < period)
            return null;

        decimal sum = 0m;
        for (var i = values.Count - period; i < values.Count; i++)
            sum += values[i];
        return sum / period;
    }

    public decimal? Ema(IReadOnlyList<decimal> values, int period)
    {
        var series = EmaSeries(values, period);
        return series.Count == 0 ? null : series[^1];
    }

    public decimal? Rsi(IReadOnlyList<decimal> values, int period = 14)
    {
        if (period <= 0 || values.Count <= period)
            return null;

        decimal gainSum = 0m, lossSum = 0m;
        for (var i = 1; i <= period; i++)
        {
            var change = values[i] - values[i - 1];
            if (change >= 0) gainSum += change;
            else lossSum -= change;
        }

        var avgGain = gainSum / period;
        var avgLoss = lossSum / period;

        for (var i = period + 1; i < values.Count; i++)
        {
            var change = values[i] - values[i - 1];
            var gain = change > 0 ? change : 0m;
            var loss = change < 0 ? -change : 0m;
            avgGain = (avgGain * (period - 1) + gain) / period;
            avgLoss = (avgLoss * (period - 1) + loss) / period;
        }

        if (avgLoss == 0m) return 100m;
        var rs = avgGain / avgLoss;
        return 100m - 100m / (1m + rs);
    }

    public MacdResult Macd(IReadOnlyList<decimal> values, int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9)
    {
        var fast = EmaSeries(values, fastPeriod);
        var slow = EmaSeries(values, slowPeriod);
        if (fast.Count == 0 || slow.Count == 0)
            return new MacdResult(null, null, null);

        // Căn chỉnh theo index gốc: cả hai series đều kết thúc ở phần tử cuối của values.
        var offsetFast = values.Count - fast.Count;
        var offsetSlow = values.Count - slow.Count;

        var macdLine = new List<decimal>();
        for (var i = 0; i < values.Count; i++)
        {
            var fi = i - offsetFast;
            var si = i - offsetSlow;
            if (fi >= 0 && si >= 0)
                macdLine.Add(fast[fi] - slow[si]);
        }

        if (macdLine.Count == 0)
            return new MacdResult(null, null, null);

        var signalSeries = EmaSeries(macdLine, signalPeriod);
        var macd = macdLine[^1];
        decimal? signal = signalSeries.Count == 0 ? null : signalSeries[^1];
        decimal? hist = signal.HasValue ? macd - signal.Value : null;
        return new MacdResult(macd, signal, hist);
    }

    public decimal? Atr(IReadOnlyList<Candle> candles, int period = 14)
    {
        if (period <= 0 || candles.Count <= period)
            return null;

        var tr = new List<decimal>(candles.Count);
        for (var i = 0; i < candles.Count; i++)
        {
            var c = candles[i];
            if (i == 0)
            {
                tr.Add(c.High - c.Low);
                continue;
            }
            var prevClose = candles[i - 1].Close;
            var range = Math.Max(c.High - c.Low, Math.Max(Math.Abs(c.High - prevClose), Math.Abs(c.Low - prevClose)));
            tr.Add(range);
        }

        // Wilder: ATR đầu = trung bình TR của `period` nến đầu (bỏ TR[0] giả), rồi smoothing.
        decimal sum = 0m;
        for (var i = 1; i <= period; i++)
            sum += tr[i];
        var atr = sum / period;

        for (var i = period + 1; i < tr.Count; i++)
            atr = (atr * (period - 1) + tr[i]) / period;

        return atr;
    }

    /// <summary>EMA dạng chuỗi, seed bằng SMA của `period` phần tử đầu. Rỗng nếu thiếu dữ liệu.</summary>
    private static List<decimal> EmaSeries(IReadOnlyList<decimal> values, int period)
    {
        var result = new List<decimal>();
        if (period <= 0 || values.Count < period)
            return result;

        decimal seed = 0m;
        for (var i = 0; i < period; i++)
            seed += values[i];
        seed /= period;

        var k = 2m / (period + 1);
        var ema = seed;
        result.Add(ema);

        for (var i = period; i < values.Count; i++)
        {
            ema = values[i] * k + ema * (1 - k);
            result.Add(ema);
        }

        return result;
    }
}
