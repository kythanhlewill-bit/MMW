using MMW.Domain.Enums;

namespace MMW.Web.Models;

/// <summary>Một bản ghi kết cục đã ghép với phiếu gốc — đủ để thống kê, không kéo cả cây quan hệ.</summary>
public sealed record OutcomeRow(
    long ReviewId,
    long ScorecardId,
    string Symbol,
    DateTime EvaluatedAtUtc,
    TradeDirection? Direction,
    VetoReason? Veto,
    ScorecardOutcome CardOutcome,
    int TotalScore,
    ScorecardReviewOutcome Outcome,
    int BarsToExit,
    decimal GrossR,
    decimal NetR,
    decimal StopDistancePercent,
    decimal MaxFavorableExcursionR,
    decimal MaxAdverseExcursionR)
{
    /// <summary>Phí + trượt giá + phí vốn, quy về R. Luôn ≥ 0 — chi phí chỉ đi một chiều.</summary>
    public decimal CostR => GrossR - NetR;
}

/// <summary>
/// Một dòng thống kê. Dùng chung cho mọi cách nhóm (theo cổng, theo dải stop, theo mã) để hai
/// bảng cạnh nhau không bao giờ tính "tỉ lệ thắng" theo hai công thức khác nhau.
/// </summary>
public sealed class OutcomeStat
{
    public string Label { get; init; } = "";
    public int Count { get; init; }
    public int TargetCount { get; init; }
    public int StopCount { get; init; }
    public int OpenCount { get; init; }
    public decimal AvgGrossR { get; init; }
    public decimal AvgNetR { get; init; }
    public decimal AvgCostR { get; init; }
    public decimal TotalNetR { get; init; }
    public decimal AvgStopPercent { get; init; }

    /// <summary>Tỉ lệ chạm mục tiêu trên tổng số phiếu đo được (kể cả phiếu hết giờ).</summary>
    public decimal TargetRate => Count == 0 ? 0m : (decimal)TargetCount / Count;

    public static OutcomeStat From(string label, IReadOnlyCollection<OutcomeRow> rows) => new()
    {
        Label = label,
        Count = rows.Count,
        TargetCount = rows.Count(r => r.Outcome == ScorecardReviewOutcome.Target),
        StopCount = rows.Count(r => r.Outcome == ScorecardReviewOutcome.Stop),
        OpenCount = rows.Count(r => r.Outcome is ScorecardReviewOutcome.OpenAtHorizon
                                             or ScorecardReviewOutcome.TimeStop),
        AvgGrossR = Avg(rows.Select(r => r.GrossR)),
        AvgNetR = Avg(rows.Select(r => r.NetR)),
        AvgCostR = Avg(rows.Select(r => r.CostR)),
        TotalNetR = rows.Sum(r => r.NetR),
        AvgStopPercent = Avg(rows.Select(r => r.StopDistancePercent)),
    };

    private static decimal Avg(IEnumerable<decimal> values)
    {
        var list = values as IReadOnlyList<decimal> ?? values.ToList();
        return list.Count == 0 ? 0m : list.Sum() / list.Count;
    }
}

/// <summary>Dữ liệu cho màn hình "Kết cục phiếu".</summary>
public sealed class ScorecardOutcomeViewModel
{
    // ── Bộ lọc ──────────────────────────────────────────────────────────

    /// <summary>Ngày VIỆT NAM người dùng chọn, không phải ngày UTC. Xem <see cref="FromUtc"/>.</summary>
    public DateOnly FromDateVn { get; set; }

    /// <inheritdoc cref="FromDateVn"/>
    public DateOnly ToDateVn { get; set; }

    /// <summary>Biên UTC thực sự dùng để truy vấn, đã dịch từ ngày VN.</summary>
    /// <remarks>
    /// Giữ lại để hiện ra màn hình: ngày giao dịch của bộ máy vẫn là ngày UTC (hạn mức lỗ, quota
    /// lệnh, bảng chất lượng phiên đều reset lúc nửa đêm UTC = 07:00 giờ VN). Lọc theo ngày VN là
    /// lựa chọn của người ĐỌC, và người đọc cần thấy hai mốc đó không trùng nhau.
    /// </remarks>
    public DateTime FromUtc { get; set; }

    /// <inheritdoc cref="FromUtc"/>
    public DateTime ToUtc { get; set; }

    public string? Symbol { get; set; }
    public VetoReason? Veto { get; set; }

    /// <summary>Chỉ giữ phiếu có điểm LỚN HƠN giá trị này. Cùng ngữ nghĩa với trang Phiếu chấm điểm.</summary>
    /// <remarks>
    /// Trên trang này bộ lọc còn một tác dụng riêng: phiếu bị veto được ghi <c>TotalScore = 0</c>
    /// theo hợp đồng của bộ chấm, nên đặt n ≥ 0 đồng thời loại chúng khỏi phép thống kê. Đó là cách
    /// tách câu hỏi "cổng chặn đúng không" khỏi câu hỏi "setup đủ điểm thì kết cục ra sao".
    /// </remarks>
    public int? MinScore { get; set; }

    public int ResolverVersion { get; set; }

    public IReadOnlyList<string> KnownSymbols { get; set; } = Array.Empty<string>();
    public IReadOnlyList<VetoReason> KnownVetoes { get; set; } = Array.Empty<VetoReason>();

    // ── Tổng thể ────────────────────────────────────────────────────────
    public OutcomeStat Overall { get; set; } = new();

    /// <summary>Số phiếu đủ điều kiện nhưng CHƯA đủ nến để kết luận. Không phải lỗi, là hàng đợi.</summary>
    public int PendingCount { get; set; }

    public DateTime? LastResolvedAtUtc { get; set; }

    // ── Các cách nhóm ───────────────────────────────────────────────────
    public IReadOnlyList<OutcomeStat> ByVeto { get; set; } = Array.Empty<OutcomeStat>();
    public IReadOnlyList<OutcomeStat> ByStopBucket { get; set; } = Array.Empty<OutcomeStat>();
    public IReadOnlyList<OutcomeStat> BySymbol { get; set; } = Array.Empty<OutcomeStat>();

    public IReadOnlyList<OutcomeRow> Recent { get; set; } = Array.Empty<OutcomeRow>();

    // ── Kinh tế ─────────────────────────────────────────────────────────
    /// <summary>R trung bình của các phiếu LÃI. Cùng với <see cref="AvgLossR"/> ra ngưỡng hoà vốn.</summary>
    public decimal AvgWinR { get; set; }

    /// <summary>Trị tuyệt đối R trung bình của các phiếu LỖ.</summary>
    public decimal AvgLossR { get; set; }

    /// <summary>
    /// Tỉ lệ thắng tối thiểu để hoà vốn với cấu trúc lãi/lỗ hiện tại.
    /// </summary>
    /// <remarks>
    /// Đây là con số duy nhất khiến "tỉ lệ thắng cao" trở nên đọc được. Một tập lệnh thắng 48%
    /// nghe như gần hoà, nhưng nếu lệnh thua mất 1,8R còn lệnh thắng chỉ được 1,2R thì ngưỡng hoà
    /// vốn là 60% — và 48% là lỗ đều đặn, không phải "gần được".
    /// </remarks>
    public decimal BreakevenWinRate =>
        AvgWinR + AvgLossR == 0m ? 0m : AvgLossR / (AvgWinR + AvgLossR);

    /// <summary>Lệch múi giờ Việt Nam. Cố định — Việt Nam không có giờ mùa hè.</summary>
    public const int VnOffsetHours = 7;

    public static string Vn(DateTime utc) => utc.AddHours(VnOffsetHours).ToString("HH:mm dd/MM");

    /// <summary>Nửa đêm ngày VN, quy về mốc UTC tương ứng.</summary>
    public static DateTime VnDayStartUtc(DateOnly dateVn) => DateTime.SpecifyKind(
        dateVn.ToDateTime(TimeOnly.MinValue).AddHours(-VnOffsetHours), DateTimeKind.Utc);

    /// <summary>Cuối ngày VN (bao gồm cả nó), quy về mốc UTC tương ứng.</summary>
    public static DateTime VnDayEndUtc(DateOnly dateVn) => DateTime.SpecifyKind(
        dateVn.ToDateTime(TimeOnly.MaxValue).AddHours(-VnOffsetHours), DateTimeKind.Utc);

    public static DateOnly TodayVn(DateTime utcNow) =>
        DateOnly.FromDateTime(utcNow.AddHours(VnOffsetHours));

    public static string OutcomeLabel(ScorecardReviewOutcome outcome) => outcome switch
    {
        ScorecardReviewOutcome.Target => "Chạm mục tiêu",
        ScorecardReviewOutcome.Stop => "Chạm dừng lỗ",
        ScorecardReviewOutcome.TimeStop => "Dừng theo thời gian",
        _ => "Còn mở hết cửa sổ",
    };

    public static string OutcomeBadge(ScorecardReviewOutcome outcome) => outcome switch
    {
        ScorecardReviewOutcome.Target => "bg-green",
        ScorecardReviewOutcome.Stop => "bg-red",
        _ => "bg-secondary",
    };

    /// <summary>
    /// Các dải khoảng dừng lỗ, theo đúng thứ tự hiển thị. Ranh giới đặt quanh vùng phí bắt đầu
    /// nuốt hết biên lợi — dưới 0,25% là vùng chi phí vượt nửa R.
    /// </summary>
    public static readonly string[] StopBuckets =
    {
        "dưới 0,15%", "0,15–0,25%", "0,25–0,50%", "0,50–1,00%", "từ 1,00%",
    };

    /// <summary>Thứ tự hiển thị của một dải. Sắp theo nhãn sẽ ra thứ tự bảng chữ cái, không phải thứ tự số.</summary>
    public static int StopBucketOrder(string label) => Array.IndexOf(StopBuckets, label);

    public static string StopBucket(decimal stopPercent) => stopPercent switch
    {
        < 0.15m => StopBuckets[0],
        < 0.25m => StopBuckets[1],
        < 0.50m => StopBuckets[2],
        < 1.00m => StopBuckets[3],
        _ => StopBuckets[4],
    };
}
