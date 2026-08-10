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

    // ── So sánh với engine tất định (FR-058) ───────────────────────────
    /// <summary>Phiếu tất định gần nhất cùng symbol trong cửa sổ một giờ.</summary>
    public long? EntryScorecardId { get; set; }

    [MaxLength(40)]
    public string? DeterministicOutcome { get; set; }

    [MaxLength(20)]
    public string? DeterministicDirection { get; set; }

    public int? DeterministicScore { get; set; }

    /// <summary>Null khi chưa có phiếu tất định để đối chiếu.</summary>
    public bool? IsDisagreement { get; set; }

    [MaxLength(500)]
    public string? DisagreementReason { get; set; }
}
