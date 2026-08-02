using MMW.Application.Models;

namespace MMW.Application.Interfaces;

public interface INotificationService
{
    Task<IReadOnlyList<NotificationModel>> PublishAsync(NotificationCreateModel model, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NotificationModel>> GetRecentAsync(long userId, int take = 20, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NotificationModel>> GetRecentAsync(long userId, int skip, int take, CancellationToken cancellationToken = default);
    Task<int> GetUnreadCountAsync(long userId, CancellationToken cancellationToken = default);
    Task MarkAsReadAsync(long userId, long notificationId, CancellationToken cancellationToken = default);
    Task MarkAllAsReadAsync(long userId, CancellationToken cancellationToken = default);
}
