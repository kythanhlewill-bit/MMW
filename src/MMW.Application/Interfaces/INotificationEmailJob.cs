namespace MMW.Application.Interfaces;

public interface INotificationEmailJob
{
    Task SendAsync(long notificationId, CancellationToken cancellationToken = default);
}
