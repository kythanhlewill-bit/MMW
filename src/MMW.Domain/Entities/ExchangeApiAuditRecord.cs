using System.ComponentModel.DataAnnotations;

namespace MMW.Domain.Entities;

/// <summary>
/// Audit request/response khi gọi API sàn. Không lưu secret/signature/API key thô.
/// </summary>
public class ExchangeApiAuditRecord : BaseEntity
{
    [Required, MaxLength(30)]
    public string Exchange { get; set; } = "Binance";

    [MaxLength(30)]
    public string? Symbol { get; set; }

    [Required, MaxLength(10)]
    public string Method { get; set; } = "";

    [Required, MaxLength(200)]
    public string Path { get; set; } = "";

    [MaxLength(100)]
    public string? ClientOrderId { get; set; }

    public DateTime RequestedAtUtc { get; set; }
    public DateTime? RespondedAtUtc { get; set; }
    public int? DurationMs { get; set; }
    public int? StatusCode { get; set; }

    public bool Succeeded { get; set; }

    public string? RequestJson { get; set; }
    public string? ResponseJson { get; set; }

    [MaxLength(500)]
    public string? Error { get; set; }
}
