using MMW.Application.Trading.TimeGuard;
using MMW.Domain.Entities;

namespace MMW.Web.Models;

/// <summary>Dữ liệu cho màn hình chặn theo khung giờ.</summary>
public class TimeGuardViewModel
{
    public DateTime UtcNow { get; set; }
    public int HorizonHours { get; set; }
    public string? AccountName { get; set; }

    /// <summary>Tình trạng cập nhật của phần lịch nạp tay (FR-014).</summary>
    public CalendarFreshness Freshness { get; set; } = new(false, null, null);

    /// <summary>Có đang bị chặn ngay lúc này không. Null khi chưa có tài khoản nào.</summary>
    public BlackoutDecision? Current { get; set; }

    public IReadOnlyList<BlackoutWindow> Windows { get; set; } = Array.Empty<BlackoutWindow>();
    public IReadOnlyList<ScheduledEvent> Events { get; set; } = Array.Empty<ScheduledEvent>();

    public SessionQuality? SessionQuality { get; set; }

    /// <summary>Việt Nam ở UTC+7 cố định quanh năm, không có giờ mùa hè.</summary>
    public static string Vn(DateTime utc) => utc.AddHours(7).ToString("HH:mm dd/MM");
}
