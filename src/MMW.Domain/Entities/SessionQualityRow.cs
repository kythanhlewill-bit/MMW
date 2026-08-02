namespace MMW.Domain.Entities;

/// <summary>
/// Một khoảng giờ UTC và điểm chất lượng của nó. Con của <see cref="EngineSetting"/>.
/// </summary>
/// <remarks>
/// Bảng này là bản cold-start: dùng khi tài khoản chưa đủ
/// <see cref="EngineSetting.PersonalStatsMinClosedTrades"/> lệnh đã đóng để tính
/// thống kê giờ cá nhân (FR-030, FR-031).
///
/// Ràng buộc: các khoảng phải phủ kín 0–24 và không chồng lấn. Kiểm tra khi LƯU,
/// không phải khi đọc — một bảng thủng lỗ mà chỉ phát hiện lúc chấm điểm sẽ biến
/// thành "thiếu dữ liệu ⟹ 0 điểm" và im lặng.
/// </remarks>
public class SessionQualityRow : BaseEntity
{
    public long EngineSettingId { get; set; }
    public EngineSetting EngineSetting { get; set; } = null!;

    /// <summary>Giờ bắt đầu, bao gồm. 0–23.</summary>
    public int FromHourUtc { get; set; }

    /// <summary>Giờ kết thúc, KHÔNG bao gồm. 1–24.</summary>
    public int ToHourUtc { get; set; }

    /// <summary>Điểm chất lượng phiên, 0–6.</summary>
    public int Score { get; set; }

    public string Label { get; set; } = string.Empty;
}
