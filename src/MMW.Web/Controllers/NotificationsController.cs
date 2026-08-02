using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using MMW.Application.Interfaces;
using MMW.Application.Models;
using MMW.Web.Helpers;

namespace MMW.Web.Controllers;

public class NotificationsController : Controller
{
    private readonly INotificationService _notifications;

    public NotificationsController(INotificationService notifications)
    {
        _notifications = notifications;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        var items = await _notifications.GetRecentAsync(userId, 20, cancellationToken);
        return View(items);
    }

    [HttpGet]
    public async Task<IActionResult> Items(int skip = 0, int take = 20, CancellationToken cancellationToken = default)
    {
        var items = await _notifications.GetRecentAsync(CurrentUserId(), skip, take, cancellationToken);
        return Json(items.Select(ToListItem));
    }

    [HttpGet]
    public async Task<IActionResult> Unread(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        var count = await _notifications.GetUnreadCountAsync(userId, cancellationToken);
        var items = await _notifications.GetRecentAsync(userId, 10, cancellationToken);
        return Json(new { count, items });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(long id, CancellationToken cancellationToken)
    {
        await _notifications.MarkAsReadAsync(CurrentUserId(), id, cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        await _notifications.MarkAllAsReadAsync(CurrentUserId(), cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> MarkReadJson(long id, CancellationToken cancellationToken)
    {
        await _notifications.MarkAsReadAsync(CurrentUserId(), id, cancellationToken);
        return Json(new { ok = true });
    }

    private long CurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(value, out var userId) ? userId : 0;
    }

    private static object ToListItem(NotificationModel item) => new
    {
        item.Id,
        Type = item.Type.ToString(),
        Severity = item.Severity.ToString(),
        item.Title,
        item.Message,
        item.RelatedSymbol,
        item.RelatedUrl,
        item.IsRead,
        CreatedAtText = VietnamTimeHelper.Format(item.CreatedAt),
    };
}
