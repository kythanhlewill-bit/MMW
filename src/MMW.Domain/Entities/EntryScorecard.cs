using Microsoft.EntityFrameworkCore;
using MMW.Domain.Enums;

namespace MMW.Domain.Entities;

/// <summary>
/// Phiếu chấm điểm một cơ hội vào lệnh — bản ghi kiểm toán trung tâm của engine.
/// </summary>
/// <remarks>
/// Lưu MỌI lần đánh giá, kể cả khi kết luận là không vào lệnh (FR-039, SC-012).
/// Những phiếu "không vào" mới là phần có giá trị nhất: chúng trả lời được câu hỏi
/// "tại sao hôm nay hệ thống đứng ngoài", và đó là câu hỏi sẽ được hỏi nhiều nhất.
/// </remarks>
public class EntryScorecard : BaseEntity
{
    public long TradingAccountId { get; set; }

    /// <summary>Null khi bị từ chối vì chưa có kế hoạch ngày.</summary>
    public long? DailyPlanId { get; set; }
    public DailyPlan? DailyPlan { get; set; }

    public string Symbol { get; set; } = string.Empty;
    public string Interval { get; set; } = string.Empty;

    /// <summary>Khoá logic chống trùng, cùng với Symbol và IsBacktest (FR-051).</summary>
    public DateTime CandleCloseTimeUtc { get; set; }

    public DateTime EvaluatedAtUtc { get; set; }

    /// <summary>Null khi không xác định được hướng.</summary>
    public TradeDirection? Direction { get; set; }

    // ── Điểm ────────────────────────────────────────────────────────────
    public int TechnicalScore { get; set; }
    public int MarketScore { get; set; }
    public int LiquidityScore { get; set; }

    /// <summary>Số âm hoặc 0. Nhóm kỷ luật chỉ trừ, không bao giờ cộng.</summary>
    public int DisciplinePenalty { get; set; }

    /// <summary>0–100.</summary>
    public int TotalScore { get; set; }

    // ── Quyết định ──────────────────────────────────────────────────────
    public ScorecardOutcome Outcome { get; set; }
    public VetoReason? VetoReason { get; set; }
    public string? VetoDetail { get; set; }

    // ── Kích thước (FR-034) ─────────────────────────────────────────────
    [Precision(9, 4)] public decimal BaseSizeR { get; set; }
    [Precision(9, 4)] public decimal DayRiskMultiplier { get; set; }
    [Precision(9, 4)] public decimal DisciplineMultiplier { get; set; }

    /// <summary>Luôn ≤ 1.0 (FR-042). AI chỉ có một hướng tác động, và hướng đó là xuống.</summary>
    [Precision(9, 4)] public decimal AiMultiplier { get; set; }

    /// <summary>Tích của bốn giá trị trên. Bất biến: <c>FinalSizeR ≤ BaseSizeR</c>.</summary>
    [Precision(9, 4)] public decimal FinalSizeR { get; set; }

    // ── Mức giá đề xuất ─────────────────────────────────────────────────
    [Precision(18, 8)] public decimal? SuggestedEntry { get; set; }
    [Precision(18, 8)] public decimal? SuggestedStopLoss { get; set; }
    [Precision(18, 8)] public decimal? SuggestedTakeProfit { get; set; }
    [Precision(9, 4)] public decimal? RiskReward { get; set; }

    // ── Truy vết ────────────────────────────────────────────────────────
    public long? TradeId { get; set; }

    /// <summary>Toàn bộ đầu vào, đủ để tái lập lại phép tính về sau.</summary>
    public string InputSnapshotJson { get; set; } = string.Empty;

    /// <summary>Tách bản ghi kiểm thử lịch sử khỏi bản ghi chạy thật.</summary>
    public bool IsBacktest { get; set; }
    public long? BacktestRunId { get; set; }

    public ICollection<EntryScorecardLine> Lines { get; set; } = new List<EntryScorecardLine>();
}
