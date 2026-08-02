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

    /// <summary>Đóng vị thế live trên sàn (huỷ SL/TP chờ + đóng MARKET reduceOnly).</summary>
    Task CloseOnExchangeAsync(long tradeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Thử đặt lại SL/TP cho các lệnh live có <see cref="LiveOrderStatus.SltpPending"/>.
    /// Gọi bởi Hangfire job định kỳ.
    /// </summary>
    Task RetryPendingSltpAsync(CancellationToken cancellationToken = default);
}
