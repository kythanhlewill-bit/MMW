using Microsoft.EntityFrameworkCore;
using MMW.Application.Interfaces;
using MMW.Application.Models;
using MMW.Domain.Constants;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;

namespace MMW.Application.Services;

public class NotificationService : INotificationService
{
    private readonly IBaseRepository<Notification> _notifications;
    private readonly IBaseRepository<NotificationPreference> _preferences;
    private readonly IBaseRepository<NotificationDelivery> _deliveries;
    private readonly IBaseRepository<User> _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRealtimeNotificationSender _realtime;
    private readonly INotificationEmailQueue _emailQueue;

    public NotificationService(
        IBaseRepository<Notification> notifications,
        IBaseRepository<NotificationPreference> preferences,
        IBaseRepository<NotificationDelivery> deliveries,
        IBaseRepository<User> users,
        IUnitOfWork unitOfWork,
        IRealtimeNotificationSender realtime,
        INotificationEmailQueue emailQueue)
    {
        _notifications = notifications;
        _preferences = preferences;
        _deliveries = deliveries;
        _users = users;
        _unitOfWork = unitOfWork;
        _realtime = realtime;
        _emailQueue = emailQueue;
    }

    public async Task<IReadOnlyList<NotificationModel>> PublishAsync(NotificationCreateModel model, CancellationToken cancellationToken = default)
    {
        Validate(model);

        var users = await ResolveUsersAsync(model.UserIds);
        var created = new List<NotificationModel>();

        foreach (var user in users)
        {
            if (!ShouldCreateForUser(user.Id, model))
                continue;

            if (await IsDuplicateAsync(user.Id, model))
                continue;

            var inAppEnabled = await IsChannelEnabledAsync(user.Id, model.Type, model.Severity, NotificationChannel.InApp);
            var emailEnabled = await IsChannelEnabledAsync(user.Id, model.Type, model.Severity, NotificationChannel.Email)
                && !string.IsNullOrWhiteSpace(user.Email);

            if (!inAppEnabled && !emailEnabled)
                continue;

            var notification = new Notification
            {
                UserId = user.Id,
                Type = model.Type,
                Severity = model.Severity,
                Title = model.Title.Trim(),
                Message = model.Message.Trim(),
                Source = TrimToNull(model.Source),
                SourceKey = TrimToNull(model.SourceKey),
                RelatedSymbol = TrimToNull(model.RelatedSymbol)?.ToUpperInvariant(),
                RelatedUrl = TrimToNull(model.RelatedUrl),
                PayloadJson = TrimToNull(model.PayloadJson),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = model.ExpiresAt,
            };

            await _notifications.AddAsync(notification);
            await _unitOfWork.CommitAsync(cancellationToken);

            if (inAppEnabled)
            {
                await _deliveries.AddAsync(new NotificationDelivery
                {
                    NotificationId = notification.Id,
                    Channel = NotificationChannel.InApp,
                    Status = NotificationDeliveryStatus.Sent,
                    SentAt = DateTime.UtcNow,
                });
            }

            if (emailEnabled)
            {
                await _deliveries.AddAsync(new NotificationDelivery
                {
                    NotificationId = notification.Id,
                    Channel = NotificationChannel.Email,
                    Status = NotificationDeliveryStatus.Pending,
                });
            }

            await _unitOfWork.CommitAsync(cancellationToken);

            var dto = Map(notification);
            created.Add(dto);

            if (inAppEnabled)
            {
                var unread = await GetUnreadCountAsync(user.Id, cancellationToken);
                await _realtime.SendToUserAsync(user.Id, dto, unread, cancellationToken);
            }

            if (emailEnabled)
                await _emailQueue.QueueAsync(notification.Id, cancellationToken);
        }

        return created;
    }

    public async Task<IReadOnlyList<NotificationModel>> GetRecentAsync(long userId, int take = 20, CancellationToken cancellationToken = default)
    {
        return await GetRecentAsync(userId, 0, take, cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationModel>> GetRecentAsync(long userId, int skip, int take, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var items = await _notifications.Queryable
            .AsNoTracking()
            .Where(n => n.UserId == userId && (n.ExpiresAt == null || n.ExpiresAt > now))
            .OrderByDescending(n => n.CreatedAt)
            .Skip(Math.Max(skip, 0))
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(cancellationToken);

        return items.Select(Map).ToList();
    }

    public async Task<int> GetUnreadCountAsync(long userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await _notifications.Queryable
            .AsNoTracking()
            .CountAsync(n => n.UserId == userId && !n.IsRead && (n.ExpiresAt == null || n.ExpiresAt > now), cancellationToken);
    }

    public async Task MarkAsReadAsync(long userId, long notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.FindAsync(notificationId);
        if (notification is null || notification.UserId != userId)
            return;

        notification.IsRead = true;
        notification.ReadAt = DateTime.UtcNow;
        _notifications.Update(notification);
        await _unitOfWork.CommitAsync(cancellationToken);
    }

    public async Task MarkAllAsReadAsync(long userId, CancellationToken cancellationToken = default)
    {
        var unread = await _notifications.Queryable
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync(cancellationToken);

        foreach (var item in unread)
        {
            item.IsRead = true;
            item.ReadAt = DateTime.UtcNow;
        }

        if (unread.Count > 0)
        {
            _notifications.UpdateRange(unread);
            await _unitOfWork.CommitAsync(cancellationToken);
        }
    }

    private async Task<List<User>> ResolveUsersAsync(IReadOnlyList<long>? userIds)
    {
        if (userIds is { Count: > 0 })
        {
            var ids = userIds.Distinct().ToList();
            return await _users.Queryable
                .AsNoTracking()
                .Where(u => ids.Contains(u.Id) && u.IsActive)
                .ToListAsync();
        }

        return await _users.Queryable
            .AsNoTracking()
            .Where(u => u.IsActive)
            .ToListAsync();
    }

    private static bool ShouldCreateForUser(long userId, NotificationCreateModel model) => userId > 0 && !string.IsNullOrWhiteSpace(model.Title);

    private async Task<bool> IsDuplicateAsync(long userId, NotificationCreateModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Source) || string.IsNullOrWhiteSpace(model.SourceKey))
            return false;

        return await _notifications.AnyAsync(n =>
            n.UserId == userId &&
            n.Type == model.Type &&
            n.Source == model.Source &&
            n.SourceKey == model.SourceKey);
    }

    private async Task<bool> IsChannelEnabledAsync(long userId, NotificationType type, NotificationSeverity severity, NotificationChannel channel)
    {
        var pref = (await _preferences.FindListAsync(p => p.UserId == userId && p.Type == type)).FirstOrDefault();
        var def = NotificationTypeConstant.Get(type);

        var minSeverity = pref?.MinSeverity ?? def.DefaultMinSeverity;
        if (severity < minSeverity)
            return false;

        return channel switch
        {
            NotificationChannel.Email => pref?.EmailEnabled ?? def.DefaultEmailEnabled,
            _ => pref?.InAppEnabled ?? def.DefaultInAppEnabled,
        };
    }

    private static void Validate(NotificationCreateModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Title))
            throw new ArgumentException("Notification title is required.");
        if (string.IsNullOrWhiteSpace(model.Message))
            throw new ArgumentException("Notification message is required.");
    }

    private static NotificationModel Map(Notification n) => new()
    {
        Id = n.Id,
        UserId = n.UserId,
        Type = n.Type,
        Severity = n.Severity,
        Title = n.Title,
        Message = n.Message,
        RelatedSymbol = n.RelatedSymbol,
        RelatedUrl = n.RelatedUrl,
        IsRead = n.IsRead,
        CreatedAt = n.CreatedAt,
    };

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
