using Microsoft.EntityFrameworkCore;
using MMW.Domain.Enums;

namespace MMW.Domain.Entities;

/// <summary>Một lần chạy kiểm thử lịch sử và kết quả tổng kết của nó.</summary>
public class BacktestRun : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public TradingStrategyVersion StrategyVersion { get; set; } = TradingStrategyVersion.AdaptiveV2;
    public string TelemetrySchemaVersion { get; set; } = string.Empty;
    public string DecisionFingerprint { get; set; } = string.Empty;
    public string TradeFingerprint { get; set; } = string.Empty;
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
    [Precision(9, 4)] public decimal WinRateCiLow { get; set; }
    [Precision(9, 4)] public decimal WinRateCiHigh { get; set; }
    [Precision(9, 4)] public decimal ExpectancyR { get; set; }
    [Precision(9, 4)] public decimal ExpectancyRCiLow { get; set; }
    [Precision(9, 4)] public decimal ExpectancyRCiHigh { get; set; }
    [Precision(9, 4)] public decimal MaxDrawdownPercent { get; set; }
    public int LongestLossStreak { get; set; }
    [Precision(18, 8)] public decimal TotalFees { get; set; }
    [Precision(18, 8)] public decimal TotalSlippage { get; set; }

    // ── Chi phí quy ra R ────────────────────────────────────────────────
    //
    // Ba cột này được THÊM chứ không thay hai cột trên, dù hai cột trên có đơn vị gần như vô
    // nghĩa khi cộng ngang các mã. Đổi nghĩa một cột đã có dữ liệu sẽ làm mọi lần chạy cũ —
    // trong đó có baseline đang dùng để so sánh — im lặng nói dối.

    /// <summary>Tổng phí giao dịch quy ra R.</summary>
    [Precision(18, 8)] public decimal TotalFeeR { get; set; }

    /// <summary>Tổng phí vốn quy ra R. Dương = tiền ra.</summary>
    [Precision(18, 8)] public decimal TotalFundingR { get; set; }

    /// <summary>Tổng trượt giá quy ra R.</summary>
    [Precision(18, 8)] public decimal TotalSlippageR { get; set; }

    /// <summary>Expectancy trước commission, funding và slippage, cùng đơn vị R.</summary>
    [Precision(9, 4)] public decimal GrossExpectancyR { get; set; }

    public string BreakdownByHourJson { get; set; } = string.Empty;
    public string BreakdownByRegimeJson { get; set; } = string.Empty;
    public string BreakdownByModeJson { get; set; } = string.Empty;
    public string BreakdownByExitReasonJson { get; set; } = string.Empty;

    /// <summary>Phân phối R:R của mọi lượt dựng được cấu trúc trong lần chạy.</summary>
    public string StructuralRrDistributionJson { get; set; } = string.Empty;

    /// <summary>
    /// Một phần tử cho MỖI veto InsufficientRoom; null nghĩa là không dựng được stop hợp lệ.
    /// Giữ số đo thô để ngưỡng sau này được chọn từ dữ liệu, không chỉ từ bảng phân vị đã làm tròn.
    /// </summary>
    public string StructuralRrVetoObservationsJson { get; set; } = string.Empty;

    /// <summary>Aggregate telemetry P0; không lưu hàng trăm nghìn scorecard production.</summary>
    public string DiagnosticsJson { get; set; } = string.Empty;

    /// <summary>Số lần chạy hoàn tất trước đó trên đúng khoảng và đúng tập mã, cộng một.</summary>
    public int ComparableTrialNumber { get; set; } = 1;

    /// <summary>Số setup đủ điểm nhưng bị loại chỉ vì biên chọn chiều.</summary>
    public int DirectionMarginMaterialBlocks { get; set; }

    /// <summary>
    /// BẮT BUỘC điền. Một báo cáo kiểm thử không nêu hạn chế của chính nó sẽ được đọc như
    /// một lời hứa — và đó chính là cách người ta thuyết phục bản thân bật giao dịch thật quá sớm.
    /// </summary>
    public string Limitations { get; set; } = string.Empty;
}
