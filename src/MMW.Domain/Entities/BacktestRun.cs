using Microsoft.EntityFrameworkCore;

namespace MMW.Domain.Entities;

/// <summary>Một lần chạy kiểm thử lịch sử và kết quả tổng kết của nó.</summary>
public class BacktestRun : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public DateTime FromUtc { get; set; }
    public DateTime ToUtc { get; set; }

    /// <summary>Phân tách bằng dấu phẩy.</summary>
    public string Symbols { get; set; } = string.Empty;

    /// <summary>
    /// Chụp lại toàn bộ cấu hình engine tại thời điểm chạy. Không có nó thì một kết quả cũ
    /// không diễn giải được sau khi tham số đã đổi — và "so sánh hai lần chạy khác tham số
    /// mà tưởng cùng tham số" là cách tự lừa mình phổ biến nhất khi tối ưu chiến lược.
    /// </summary>
    public string EngineSettingSnapshotJson { get; set; } = string.Empty;

    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary><c>Running</c> / <c>Completed</c> / <c>Failed</c>.</summary>
    public string Status { get; set; } = "Running";

    // ── Chỉ số tổng kết (FR-055) ────────────────────────────────────────
    public int TradeCount { get; set; }
    [Precision(9, 4)] public decimal WinRate { get; set; }
    [Precision(9, 4)] public decimal ExpectancyR { get; set; }
    [Precision(9, 4)] public decimal MaxDrawdownPercent { get; set; }
    public int LongestLossStreak { get; set; }
    [Precision(18, 8)] public decimal TotalFees { get; set; }
    [Precision(18, 8)] public decimal TotalSlippage { get; set; }

    public string BreakdownByHourJson { get; set; } = string.Empty;
    public string BreakdownByRegimeJson { get; set; } = string.Empty;

    /// <summary>
    /// BẮT BUỘC điền. Một báo cáo kiểm thử không nêu hạn chế của chính nó sẽ được đọc như
    /// một lời hứa — và đó chính là cách người ta thuyết phục bản thân bật giao dịch thật quá sớm.
    /// </summary>
    public string Limitations { get; set; } = string.Empty;
}
