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

    // ── Phiên bản/setup/trigger ────────────────────────────────────────
    public TradingStrategyVersion StrategyVersion { get; set; } = TradingStrategyVersion.AdaptiveV2;
    public SetupType SetupType { get; set; } = SetupType.None;
    public SetupTriggerState TriggerState { get; set; } = SetupTriggerState.NotEvaluated;
    public string? TriggerDetail { get; set; }
    public SetupFunnelStage SetupStage { get; set; } = SetupFunnelStage.NotEligible;
    public string? SetupEventId { get; set; }
    public int SetupQualityScore { get; set; }

    // ── Điểm ────────────────────────────────────────────────────────────
    public int TechnicalScore { get; set; }
    public int MarketScore { get; set; }
    public int LiquidityScore { get; set; }

    /// <summary>Số âm hoặc 0. Nhóm kỷ luật chỉ trừ, không bao giờ cộng.</summary>
    public int DisciplinePenalty { get; set; }

    /// <summary>0–100.</summary>
    public int TotalScore { get; set; }

    // ── Chọn chiều (V2 §4) ──────────────────────────────────────────────

    /// <summary>
    /// Phần điểm đến từ các tiêu chí ĐỔI THEO CHIỀU — con số đã dùng để so hai chiều.
    /// </summary>
    /// <remarks>
    /// Ghi riêng chứ không suy lại từ các dòng phiếu: cờ <c>IsDirectional</c> nằm trong mã và có
    /// thể đổi, còn phiếu là bản ghi kiểm toán — nó phải nói con số nào ĐÃ ĐƯỢC dùng hôm đó.
    /// </remarks>
    public int DirectionalScore { get; set; }

    /// <summary>Tổng điểm của chiều NGƯỢC LẠI. Null khi chiều kia không được chấm hoặc bị veto.</summary>
    public int? OppositeScore { get; set; }

    /// <summary>Điểm đổi-theo-chiều của chiều ngược lại. Cùng quy ước null như trên.</summary>
    public int? OppositeDirectionalScore { get; set; }

    /// <summary>
    /// Vị trí giá trong biên độ khung thiên hướng, <c>0</c> = đáy, <c>100</c> = đỉnh.
    /// </summary>
    /// <remarks>
    /// Chỉ có giá trị trên ngày đi ngang. Ngoài 0–100 nghĩa là giá đã ra ngoài biên độ. Ghi lại
    /// để trả lời được câu "vì sao hôm đó không lệnh nào vào" mà không phải dựng lại chuỗi nến.
    /// </remarks>
    [Precision(9, 4)] public decimal? RangePositionPercent { get; set; }

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

    /// <summary>
    /// Tỉ lệ điểm ĐO ĐƯỢC trên tổng thang điểm, luôn ≤ 1.0.
    /// </summary>
    /// <remarks>
    /// Ngưỡng vào lệnh được chuẩn hoá theo phần điểm đo được, nên hình phạt thiếu dữ liệu của
    /// FR-006 phải quay lại ở kích thước thay vì ở ngưỡng. Ghi thành cột riêng chứ không gộp vào
    /// hệ số ngày: khi xem lại một phiếu cũ, "vào nhỏ vì hôm đó là ngày xấu" và "vào nhỏ vì hôm
    /// đó mất nguồn dữ liệu" là hai câu chuyện khác nhau.
    /// </remarks>
    [Precision(9, 4)] public decimal DataMultiplier { get; set; } = 1m;

    /// <summary>Hệ số riêng của playbook và quality V6; V2–V5 luôn bằng 1.</summary>
    [Precision(9, 4)] public decimal SetupSizeMultiplier { get; set; } = 1m;

    /// <summary>Số điểm thực sự đo được lần chấm này, trên thang <see cref="TotalMaxPoints"/>.</summary>
    public int AvailableMaxPoints { get; set; }

    /// <summary>Tổng thang điểm của bộ tiêu chí cộng điểm (85 với bộ hiện tại).</summary>
    public int TotalMaxPoints { get; set; }

    /// <summary>Tích của năm giá trị trên. Bất biến: <c>FinalSizeR ≤ BaseSizeR</c>.</summary>
    [Precision(9, 4)] public decimal FinalSizeR { get; set; }

    // ── Mức giá đề xuất ─────────────────────────────────────────────────
    [Precision(18, 8)] public decimal? SuggestedEntry { get; set; }
    [Precision(18, 8)] public decimal? SuggestedStopLoss { get; set; }
    [Precision(18, 8)] public decimal? SuggestedTakeProfit { get; set; }
    [Precision(18, 8)] public decimal? SuggestedFirstTakeProfit { get; set; }
    [Precision(18, 8)] public decimal? SuggestedRunnerTakeProfit { get; set; }
    [Precision(18, 8)] public decimal? SuggestedLimitEntry { get; set; }
    [Precision(9, 4)] public decimal? RiskReward { get; set; }

    /// <summary>Expected execution cost theo R của đúng plan entry/stop/target.</summary>
    [Precision(9, 4)] public decimal? ExpectedCostR { get; set; }

    /// <summary>R:R sau expected cost, dùng làm gate ở V3.</summary>
    [Precision(9, 4)] public decimal? NetRiskReward { get; set; }

    /// <summary>Khoảng entry đầu tới stop theo điểm cơ bản của giá entry.</summary>
    [Precision(9, 4)] public decimal? StopDistanceBps { get; set; }
    public DayRegime? EffectiveDayRegime { get; set; }
    public bool IsIntradayRegimeOverride { get; set; }
    public string? IntradayRegimeReason { get; set; }

    // ── Truy vết ────────────────────────────────────────────────────────
    public long? TradeId { get; set; }

    /// <summary>Toàn bộ đầu vào, đủ để tái lập lại phép tính về sau.</summary>
    public string InputSnapshotJson { get; set; } = string.Empty;

    /// <summary>Tách bản ghi kiểm thử lịch sử khỏi bản ghi chạy thật.</summary>
    public bool IsBacktest { get; set; }
    public long? BacktestRunId { get; set; }

    public ICollection<EntryScorecardLine> Lines { get; set; } = new List<EntryScorecardLine>();
}
