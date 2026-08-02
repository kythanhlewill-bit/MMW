using Hangfire;
using MMW.Application.Interfaces;

namespace MMW.Web.Infrastructure;

public class HangfireNotificationEmailQueue : INotificationEmailQueue
{
    private readonly IBackgroundJobClient _jobs;

    public HangfireNotificationEmailQueue(IBackgroundJobClient jobs)
    {
        _jobs = jobs;
    }

    public Task QueueAsync(long notificationId, CancellationToken cancellationToken = default)
    {
        _jobs.Enqueue<INotificationEmailJob>(job => job.SendAsync(notificationId, CancellationToken.None));
        return Task.CompletedTask;
    }
}
