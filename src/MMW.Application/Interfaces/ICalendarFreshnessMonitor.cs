namespace MMW.Application.Interfaces;

/// <summary>
/// Phát cảnh báo khi phần lịch NẠP TAY đã quá hạn (FR-014).
/// </summary>
/// <remarks>
/// Tách khỏi <c>ITimeGuardService</c> có chủ ý: tầng quyết định phải thuần và tất định, còn gửi
/// thông báo là tác dụng phụ. Gộp vào sẽ kéo <c>INotificationService</c> vào namespace bị bộ gác
/// hiến chương canh.
///
/// Kịch bản mà lớp này tồn tại để chống: tháng 1 sang năm không ai nhớ nạp lịch CPI/FOMC mới.
/// Không có ngoại lệ nào được ném, không truy vấn nào thất bại — hệ thống chỉ đơn giản là hết
/// chặn tin mạnh, và im lặng.
/// </remarks>
public interface ICalendarFreshnessMonitor
{
    /// <summary>Kiểm tra và phát cảnh báo nếu cần. Trả về đúng khi đã phát.</summary>
    Task<bool> RunAsync(DateTime utcNow, CancellationToken ct = default);
}
