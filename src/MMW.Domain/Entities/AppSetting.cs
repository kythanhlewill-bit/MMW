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

    /// <summary>Tự tạo lệnh journal từ đề xuất đã qua AI preflight.</summary>
    public bool AutoCreateTradeFromSignal { get; set; }

    /// <summary>Điểm tối thiểu để sinh đề xuất lệnh (độ mạnh tín hiệu).</summary>
    public int MinSignalScore { get; set; } = 2;

    /// <summary>Cho phép đặt lệnh thật DÙ vi phạm rule Critical (bỏ qua chặn rủi ro). Nguy hiểm.</summary>
    public bool AllowOverrideRisk { get; set; }

    /// <summary>
    /// Công tắc chuyển từ đường sinh tín hiệu bằng AI sang engine tất định.
    /// Mặc định TẮT: cho phép triển khai từng phần mà không đổi hành vi hệ thống đang chạy —
    /// cùng tinh thần với Nguyên tắc III.
    /// </summary>
    public bool DeterministicEngineEnabled { get; set; }

    /// <summary>
    /// KHÔNG CÒN TÁC DỤNG từ 2026-08-13 — đường AI đề xuất lệnh đã gỡ, không mã nào đọc cờ này.
    /// </summary>
    /// <remarks>
    /// Giữ lại cột thay vì bỏ hẳn vì bỏ cần một migration, mà cột <c>bit</c> thừa thì vô hại.
    /// Ô bật/tắt tương ứng đã gỡ khỏi trang Cấu hình để không ai tưởng nó còn điều khiển gì.
    /// Dọn hẳn thì gộp vào lần migration kế tiếp.
    /// </remarks>
    public bool ShadowComparisonEnabled { get; set; } = true;
}
