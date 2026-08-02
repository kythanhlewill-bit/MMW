using MMW.Application.MarketData.Models;

namespace MMW.Application.Trading.Structure;

/// <summary>Một điểm xoay đã được xác nhận.</summary>
/// <param name="Index">Vị trí trong chuỗi nến đầu vào.</param>
/// <param name="IsHigh">Đỉnh (<c>true</c>) hay đáy (<c>false</c>).</param>
/// <param name="Price">Giá cao nhất của nến đỉnh, hoặc thấp nhất của nến đáy.</param>
/// <param name="OccurredAtUtc">Thời điểm nến chứa điểm xoay đóng.</param>
/// <param name="ConfirmedAtIndex">Vị trí nến mà tại đó điểm xoay mới xác nhận được.</param>
/// <param name="ConfirmedAtUtc">
/// Thời điểm điểm xoay trở nên BIẾT ĐƯỢC. Luôn muộn hơn <paramref name="OccurredAtUtc"/>
/// đúng <c>N</c> nến. Trường này là bằng chứng chống nhìn trước tương lai — bất kỳ phép
/// tính nào dùng điểm xoay tại thời điểm sớm hơn đây đều đang gian lận.
/// </param>
public sealed record SwingPoint(
    int Index,
    bool IsHigh,
    decimal Price,
    DateTime OccurredAtUtc,
    int ConfirmedAtIndex,
    DateTime ConfirmedAtUtc);

/// <summary>Phát hiện điểm xoay kiểu fractal (R-007).</summary>
public interface ISwingDetector
{
    /// <summary>
    /// Tìm mọi điểm xoay đã xác nhận trong chuỗi. Kết quả sắp theo chỉ số tăng dần.
    /// </summary>
    /// <param name="pivotBars">Số nến hai bên cần vượt qua. Phải &gt; 0.</param>
    IReadOnlyList<SwingPoint> Detect(IReadOnlyList<Candle> candles, int pivotBars);
}
