namespace MMW.Domain.Entities;

/// <summary>
/// Cấu hình toàn cục (1 bản ghi). Per-account dùng <see cref="RiskSetting"/>.
/// </summary>
public class AppSetting : BaseEntity
{
    /// <summary>Tài khoản mặc định khi tạo lệnh (vd từ đề xuất).</summary>
    public long? DefaultTradingAccountId { get; set; }

    /// <summary>Yêu cầu xác nhận trước khi tạo lệnh.</summary>
    public bool ConfirmBeforeCreateTrade { get; set; } = true;

    /// <summary>Điểm tối thiểu để sinh đề xuất lệnh (độ mạnh tín hiệu).</summary>
    public int MinSignalScore { get; set; } = 2;
}
