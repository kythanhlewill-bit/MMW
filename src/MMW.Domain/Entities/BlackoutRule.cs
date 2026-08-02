using MMW.Domain.Enums;

namespace MMW.Domain.Entities;

/// <summary>
/// Độ rộng cửa sổ chặn cho một loại sự kiện. Con của <see cref="EngineSetting"/> (FR-010).
/// </summary>
public class BlackoutRule : BaseEntity
{
    public long EngineSettingId { get; set; }
    public EngineSetting EngineSetting { get; set; } = null!;

    public ScheduledEventKind EventKind { get; set; }

    /// <summary>Số phút chặn TRƯỚC mốc sự kiện.</summary>
    public int MinutesBefore { get; set; }

    /// <summary>Số phút chặn SAU mốc sự kiện (hoặc sau khi sự kiện kết thúc, nếu có độ dài).</summary>
    public int MinutesAfter { get; set; }

    /// <summary>Cửa sổ này có chặn lệnh mới không.</summary>
    public bool BlocksNewEntries { get; set; } = true;

    /// <summary>
    /// Cửa sổ này có buộc xử lý vị thế đang mở không (kéo dừng lỗ về hoà vốn hoặc đóng một nửa).
    /// Vị thế đang mở khi bước vào cửa sổ chặn KHÔNG được để trần (FR-013).
    /// </summary>
    public bool RequiresPositionAction { get; set; }
}
