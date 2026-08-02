using MMW.Domain.Enums;

namespace MMW.Domain.Entities;

public class NotificationPreference : BaseEntity
{
    public long UserId { get; set; }
    public User User { get; set; } = null!;

    public NotificationType Type { get; set; }
    public bool InAppEnabled { get; set; } = true;
    public bool EmailEnabled { get; set; }
    public NotificationSeverity MinSeverity { get; set; } = NotificationSeverity.Info;
    public TimeSpan? QuietHoursStart { get; set; }
    public TimeSpan? QuietHoursEnd { get; set; }
}
