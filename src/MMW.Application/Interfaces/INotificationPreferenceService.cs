using MMW.Application.Models;

namespace MMW.Application.Interfaces;

public interface INotificationPreferenceService
{
    Task<NotificationSettingsModel> GetSettingsAsync(long userId, CancellationToken cancellationToken = default);
    Task UpdateSettingsAsync(long userId, string? email, IReadOnlyList<NotificationPreferenceUpdateModel> preferences, CancellationToken cancellationToken = default);
}
