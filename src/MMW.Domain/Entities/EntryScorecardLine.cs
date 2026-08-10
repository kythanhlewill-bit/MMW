using MMW.Domain.Enums;

namespace MMW.Domain.Entities;

/// <summary>
/// Điểm của một tiêu chí trong phiếu chấm điểm. Một dòng cho mỗi tiêu chí và mỗi gate kỷ luật.
/// </summary>
/// <remarks>
/// Tách bảng thay vì nhét JSON vào phiếu vì cần truy vấn được: "3 tháng qua tiêu chí nào
/// hay về 0 điểm nhất". Đó chính là dữ liệu để cải tiến thuật toán, và là lý do tồn tại
/// của Nguyên tắc IV.
/// </remarks>
public class EntryScorecardLine : BaseEntity
{
    public long EntryScorecardId { get; set; }
    public EntryScorecard EntryScorecard { get; set; } = null!;

    /// <summary>
    /// Định danh ổn định, ví dụ <c>technical.htf_alignment</c>.
    /// KHÔNG được đổi sau khi đã có dữ liệu lịch sử — đổi khoá là mất khả năng so sánh theo thời gian.
    /// </summary>
    public string CriterionKey { get; set; } = string.Empty;

    public ScoreGroup Group { get; set; }
    public int MaxPoints { get; set; }

    /// <summary>Có thể âm với nhóm kỷ luật.</summary>
    public int AwardedPoints { get; set; }

    public bool IsHardVeto { get; set; }

    /// <summary>
    /// Tiếng Việt, PHẢI nêu số liệu thực tế so với ngưỡng (Nguyên tắc I).
    /// "Không đạt" là lý do vô dụng; "RSI 38.2, ngoài dải 45–65" thì dùng được.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>False ⟹ <see cref="AwardedPoints"/> = 0 theo FR-006.</summary>
    public bool DataAvailable { get; set; } = true;

    /// <summary>True cho tiêu chí vùng thanh khoản, vốn là xấp xỉ từ nến (R-010).</summary>
    public bool IsApproximation { get; set; }

    /// <summary>Mã trạng thái máy đọc được, ví dụ NoBos/RetestConfirmed; null với tiêu chí không cần.</summary>
    public string? StateCode { get; set; }
}
