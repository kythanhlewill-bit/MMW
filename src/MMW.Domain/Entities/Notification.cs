using System.ComponentModel.DataAnnotations;
using MMW.Domain.Enums;

namespace MMW.Domain.Entities;

public class Notification : BaseEntity
{
    public long UserId { get; set; }
    public User User { get; set; } = null!;

    public NotificationType Type { get; set; }
    public NotificationSeverity Severity { get; set; }

    [Required, MaxLength(200)]
    public string Title { get; set; } = "";

    [Required, MaxLength(2000)]
    public string Message { get; set; } = "";

    [MaxLength(100)]
    public string? Source { get; set; }

    [MaxLength(200)]
    public string? SourceKey { get; set; }

    [MaxLength(30)]
    public string? RelatedSymbol { get; set; }

    [MaxLength(500)]
    public string? RelatedUrl { get; set; }

    public string? PayloadJson { get; set; }

    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }

    public ICollection<NotificationDelivery> Deliveries { get; set; } = new List<NotificationDelivery>();
}
