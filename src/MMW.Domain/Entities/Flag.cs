using System.ComponentModel.DataAnnotations;
using MMW.Domain.Enums;

namespace MMW.Domain.Entities;

/// <summary>
/// Cờ cảnh báo — hợp nhất vi phạm Rule Engine (Category=RuleViolation) và hành vi xấu
/// (Category=Behavior). Gắn vào một lệnh hoặc một ngày giao dịch.
/// </summary>
public class Flag : BaseEntity
{
    public long TradingAccountId { get; set; }
    public TradingAccount TradingAccount { get; set; } = null!;

    /// <summary>Lệnh liên quan (nếu cờ ở cấp lệnh).</summary>
    public long? TradeId { get; set; }
    public Trade? Trade { get; set; }

    /// <summary>Ngày liên quan (nếu cờ ở cấp ngày: overtrade, daily loss...).</summary>
    public long? TradingDayId { get; set; }
    public TradingDay? TradingDay { get; set; }

    public FlagCategory Category { get; set; }
    public FlagType Type { get; set; }
    public FlagSeverity Severity { get; set; } = FlagSeverity.Warning;

    [Required, MaxLength(500)]
    public string Message { get; set; } = null!;

    /// <summary>Ngữ cảnh có cấu trúc (JSON) — vd: giá trị thực tế vs ngưỡng.</summary>
    public string? DetailJson { get; set; }

    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    public bool IsAcknowledged { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
}
