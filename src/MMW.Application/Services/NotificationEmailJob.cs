using MMW.Application.Interfaces;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.Shared.Interfaces;
using System.Text.Json;

namespace MMW.Application.Services;

public class NotificationEmailJob : INotificationEmailJob
{
    private readonly IBaseRepository<Notification> _notifications;
    private readonly IBaseRepository<NotificationDelivery> _deliveries;
    private readonly IBaseRepository<User> _users;
    private readonly IEmailSender _emailSender;
    private readonly IUnitOfWork _unitOfWork;

    public NotificationEmailJob(
        IBaseRepository<Notification> notifications,
        IBaseRepository<NotificationDelivery> deliveries,
        IBaseRepository<User> users,
        IEmailSender emailSender,
        IUnitOfWork unitOfWork)
    {
        _notifications = notifications;
        _deliveries = deliveries;
        _users = users;
        _emailSender = emailSender;
        _unitOfWork = unitOfWork;
    }

    public async Task SendAsync(long notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _notifications.FindAsync(notificationId);
        if (notification is null) return;

        var delivery = (await _deliveries.FindListAsync(d =>
            d.NotificationId == notificationId && d.Channel == NotificationChannel.Email))
            .FirstOrDefault();

        if (delivery is null || delivery.Status == NotificationDeliveryStatus.Sent)
            return;

        var trackedDelivery = await _deliveries.FindAsync(delivery.Id);
        if (trackedDelivery is null) return;

        try
        {
            var user = await _users.FindAsync(notification.UserId);
            if (user is null || string.IsNullOrWhiteSpace(user.Email))
            {
                trackedDelivery.Status = NotificationDeliveryStatus.Skipped;
                trackedDelivery.LastError = "User email is empty.";
                await _unitOfWork.CommitAsync(cancellationToken);
                return;
            }

            if (!_emailSender.IsConfigured)
            {
                trackedDelivery.Status = NotificationDeliveryStatus.Skipped;
                trackedDelivery.LastError = "Email sender is not configured.";
                await _unitOfWork.CommitAsync(cancellationToken);
                return;
            }

            trackedDelivery.AttemptCount++;
            await _emailSender.SendAsync(user.Email, notification.Title, BuildHtml(notification), cancellationToken);

            trackedDelivery.Status = NotificationDeliveryStatus.Sent;
            trackedDelivery.SentAt = DateTime.UtcNow;
            trackedDelivery.LastError = null;
            await _unitOfWork.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            trackedDelivery.AttemptCount++;
            trackedDelivery.Status = NotificationDeliveryStatus.Failed;
            trackedDelivery.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            await _unitOfWork.CommitAsync(cancellationToken);
            throw;
        }
    }

    private static string BuildHtml(Notification notification)
    {
        var symbol = string.IsNullOrWhiteSpace(notification.RelatedSymbol)
            ? ""
            : $"<p><strong>Symbol:</strong> {Escape(notification.RelatedSymbol)}</p>";
        var links = BuildLinks(notification);

        return $"""
            <h2>{Escape(notification.Title)}</h2>
            <p>{Escape(notification.Message)}</p>
            {symbol}
            {links}
            <p style="color:#64748b;font-size:12px">MMW notification · {notification.CreatedAt:yyyy-MM-dd HH:mm} UTC</p>
            """;
    }

    private static string BuildLinks(Notification notification)
    {
        var payload = TryParsePayload(notification.PayloadJson);
        payload.TryGetValue("mmwCreateTradePath", out var mmwPath);
        payload.TryGetValue("binanceUrl", out var binanceUrl);

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(mmwPath))
            parts.Add($"""<a href="{Escape(mmwPath)}" style="display:inline-block;margin-right:8px;padding:10px 14px;background:#2563eb;color:white;text-decoration:none;border-radius:6px">Ghi nhận trong MMW</a>""");

        var exchangeUrl = !string.IsNullOrWhiteSpace(binanceUrl) ? binanceUrl : notification.RelatedUrl;
        if (!string.IsNullOrWhiteSpace(exchangeUrl))
            parts.Add($"""<a href="{Escape(exchangeUrl)}" style="display:inline-block;padding:10px 14px;background:#111827;color:white;text-decoration:none;border-radius:6px">Mở Binance Futures</a>""");

        return parts.Count == 0 ? "" : $"<p>{string.Join("", parts)}</p>";
    }

    private static Dictionary<string, string> TryParsePayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return new Dictionary<string, string>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(payloadJson)
                ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private static string Escape(string value) =>
        System.Net.WebUtility.HtmlEncode(value);
}
