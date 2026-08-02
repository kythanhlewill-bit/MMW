using MMW.Domain.Enums;

namespace MMW.Application.Models;

public class NotificationCreateModel
{
    public IReadOnlyList<long>? UserIds { get; set; }
    public NotificationType Type { get; set; }
    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string? Source { get; set; }
    public string? SourceKey { get; set; }
    public string? RelatedSymbol { get; set; }
    public string? RelatedUrl { get; set; }
    public string? PayloadJson { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class NotificationModel
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public NotificationType Type { get; set; }
    public NotificationSeverity Severity { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string? RelatedSymbol { get; set; }
    public string? RelatedUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class NotificationPreferenceModel
{
    public NotificationType Type { get; set; }
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool InAppEnabled { get; set; }
    public bool EmailEnabled { get; set; }
    public NotificationSeverity MinSeverity { get; set; }
}

public class NotificationSettingsModel
{
    public long UserId { get; set; }
    public string Username { get; set; } = "";
    public string? Email { get; set; }
    public IReadOnlyList<NotificationPreferenceModel> Preferences { get; set; } = new List<NotificationPreferenceModel>();
}

public class NotificationPreferenceUpdateModel
{
    public NotificationType Type { get; set; }
    public bool InAppEnabled { get; set; }
    public bool EmailEnabled { get; set; }
    public NotificationSeverity MinSeverity { get; set; }
}
