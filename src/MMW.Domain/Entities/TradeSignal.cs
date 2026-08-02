using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using MMW.Domain.Enums;

namespace MMW.Domain.Entities;

/// <summary>
/// Đề xuất lệnh sinh tự động từ AI mỗi lần quét. Lưu lịch sử toàn bộ.
/// Đây CHỈ là gợi ý — không phải lệnh thật trong journal.
/// </summary>
public class TradeSignal : BaseEntity
{
    [Required, MaxLength(30)]
    public string Symbol { get; set; } = null!;

    [Required, MaxLength(10)]
    public string Interval { get; set; } = "1h";

    public TradeDirection Direction { get; set; }
    public MarketBias Bias { get; set; }

    /// <summary>Điểm tín hiệu (độ mạnh) từ analyzer.</summary>
    public int Score { get; set; }

    [Precision(18, 8)] public decimal Entry { get; set; }
    [Precision(18, 8)] public decimal StopLoss { get; set; }
    [Precision(18, 8)] public decimal TakeProfit { get; set; }
    [Precision(9, 4)] public decimal RiskReward { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }

    public DateTime CreatedAt { get; set; }
}
