using Microsoft.EntityFrameworkCore;

namespace MMW.Domain.Entities;

/// <summary>Kho nến lịch sử, cho phép kiểm thử lịch sử chạy offline.</summary>
/// <remarks>
/// KHÔNG lưu cờ đã-đóng: suy ra từ <see cref="CloseTimeUtc"/> theo R-002, giữ cho kho lịch sử
/// và sàn hành xử giống hệt nhau. Lưu cờ tĩnh sẽ tạo khác biệt ở đúng chỗ mà tính tương đương
/// giữa kiểm thử và chạy thật phụ thuộc vào.
///
/// Ước lượng: 2 symbol × 3 khung × 2 năm ≈ 160k dòng.
/// </remarks>
public class KlineArchive : BaseEntity
{
    public string Symbol { get; set; } = string.Empty;
    public string Interval { get; set; } = string.Empty;

    public DateTime OpenTimeUtc { get; set; }
    public DateTime CloseTimeUtc { get; set; }

    [Precision(18, 8)] public decimal Open { get; set; }
    [Precision(18, 8)] public decimal High { get; set; }
    [Precision(18, 8)] public decimal Low { get; set; }
    [Precision(18, 8)] public decimal Close { get; set; }
    [Precision(18, 8)] public decimal Volume { get; set; }

    [Precision(18, 8)] public decimal? QuoteVolume { get; set; }
    public int? TradeCount { get; set; }
}
