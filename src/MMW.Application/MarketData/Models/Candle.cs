using MMW.Application.Abstractions;

namespace MMW.Application.MarketData.Models;

/// <summary>Một nến OHLCV.</summary>
/// <remarks>
/// <see cref="CloseTime"/> lấy nguyên từ trường <c>closeTime</c> của sàn, tức mốc cuối cùng
/// còn thuộc về cây nến (ví dụ nến 15 phút mở 12:00 có <c>CloseTime</c> = 12:14:59.999).
///
/// Trạng thái đã-đóng KHÔNG được lưu trong bản ghi mà suy ra từ đồng hồ — xem R-002.
/// Lưu cờ tĩnh sẽ làm kho lịch sử và sàn hành xử khác nhau, và khác nhau ở đúng chỗ
/// mà tính tương đương giữa kiểm thử lịch sử với chạy thật phụ thuộc vào.
/// </remarks>
public sealed record Candle(
    DateTime OpenTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    DateTime CloseTime);

public static class CandleExtensions
{
    /// <summary>
    /// Nến đã đóng chưa tại thời điểm của <paramref name="clock"/>.
    /// Mốc <c>CloseTime</c> trùng đúng hiện tại được tính là ĐÃ đóng.
    /// </summary>
    public static bool IsClosed(this Candle candle, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(candle);
        ArgumentNullException.ThrowIfNull(clock);
        return clock.UtcNow >= candle.CloseTime;
    }

    /// <summary>
    /// Cắt bỏ phần đuôi nến chưa đóng. PHẢI gọi trước mọi phép tính chỉ báo (FR-001).
    /// </summary>
    /// <remarks>
    /// Đây là chốt chặn duy nhất chống lỗi repaint. Chỉ báo tính trên cây nến đang chạy
    /// đổi giá trị theo từng tick, nên bỏ qua bước này là làm cho kiểm thử lịch sử
    /// vĩnh viễn không tái lập được kết quả chạy thật.
    ///
    /// Chuỗi đầu vào phải tăng dần theo thời gian. Gặp nến chưa đóng nằm giữa chuỗi thì
    /// <b>ném ngoại lệ</b> chứ không lọc bỏ: lọc sẽ tạo chuỗi thủng lỗ, và chỉ báo tính
    /// trên chuỗi thủng thì sai trong im lặng.
    /// </remarks>
    public static IReadOnlyList<Candle> ClosedOnly(this IReadOnlyList<Candle> candles, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(candles);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.UtcNow;

        var cut = candles.Count;
        while (cut > 0 && candles[cut - 1].CloseTime > now) cut--;

        // Kiểm tra phần giữ lại LUÔN chạy, kể cả khi không cắt gì. Bỏ qua bước này khi
        // đuôi chuỗi đã đóng hết sẽ để lọt đúng trường hợp nguy hiểm nhất: nến hở nằm giữa.
        for (var i = 0; i < cut; i++)
        {
            if (candles[i].CloseTime > now)
            {
                throw new ArgumentException(
                    $"Chuỗi nến không tăng dần theo thời gian: nến tại vị trí {i} đóng lúc " +
                    $"{candles[i].CloseTime:O} (sau {now:O}) nhưng vẫn còn nến đã đóng đứng sau nó.",
                    nameof(candles));
            }
        }

        if (cut == candles.Count) return candles;
        if (cut == 0) return Array.Empty<Candle>();

        var closed = new List<Candle>(cut);
        for (var i = 0; i < cut; i++) closed.Add(candles[i]);
        return closed;
    }
}
