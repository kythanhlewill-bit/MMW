using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace MMW.Domain.Entities;

/// <summary>
/// Audit đầy đủ mỗi lần AI được yêu cầu phân tích một symbol trong market scan.
/// </summary>
public class AiSignalScanRecord : BaseEntity
{
    [Required, MaxLength(30)]
    public string Symbol { get; set; } = null!;

    [Required, MaxLength(10)]
    public string Interval { get; set; } = "1h";

    public DateTime ScannedAt { get; set; }

    [Precision(18, 8)] public decimal Price { get; set; }
    [Precision(9, 4)] public decimal? Rsi { get; set; }
    [Precision(18, 8)] public decimal? Ema20 { get; set; }
    [Precision(18, 8)] public decimal? Ema50 { get; set; }
    [Precision(18, 8)] public decimal? MacdHistogram { get; set; }
    [Precision(18, 8)] public decimal? Atr { get; set; }

    /// <summary>Configured, NotConfigured, Responded, Repaired, InvalidJson, Wait, Accepted, Rejected.</summary>
    [MaxLength(40)]
    public string Status { get; set; } = "Created";

    [MaxLength(20)]
    public string? Action { get; set; }

    public int? Score { get; set; }
    [Precision(9, 4)] public decimal? Confidence { get; set; }

    [Precision(18, 8)] public decimal? Entry { get; set; }
    [Precision(18, 8)] public decimal? StopLoss { get; set; }
    [Precision(18, 8)] public decimal? TakeProfit { get; set; }
    [Precision(9, 4)] public decimal? RiskReward { get; set; }

    [MaxLength(500)]
    public string? RejectReason { get; set; }

    [MaxLength(500)]
    public string? AiReason { get; set; }

    public string? SystemPrompt { get; set; }
    public string? RequestJson { get; set; }
    public string? ResponseJson { get; set; }
    public string? RepairResponseJson { get; set; }
}
