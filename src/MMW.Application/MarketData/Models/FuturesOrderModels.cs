namespace MMW.Application.MarketData.Models;

/// <summary>Phía lệnh trên sàn.</summary>
public enum OrderSide
{
    Buy = 1,
    Sell = 2,
}

/// <summary>Loại lệnh futures gửi lên sàn.</summary>
public enum FuturesOrderKind
{
    Market = 1,
    Limit = 2,
    StopMarket = 3,
    TakeProfitMarket = 4,
}

/// <summary>
/// Phía vị thế mà lệnh thuộc về.
/// One-way mode dùng <see cref="Both"/> (mặc định). Hedge Mode bắt buộc <see cref="Long"/>/<see cref="Short"/>.
/// Với lệnh đóng (SL/TP/close) đây là phía vị thế ĐANG ĐÓNG, không phải phía lệnh.
/// </summary>
public enum FuturesPositionSide
{
    Both = 0,
    Long = 1,
    Short = 2,
}

/// <summary>
/// Yêu cầu đặt 1 lệnh USDT-M Futures. Thuần dữ liệu — không phụ thuộc HTTP.
/// </summary>
public class FuturesOrderRequest
{
    public string Symbol { get; set; } = "";
    public OrderSide Side { get; set; }
    public FuturesOrderKind Kind { get; set; }

    /// <summary>
    /// Phía vị thế. Chỉ được gửi lên sàn khi tài khoản ở Hedge Mode.
    /// Entry: trùng hướng vào. SL/TP/close: hướng vị thế đang đóng.
    /// </summary>
    public FuturesPositionSide PositionSide { get; set; } = FuturesPositionSide.Both;

    /// <summary>Khối lượng. Bỏ qua khi ClosePosition=true.</summary>
    public decimal? Quantity { get; set; }

    /// <summary>Giá đặt cho lệnh LIMIT.</summary>
    public decimal? Price { get; set; }

    /// <summary>Giá kích hoạt cho STOP_MARKET / TAKE_PROFIT_MARKET.</summary>
    public decimal? StopPrice { get; set; }

    /// <summary>Chỉ đóng vị thế (dùng cho SL/TP), không mở thêm.</summary>
    public bool ReduceOnly { get; set; }

    /// <summary>Đóng toàn bộ vị thế khi chạm stop (SL/TP).</summary>
    public bool ClosePosition { get; set; }

    /// <summary>ID client tự sinh để chống đặt trùng (idempotency).</summary>
    public string? NewClientOrderId { get; set; }

    /// <summary>GTC/IOC/FOK cho lệnh LIMIT.</summary>
    public string TimeInForce { get; set; } = "GTC";
}

/// <summary>Kết quả sàn trả về sau khi đặt lệnh.</summary>
public record ExchangeOrderResult(
    string OrderId,
    string? ClientOrderId,
    string Status);

/// <summary>Vị thế đang mở trên sàn (PositionAmt &gt; 0 Long, &lt; 0 Short).</summary>
public record ExchangePosition(
    string Symbol,
    decimal PositionAmt,
    decimal EntryPrice,
    DateTime? UpdatedAtUtc = null)
{
    public bool IsLong => PositionAmt > 0m;
    public bool IsShort => PositionAmt < 0m;
}

/// <summary>
/// Lệnh chờ trên sàn (chưa khớp xong) — gồm LIMIT đang đợi khớp và STOP/TP đang treo.
/// Chỉ đọc, dùng để hiển thị; không lưu DB.
/// </summary>
public record ExchangeOpenOrder(
    string Symbol,
    string OrderId,
    string? ClientOrderId,
    OrderSide Side,
    string Type,
    string PositionSide,
    decimal Price,
    decimal StopPrice,
    decimal OrigQty,
    decimal ExecutedQty,
    bool ReduceOnly,
    bool ClosePosition,
    DateTime CreatedTimeUtc);
