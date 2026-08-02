using MMW.Application.MarketData.Models;

namespace MMW.Application.MarketData;

/// <summary>
/// Cổng ĐẶT LỆNH THẬT trên sàn (cần API key có quyền Futures trading).
/// Tách khỏi IExchangeAccountProvider (read-only) để ranh giới rủi ro rõ ràng.
/// </summary>
public interface IExchangeOrderProvider
{
    /// <summary>Đặt 1 lệnh USDT-M Futures.</summary>
    Task<ExchangeOrderResult> PlaceFuturesOrderAsync(FuturesOrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chuẩn hoá khối lượng theo precision (stepSize) của symbol và ÉP LÊN mức tối thiểu
    /// (minQty) nếu nhỏ hơn. Trả về khối lượng hợp lệ để gửi sàn.
    /// </summary>
    Task<decimal> NormalizeQuantityAsync(string symbol, decimal desiredQty, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chuẩn hoá khối lượng theo precision và ép LÊN để đạt notional tối thiểu
    /// (EntryPrice × Quantity). Dùng cho lệnh mở mới, không dùng cho reduceOnly.
    /// </summary>
    Task<decimal> NormalizeQuantityForNotionalAsync(
        string symbol,
        decimal desiredQty,
        decimal entryPrice,
        decimal minNotionalUsdt,
        CancellationToken cancellationToken = default);

    /// <summary>Đặt đòn bẩy cho symbol trước khi vào lệnh.</summary>
    Task SetLeverageAsync(string symbol, int leverage, CancellationToken cancellationToken = default);

    /// <summary>Huỷ 1 lệnh theo orderId.</summary>
    Task CancelOrderAsync(string symbol, string orderId, CancellationToken cancellationToken = default);

    /// <summary>Lấy các vị thế đang mở (positionAmt != 0). Lọc theo symbol nếu truyền.</summary>
    Task<IReadOnlyList<ExchangePosition>> GetOpenPositionsAsync(string? symbol = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lấy các lệnh chờ trên sàn (LIMIT đợi khớp + STOP/TP đang treo). Lọc theo symbol nếu truyền.
    /// Chỉ đọc, không lưu DB.
    /// </summary>
    Task<IReadOnlyList<ExchangeOpenOrder>> GetOpenOrdersAsync(string? symbol = null, CancellationToken cancellationToken = default);

    /// <summary>Huỷ toàn bộ lệnh chờ (SL/TP) của symbol.</summary>
    Task CancelAllOpenOrdersAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>Đóng toàn bộ vị thế hiện tại của symbol bằng MARKET reduceOnly. No-op nếu không có vị thế.</summary>
    Task ClosePositionAsync(string symbol, CancellationToken cancellationToken = default);
}
