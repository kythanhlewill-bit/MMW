using MMW.Domain.Enums;

namespace MMW.Domain.Entities;

public class NotificationDelivery : BaseEntity
{
    public long NotificationId { get; set; }
    public Notification Notification { get; set; } = null!;

    public NotificationChannel Channel { get; set; }
    public NotificationDeliveryStatus Status { get; set; } = NotificationDeliveryStatus.Pending;
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTime? SentAt { get; set; }
}
