using MMW.Domain.Enums;

namespace MMW.Domain.Entities;

/// <summary>
/// Một sự kiện trên cuốn lịch nội bộ. Nguồn dữ liệu cho tầng chặn theo khung giờ.
/// </summary>
/// <remarks>
/// Sự kiện là BẤT BIẾN sau khi tạo — không có vòng đời trạng thái. Sửa lịch nghĩa là
/// xoá và nạp lại.
///
/// <see cref="ScheduledEventOrigin.AiDetected"/> chỉ dùng cho tin sốc đột xuất.
/// Sự kiện có ngày giờ cố định (CPI, FOMC, NFP) PHẢI là <c>Seeded</c> — nạp tay từ
/// lịch công bố của BLS/Fed. Để AI sinh ngày giờ là mời nó bịa, và một cửa sổ chặn
/// đặt sai giờ còn tệ hơn không có cửa sổ nào.
/// </remarks>
public class ScheduledEvent : BaseEntity
{
    public ScheduledEventKind Kind { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime OccursAtUtc { get; set; }

    /// <summary>Độ dài sự kiện, dùng cho loại có khoảng thời gian như họp báo FOMC.</summary>
    public int? DurationMinutes { get; set; }

    public MacroEventImpact Impact { get; set; }
    public ScheduledEventOrigin Origin { get; set; }
    public string? Currency { get; set; }

    /// <summary>Khoá chống nạp trùng. Duy nhất khi khác null.</summary>
    public string? SourceKey { get; set; }

    public string? Notes { get; set; }
}
