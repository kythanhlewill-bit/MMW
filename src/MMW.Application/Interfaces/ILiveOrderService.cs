namespace MMW.Application.Interfaces;

/// <summary>
/// Gửi lệnh THẬT lên sàn cho một Trade đã tạo. An toàn theo lớp:
/// master switch tắt / thiếu key / vượt cap / vi phạm rule Critical → KHÔNG gửi.
/// Idempotent: trade đã IsLive sẽ bỏ qua.
/// </summary>
public interface ILiveOrderService
{
    Task PlaceForTradeAsync(long tradeId, CancellationToken cancellationToken = default);

    /// <summary>Đồng bộ SL/TP của lệnh live lên sàn (huỷ SL/TP cũ, đặt lại theo giá mới).</summary>
    Task SyncLevelsAsync(long tradeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Đóng vị thế live trên sàn (huỷ SL/TP chờ + đóng MARKET reduceOnly).
    /// Trả <c>true</c> khi lệnh đóng THẬT SỰ tới được sàn.
    /// </summary>
    /// <remarks>
    /// Trả kết quả chứ không phải <c>Task</c> trần vì phương thức này NUỐT lỗi: sàn không với
    /// tới được là chuyện thường (mạng, hạn mức, lệnh cấm IP) và không được phép giết cả vòng
    /// job. Nhưng người gọi cần phân biệt "đã đóng" với "đã thử" — nếu không, họ sẽ ghi nhật ký
    /// và bắn thông báo nói vị thế đã đóng trong khi nó vẫn đang mở.
    ///
    /// Đúng chuyện đó đã xảy ra ngày 30/08/2026 lúc 18:30: dừng thời gian báo "đã đóng lệnh #72"
    /// trong khi Binance đang cấm IP nên lệnh đóng chưa bao giờ rời máy.
    /// </remarks>
    Task<bool> CloseOnExchangeAsync(long tradeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Thử đặt lại SL/TP cho các lệnh live có <see cref="LiveOrderStatus.SltpPending"/>.
    /// Gọi bởi Hangfire job định kỳ.
    /// </summary>
    Task RetryPendingSltpAsync(CancellationToken cancellationToken = default);
}
