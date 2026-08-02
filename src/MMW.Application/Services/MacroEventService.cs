using Microsoft.Extensions.Logging;
using MMW.Application.Interfaces;
using MMW.Application.Models;
using MMW.Domain.Enums;

namespace MMW.Application.Services;

public class MacroEventService : IMacroEventService
{
    private static readonly TimeSpan LookAhead = TimeSpan.FromHours(24);
    private static readonly TimeSpan NewsLookBack = TimeSpan.FromHours(12);
    private static readonly TimeSpan AvoidBefore = TimeSpan.FromMinutes(45);
    private static readonly TimeSpan AvoidAfter = TimeSpan.FromMinutes(30);

    private readonly IMacroEventProvider _provider;
    private readonly INotificationService _notifications;
    private readonly ILogger<MacroEventService> _logger;

    public MacroEventService(
        IMacroEventProvider provider,
        INotificationService notifications,
        ILogger<MacroEventService> logger)
    {
        _provider = provider;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<MacroEventContext> GetContextForTradeAsync(
        string symbol,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var context = new MacroEventContext
        {
            IsConfigured = _provider.IsConfigured,
        };

        if (!_provider.IsConfigured)
            return context;

        try
        {
            var events = await _provider.GetEventsAsync(utcNow, LookAhead, NewsLookBack, cancellationToken);
            var relevant = events
                .Where(e => IsRelevant(symbol, e))
                .Where(e => e.Impact >= MacroEventImpact.High)
                .OrderBy(e => e.OccursAtUtc ?? DateTime.MaxValue)
                .Take(8)
                .ToList();

            var blocking = relevant
                .Where(e => IsInAvoidWindow(e, utcNow) || IsFreshMarketShock(e, utcNow))
                .ToList();

            context.Events = relevant;
            context.BlockingEvents = blocking;
            context.HasBlockingEvent = blocking.Count > 0;
            context.Summary = BuildSummary(relevant);

            if (blocking.Count > 0)
            {
                var first = blocking[0];
                context.RiskWarnings.Add(
                    $"Đang gần khung giờ tin mạnh: {first.Title} ({FormatVietnamTime(first.OccursAtUtc)}). Nên chờ qua vùng tin trước khi vào lệnh.");
            }

            if (relevant.Count > 0 && string.IsNullOrWhiteSpace(context.Summary) == false)
                context.RiskWarnings.Add($"Bối cảnh ngoài biểu đồ cần lưu ý: {context.Summary}");

            return context;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build macro context for {Symbol}", symbol);
            context.RiskWarnings.Add("Không đọc được lịch tin/news vĩ mô, hãy kiểm tra thủ công trước khi vào lệnh.");
            return context;
        }
    }

    public async Task<int> ScanAndNotifyAsync(CancellationToken cancellationToken = default)
    {
        if (!_provider.IsConfigured)
            return 0;

        try
        {
            var now = DateTime.UtcNow;
            var events = await _provider.GetEventsAsync(now, LookAhead, NewsLookBack, cancellationToken);
            var important = events
                .Where(e => e.Impact >= MacroEventImpact.High)
                .Where(e => IsInNotifyWindow(e, now) || IsFreshMarketShock(e, now))
                .OrderBy(e => e.OccursAtUtc ?? DateTime.MaxValue)
                .Take(30)
                .ToList();

            var published = 0;
            foreach (var item in important)
            {
                await _notifications.PublishAsync(new NotificationCreateModel
                {
                    Type = MapNotificationType(item),
                    Severity = item.Impact >= MacroEventImpact.Critical ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                    Title = BuildNotificationTitle(item),
                    Message = BuildNotificationMessage(item),
                    Source = $"macro:{item.Source}",
                    SourceKey = item.SourceKey,
                    RelatedSymbol = item.Currency,
                    RelatedUrl = item.Url,
                    ExpiresAt = (item.OccursAtUtc ?? now).AddHours(2),
                }, cancellationToken);

                published++;
            }

            return published;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Macro event scan failed");
            return 0;
        }
    }

    private static bool IsRelevant(string symbol, MacroEventModel item)
    {
        var text = $"{item.Currency} {item.Title} {item.Summary}".ToUpperInvariant();
        var normalizedSymbol = (symbol ?? "").ToUpperInvariant();

        if (text.Contains("GLOBAL") || text.Contains("CRYPTO") || text.Contains("BINANCE"))
            return true;

        if (normalizedSymbol.EndsWith("USDT", StringComparison.Ordinal) || normalizedSymbol.EndsWith("USD", StringComparison.Ordinal))
            return ContainsAny(text, "USD", "US ", "FOMC", "FED", "CPI", "NFP", "PAYROLL", "PCE", "GDP");

        return ContainsAny(text, "USD", "EUR", "GBP", "JPY", "CNY", "FED", "ECB", "BOE", "BOJ", "PBOC");
    }

    private static bool IsInAvoidWindow(MacroEventModel item, DateTime utcNow)
    {
        if (item.OccursAtUtc is not DateTime occursAtUtc)
            return false;

        return occursAtUtc >= utcNow.Subtract(AvoidAfter)
            && occursAtUtc <= utcNow.Add(AvoidBefore);
    }

    private static bool IsInNotifyWindow(MacroEventModel item, DateTime utcNow)
    {
        if (item.OccursAtUtc is not DateTime occursAtUtc)
            return false;

        return occursAtUtc >= utcNow.Subtract(TimeSpan.FromMinutes(10))
            && occursAtUtc <= utcNow.Add(TimeSpan.FromHours(6));
    }

    private static bool IsFreshMarketShock(MacroEventModel item, DateTime utcNow)
    {
        if (item.Kind is not (MacroEventKind.Geopolitical or MacroEventKind.Regulation or MacroEventKind.CentralBank))
            return false;
        if (item.OccursAtUtc is not DateTime occursAtUtc)
            return false;

        return occursAtUtc >= utcNow.Subtract(NewsLookBack) && occursAtUtc <= utcNow;
    }

    private static string BuildSummary(IReadOnlyList<MacroEventModel> events)
    {
        if (events.Count == 0)
            return "";

        return string.Join(" | ", events
            .Take(3)
            .Select(e => $"{e.Title} ({FormatVietnamTime(e.OccursAtUtc)})"));
    }

    private static string BuildNotificationTitle(MacroEventModel item)
    {
        return item.Kind switch
        {
            MacroEventKind.CentralBank => $"Tin NHTW: {item.Title}",
            MacroEventKind.Geopolitical => $"Tin địa chính trị: {item.Title}",
            MacroEventKind.Regulation => $"Tin pháp lý: {item.Title}",
            _ => $"Tin/lịch kinh tế: {item.Title}",
        };
    }

    private static string BuildNotificationMessage(MacroEventModel item)
    {
        var time = FormatVietnamTime(item.OccursAtUtc);
        var summary = string.IsNullOrWhiteSpace(item.Summary) ? "" : $" · {item.Summary}";
        return $"{time} · {item.Impact} · {item.Currency ?? "GLOBAL"}{summary}";
    }

    private static NotificationType MapNotificationType(MacroEventModel item)
    {
        return item.Kind switch
        {
            MacroEventKind.CentralBank => NotificationType.CentralBankEvent,
            MacroEventKind.Geopolitical => NotificationType.WarConflictEscalation,
            MacroEventKind.Regulation => NotificationType.SanctionAlert,
            _ => NotificationType.EconomicCalendarHighImpact,
        };
    }

    private static bool ContainsAny(string text, params string[] keywords)
    {
        return keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatVietnamTime(DateTime? utc)
    {
        if (utc is null)
            return "vừa cập nhật";

        var timeZone = ResolveVietnamTimeZone();
        var value = utc.Value.Kind == DateTimeKind.Utc
            ? utc.Value
            : DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(value, timeZone).ToString("HH:mm dd/MM");
    }

    private static TimeZoneInfo ResolveVietnamTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
        }
        catch
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        }
    }
}
