using Microsoft.EntityFrameworkCore;
using MMW.Domain.Enums;

namespace MMW.Domain.Entities;

/// <summary>
/// Kế hoạch giao dịch của một ngày. Một bản duy nhất cho mỗi (TradingAccountId, PlanDateUtc).
/// </summary>
/// <remarks>
/// Sinh lúc 23:30 UTC cho ngày kế tiếp. BẤT BIẾN sau khi sinh: job chạy lại trong cùng ngày
/// KHÔNG được ghi đè bản đã có. Kế hoạch đổi giữa ngày thì mọi phiếu chấm điểm trước đó
/// mất ngữ cảnh, và bản ghi kiểm toán trở thành vô nghĩa.
/// </remarks>
public class DailyPlan : BaseEntity
{
    public long TradingAccountId { get; set; }
    public TradingAccount TradingAccount { get; set; } = null!;

    /// <summary>Ngày giao dịch, neo tại mốc 00:00 UTC (FR-024).</summary>
    public DateOnly PlanDateUtc { get; set; }

    public DateTime GeneratedAtUtc { get; set; }

    // ── Kết quả phân loại (FR-018) ──────────────────────────────────────
    public DayRegime DayRegime { get; set; }
    public VolatilityRegime VolatilityRegime { get; set; }
    public AllowedDirections AllowedDirections { get; set; }

    [Precision(9, 4)] public decimal RiskMultiplier { get; set; }
    public int MaxTradesToday { get; set; }

    // ── Mức giá tham chiếu ──────────────────────────────────────────────
    [Precision(18, 8)] public decimal? PreviousDayHigh { get; set; }
    [Precision(18, 8)] public decimal? PreviousDayLow { get; set; }
    [Precision(18, 8)] public decimal? WeeklyOpen { get; set; }
    [Precision(18, 8)] public decimal? DailyOpen { get; set; }

    // ── Đầu vào đã dùng, lưu để truy vết (FR-017) ───────────────────────
    public string? BtcStructure { get; set; }
    [Precision(9, 4)] public decimal? AtrPercentile { get; set; }
    [Precision(18, 8)] public decimal? FundingRate { get; set; }
    [Precision(9, 4)] public decimal? OpenInterestChange24hPercent { get; set; }
    [Precision(9, 4)] public decimal? LongShortAccountRatio { get; set; }
    public int? FearGreedIndex { get; set; }

    // ── Chất lượng dữ liệu (FR-022) ─────────────────────────────────────
    /// <summary>Các thành phần không lấy được, phân tách bằng dấu phẩy.</summary>
    public string? MissingInputs { get; set; }

    /// <summary>
    /// False khi có <see cref="MissingInputs"/>. Bất biến bắt buộc: khi thiếu dữ liệu,
    /// <see cref="RiskMultiplier"/> KHÔNG được cao hơn giá trị lẽ ra có nếu đủ dữ liệu —
    /// thiếu thông tin phải làm hệ thống thận trọng hơn, không phải mạnh dạn hơn.
    /// </summary>
    public bool IsComplete { get; set; }

    // ── Bối cảnh AI (FR-040) — chỉ mô tả, không quyết định ──────────────
    public string? AiDayRiskLevel { get; set; }

    /// <summary>Tiếng Việt (FR-047).</summary>
    public string? AiNarrative { get; set; }

    /// <summary>Trần 0.8 — cắt ở phía nhận, không tin con số AI tự khai.</summary>
    [Precision(9, 4)] public decimal? AiConfidence { get; set; }

    public bool AiAnswered { get; set; }

    public ICollection<EntryScorecard> Scorecards { get; set; } = new List<EntryScorecard>();
}
