using MMW.Application.Models;

namespace MMW.Application.Interfaces;

public interface IRealtimeNotificationSender
{
    Task SendToUserAsync(long userId, NotificationModel notification, int unreadCount, CancellationToken cancellationToken = default);
}
