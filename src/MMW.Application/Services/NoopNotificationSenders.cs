using MMW.Application.Interfaces;
using MMW.Application.Models;

namespace MMW.Application.Services;

public class NoopRealtimeNotificationSender : IRealtimeNotificationSender
{
    public Task SendToUserAsync(long userId, NotificationModel notification, int unreadCount, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

public class NoopNotificationEmailQueue : INotificationEmailQueue
{
    public Task QueueAsync(long notificationId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
