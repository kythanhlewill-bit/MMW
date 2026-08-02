using MMW.Application.MarketData.Models;

namespace MMW.Application.Trading.Structure;

/// <summary>
/// Điểm xoay fractal: đỉnh tại <c>i</c> khi <c>High[i]</c> lớn hơn hẳn <c>High</c> của
/// <c>N</c> nến trước và <c>N</c> nến sau. Định nghĩa đối xứng cho đáy (R-007).
/// </summary>
/// <remarks>
/// Hai lựa chọn thiết kế đáng nêu:
///
/// <para><b>1. Độ trễ N nến là chủ ý.</b> Một điểm xoay chỉ được xác nhận sau khi đã có
/// <c>N</c> nến đứng sau nó. Cái giá là biết muộn; đổi lại là không thể nhìn trước tương lai,
/// điều kiện bắt buộc để kiểm thử lịch sử trung thực.</para>
///
/// <para><b>2. So sánh dùng "lớn hơn hẳn", không phải "lớn hơn hoặc bằng".</b> Một vùng
/// đi ngang phẳng lẽ ra không nên sinh ra hàng loạt điểm xoay giả — và các điểm xoay giả đó
/// sẽ chảy thẳng vào tiêu chí phá vỡ cấu trúc dưới dạng những cú phá vỡ không có thật.</para>
/// </remarks>
public sealed class SwingDetector : ISwingDetector
{
    public IReadOnlyList<SwingPoint> Detect(IReadOnlyList<Candle> candles, int pivotBars)
    {
        if (pivotBars <= 0)
            throw new ArgumentOutOfRangeException(nameof(pivotBars), pivotBars, "Số nến xác nhận phải lớn hơn 0.");

        ArgumentNullException.ThrowIfNull(candles);

        var n = pivotBars;
        if (candles.Count < 2 * n + 1) return Array.Empty<SwingPoint>();

        var points = new List<SwingPoint>();

        for (var i = n; i <= candles.Count - 1 - n; i++)
        {
            var isHigh = true;
            var isLow = true;

            for (var k = i - n; k <= i + n; k++)
            {
                if (k == i) continue;
                if (candles[k].High >= candles[i].High) isHigh = false;
                if (candles[k].Low <= candles[i].Low) isLow = false;
                if (!isHigh && !isLow) break;
            }

            var confirmedAt = i + n;

            if (isHigh)
            {
                points.Add(new SwingPoint(i, true, candles[i].High, candles[i].CloseTime,
                    confirmedAt, candles[confirmedAt].CloseTime));
            }

            if (isLow)
            {
                points.Add(new SwingPoint(i, false, candles[i].Low, candles[i].CloseTime,
                    confirmedAt, candles[confirmedAt].CloseTime));
            }
        }

        return points;
    }
}
