using Microsoft.Extensions.Logging;
using MMW.Application.Interfaces;
using MMW.Application.Models;
using MMW.Application.Trading.TimeGuard;
using MMW.Domain.Enums;

namespace MMW.Application.Services;

/// <inheritdoc cref="ICalendarFreshnessMonitor"/>
public sealed class CalendarFreshnessMonitor : ICalendarFreshnessMonitor
{
    private readonly ITimeGuardService _timeGuard;
    private readonly INotificationService _notifications;
    private readonly ILogger<CalendarFreshnessMonitor> _logger;

    public CalendarFreshnessMonitor(
        ITimeGuardService timeGuard,
        INotificationService notifications,
        ILogger<CalendarFreshnessMonitor> logger)
    {
        _timeGuard = timeGuard;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<bool> RunAsync(DateTime utcNow, CancellationToken ct = default)
    {
        var freshness = await _timeGuard.GetCalendarFreshnessAsync(utcNow, ct);
        if (!freshness.IsStale) return false;

        _logger.LogWarning(
            "Lịch sự kiện kinh tế thiếu hoặc đã quá hạn theo loại. coverageEndUtc={CoverageEndUtc:o} " +
            "staleKinds={StaleKinds} evaluatedAtUtc={EvaluatedAtUtc:o}",
            freshness.LastSeededEventUtc,
            string.Join(',', freshness.Kinds.Where(k => k.IsStale).Select(k => k.Kind)),
            utcNow);

        await _notifications.PublishAsync(new NotificationCreateModel
        {
            Type = NotificationType.SystemHealth,

            // Critical chứ không phải Warning: lớp bảo vệ trước CPI/NFP/FOMC đang TẮT mà hệ
            // thống vẫn chạy bình thường. Ngoài ra loại SystemHealth đặt ngưỡng mặc định ở
            // Critical, nên Warning sẽ bị lọc mất và cảnh báo không bao giờ đến tay ai.
            Severity = NotificationSeverity.Critical,
            Title = "Lịch sự kiện kinh tế chưa đầy đủ",
            Message = freshness.WarningVi ?? "Lịch sự kiện kinh tế thiếu hoặc đã quá hạn.",
            Source = nameof(CalendarFreshnessMonitor),

            // Khoá theo NGÀY: nhắc mỗi ngày một lần là đủ để không quên, và không đủ để thành
            // tiếng ồn bị bỏ qua.
            SourceKey = $"calendar-stale:{utcNow:yyyy-MM-dd}",
        }, ct);

        return true;
    }
}
