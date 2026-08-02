using Microsoft.AspNetCore.SignalR;
using MMW.Application.Interfaces;
using MMW.Application.Models;
using MMW.Web.Hubs;

namespace MMW.Web.Infrastructure;

public class SignalRNotificationSender : IRealtimeNotificationSender
{
    private readonly IHubContext<NotificationHub> _hub;

    public SignalRNotificationSender(IHubContext<NotificationHub> hub)
    {
        _hub = hub;
    }

    public Task SendToUserAsync(long userId, NotificationModel notification, int unreadCount, CancellationToken cancellationToken = default)
    {
        return _hub.Clients
            .Group(NotificationHub.GroupName(userId))
            .SendAsync("notificationReceived", new { notification, unreadCount }, cancellationToken);
    }
}
