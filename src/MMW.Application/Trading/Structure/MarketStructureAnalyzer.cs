using MMW.Application.MarketData.Models;

namespace MMW.Application.Trading.Structure;

public enum StructureBreak
{
    None = 0,
    BullishBreak = 1,
    BearishBreak = 2,
}

/// <param name="Break">Chiều phá vỡ, hoặc <see cref="StructureBreak.None"/>.</param>
/// <param name="BrokenLevel">Giá của điểm xoay bị phá vỡ.</param>
/// <param name="BreakIndex">Vị trí nến đã phá vỡ.</param>
/// <param name="RetestConfirmed">Giá đã quay về chạm vùng phá vỡ rồi đóng cửa trở lại đúng chiều.</param>
/// <param name="RetestFailed">Giá đã quay về và đóng cửa thủng hẳn qua vùng phá vỡ.</param>
public sealed record MarketStructureResult(
    StructureBreak Break,
    decimal? BrokenLevel,
    int? BreakIndex,
    bool RetestConfirmed,
    bool RetestFailed);

/// <summary>
/// Xác định phá vỡ cấu trúc và kết quả kiểm định lại theo định nghĩa tất định ở R-007.
/// </summary>
/// <remarks>
/// Hàm thuần: cùng đầu vào luôn cho cùng kết quả, không đọc đồng hồ, không gọi mạng.
/// Mọi tham số ngưỡng truyền từ ngoài vào để không hằng số nào của thuật toán nằm trong lớp này
/// (Nguyên tắc I).
/// </remarks>
public sealed class MarketStructureAnalyzer
{
    /// <summary>
    /// Nửa độ rộng vùng kiểm định, tính theo bội của biên độ dao động. Cố định ở đây vì nó là
    /// một phần của ĐỊNH NGHĨA "kiểm định lại" tại R-007, không phải một ngưỡng để chỉnh.
    /// </summary>
    private const decimal RetestBandAtrMultiple = 0.25m;

    private readonly ISwingDetector _swings;

    public MarketStructureAnalyzer(ISwingDetector swings) => _swings = swings;

    public MarketStructureResult Analyze(
        IReadOnlyList<Candle> candles, int pivotBars, int retestWindowBars, decimal atr)
    {
        ArgumentNullException.ThrowIfNull(candles);

        var none = new MarketStructureResult(StructureBreak.None, null, null, false, false);
        if (candles.Count < 2 * pivotBars + 2) return none;

        var pivots = _swings.Detect(candles, pivotBars);
        if (pivots.Count == 0) return none;

        // Quét ngược tìm lần VƯỢT QUA gần nhất, không phải nến gần nhất đang nằm bên kia mức.
        //
        // Khác biệt này quan trọng: sau khi phá vỡ, giá thường ở trên mức suốt nhiều nến.
        // Nếu lấy nến cuối cùng còn nằm trên mức làm "nến phá vỡ" thì cửa sổ kiểm định lại
        // bắt đầu từ sau nến đó — tức là ở tương lai chưa tồn tại — và mọi cú kiểm định lại
        // thành công trong quá khứ đều bị bỏ sót.
        for (var i = candles.Count - 1; i >= 1; i--)
        {
            var close = candles[i].Close;
            var previousClose = candles[i - 1].Close;

            // Chỉ được dùng điểm xoay đã XÁC NHẬN tại thời điểm nến i đóng. Đây là dòng
            // ngăn nhìn trước tương lai: một đỉnh hình thành tại i-1 chưa tồn tại ở nến i.
            var high = LastConfirmedPivot(pivots, i, isHigh: true);
            var low = LastConfirmedPivot(pivots, i, isHigh: false);

            if (high is not null && close > high.Price && previousClose <= high.Price)
                return EvaluateRetest(candles, i, high.Price, StructureBreak.BullishBreak, retestWindowBars, atr);

            if (low is not null && close < low.Price && previousClose >= low.Price)
                return EvaluateRetest(candles, i, low.Price, StructureBreak.BearishBreak, retestWindowBars, atr);
        }

        return none;
    }

    private static SwingPoint? LastConfirmedPivot(IReadOnlyList<SwingPoint> pivots, int atIndex, bool isHigh)
    {
        SwingPoint? found = null;
        foreach (var p in pivots)
        {
            if (p.IsHigh != isHigh) continue;
            if (p.ConfirmedAtIndex > atIndex) break;   // chưa biết được tại nến này
            found = p;
        }
        return found;
    }

    private static MarketStructureResult EvaluateRetest(
        IReadOnlyList<Candle> candles, int breakIndex, decimal level,
        StructureBreak direction, int retestWindowBars, decimal atr)
    {
        var band = Math.Abs(atr) * RetestBandAtrMultiple;
        var upper = level + band;
        var lower = level - band;

        var last = Math.Min(candles.Count - 1, breakIndex + retestWindowBars);
        var touched = false;

        for (var i = breakIndex + 1; i <= last; i++)
        {
            var c = candles[i];

            // Chạm vùng: nến phủ lên dải [lower, upper] ở bất kỳ đâu.
            if (c.Low <= upper && c.High >= lower) touched = true;

            if (!touched) continue;

            if (direction == StructureBreak.BullishBreak)
            {
                if (c.Close < lower)
                    return new MarketStructureResult(direction, level, breakIndex, false, true);
                if (c.Close > upper)
                    return new MarketStructureResult(direction, level, breakIndex, true, false);
            }
            else
            {
                if (c.Close > upper)
                    return new MarketStructureResult(direction, level, breakIndex, false, true);
                if (c.Close < lower)
                    return new MarketStructureResult(direction, level, breakIndex, true, false);
            }
        }

        // Chưa quay lại kiểm định trong cửa sổ: phá vỡ có thật nhưng chưa được xác nhận.
        return new MarketStructureResult(direction, level, breakIndex, false, false);
    }
}
