using MMW.Domain.Entities;

namespace MMW.Web.Models;

/// <summary>Dữ liệu cho màn hình kế hoạch ngày.</summary>
public class DailyPlanViewModel
{
    public DateTime UtcNow { get; set; }
    public DateOnly TodayUtc { get; set; }
    public int HistoryDays { get; set; }
    public string? AccountName { get; set; }

    /// <summary>Kế hoạch đang có hiệu lực. Null ⟹ mọi lệnh mới bị chặn (FR-023).</summary>
    public DailyPlan? Today { get; set; }

    /// <summary>Kế hoạch ngày mai, có sau khi job 23:30 UTC chạy.</summary>
    public DailyPlan? Tomorrow { get; set; }

    public IReadOnlyList<DailyPlan> History { get; set; } = Array.Empty<DailyPlan>();

    /// <summary>Việt Nam ở UTC+7 cố định quanh năm.</summary>
    public static string Vn(DateTime utc) => utc.AddHours(7).ToString("HH:mm dd/MM");
}
