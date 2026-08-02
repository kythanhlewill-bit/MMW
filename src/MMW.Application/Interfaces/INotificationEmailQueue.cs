namespace MMW.Application.Interfaces;

public interface INotificationEmailQueue
{
    Task QueueAsync(long notificationId, CancellationToken cancellationToken = default);
}
