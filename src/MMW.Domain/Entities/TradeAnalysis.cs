using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using MMW.Domain.Enums;

namespace MMW.Domain.Entities;

/// <summary>
/// Kết quả phân tích lệnh đang mở — cập nhật mỗi lần job chạy.
/// Mỗi trade chỉ có 1 bản phân tích mới nhất (upsert).
/// </summary>
public class TradeAnalysis : BaseEntity
{
    public long TradeId { get; set; }
    public Trade Trade { get; set; } = null!;

    // --- Giá hiện tại & PnL chưa chốt ---
    [Precision(18, 8)] public decimal CurrentPrice { get; set; }
    [Precision(18, 8)] public decimal UnrealizedPnl { get; set; }
    [Precision(9, 4)] public decimal UnrealizedPnlPercent { get; set; }

    // --- Khoảng cách tới SL/TP (%) ---
    [Precision(9, 4)] public decimal? DistanceToSlPercent { get; set; }
    [Precision(9, 4)] public decimal? DistanceToTpPercent { get; set; }

    // --- Indicator hiện tại ---
    [Precision(18, 8)] public decimal? Rsi { get; set; }
    [Precision(18, 8)] public decimal? Ema20 { get; set; }
    [Precision(18, 8)] public decimal? Ema50 { get; set; }
    public MarketBias Bias { get; set; }

    // --- Phân tích & lời khuyên ---
    [MaxLength(50)] public string RiskLevel { get; set; } = "Normal";
    [MaxLength(2000)] public string Advice { get; set; } = "";
    [MaxLength(2000)] public string? Details { get; set; }
    public bool AiEnhanced { get; set; }

    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
}
