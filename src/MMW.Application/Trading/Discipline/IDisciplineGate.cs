using MMW.Application.Trading.Scoring;
using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Application.Trading.Discipline;

/// <summary>Việc một gate kỷ luật yêu cầu làm.</summary>
public enum GateAction
{
    Allow = 0,

    /// <summary>Cho vào lệnh nhưng nhỏ hơn.</summary>
    ReduceSize = 1,

    /// <summary>Chặn lệnh này. Lệnh sau vẫn có thể qua.</summary>
    BlockTrade = 2,

    /// <summary>Dừng giao dịch đến hết ngày UTC.</summary>
    StopForDay = 3,
}

/// <param name="SizeMultiplier">Chỉ dùng khi <see cref="GateAction.ReduceSize"/>. PHẢI ≤ 1.0.</param>
/// <param name="ScorePenalty">≤ 0. Nhóm kỷ luật chỉ trừ, không bao giờ cộng.</param>
/// <param name="Reason">Tiếng Việt, PHẢI nêu số liệu thực tế so với ngưỡng.</param>
public sealed record GateResult(
    GateAction Action,
    decimal SizeMultiplier,
    int ScorePenalty,
    string Reason,
    VetoReason? VetoReason)
{
    public static GateResult Pass(string reason) => new(GateAction.Allow, 1.0m, 0, reason, null);

    public static GateResult Reduce(decimal multiplier, string reason) =>
        new(GateAction.ReduceSize, Math.Clamp(multiplier, 0m, 1m), 0, reason, null);

    public static GateResult ReduceAndPenalise(decimal multiplier, int penalty, string reason) =>
        new(GateAction.ReduceSize, Math.Clamp(multiplier, 0m, 1m), Math.Min(0, penalty), reason, null);

    public static GateResult Block(VetoReason reason, string detail) =>
        new(GateAction.BlockTrade, 1.0m, 0, detail, reason);

    public static GateResult StopDay(VetoReason reason, string detail) =>
        new(GateAction.StopForDay, 0m, 0, detail, reason);

    public static GateResult Penalise(int penalty, string reason) =>
        new(GateAction.Allow, 1.0m, Math.Min(0, penalty), reason, null);
}

/// <summary>
/// Đầu vào BẤT BIẾN của một lượt chạy gate kỷ luật.
/// </summary>
/// <remarks>
/// Giống <see cref="ScoringContext"/>: mọi số liệu đã tính sẵn ở ngoài, để gate thuần và
/// kiểm thử lịch sử chạy được hàng chục nghìn lượt trong bộ nhớ.
/// </remarks>
public sealed record DisciplineContext
{
    public required long TradingAccountId { get; init; }
    public required DateTime EvaluatedAtUtc { get; init; }

    /// <summary>Mã của lệnh SẮP vào.</summary>
    public required string Symbol { get; init; }

    /// <summary>Chiều của lệnh SẮP vào.</summary>
    public required TradeDirection Direction { get; init; }

    /// <summary>Phần trăm rủi ro của lệnh SẮP vào, để so với trung bình lịch sử.</summary>
    public required decimal PlannedRiskPercent { get; init; }

    /// <summary>Kích thước theo R của lệnh SẮP vào, tính với gate và AI trung tính.</summary>
    /// <remarks>
    /// Cận TRÊN: các hệ số chạy sau chỉ có thể giảm tiếp. Dùng cận trên để cộng dồn rủi ro tương
    /// quan là lựa chọn thận trọng đúng hướng — thà chặn sớm hơn là để lọt một lệnh làm tổng rủi
    /// ro vượt trần rồi mới biết.
    /// </remarks>
    public decimal ProjectedSizeR { get; init; }

    /// <summary>Tương quan của mã này với mã dẫn dắt. Null khi không đo được hoặc chính là mã dẫn dắt.</summary>
    public decimal? LeaderCorrelation { get; init; }

    /// <summary>Mã này có phải mã dẫn dắt không.</summary>
    public bool IsLeaderSymbol { get; init; }

    public required DailyPlan DailyPlan { get; init; }
    public required EngineSetting Settings { get; init; }
    public required RiskSetting RiskSettings { get; init; }
    public required TraderStatistics Stats { get; init; }
}

/// <summary>
/// Hợp đồng plug-in của một rào chắn kỷ luật (Nguyên tắc V).
/// </summary>
/// <remarks>
/// Khác biệt cốt lõi so với <c>IBehaviorDetector</c> hiện có: detector CẢNH BÁO sau khi lệnh
/// đã vào, gate CHẶN trước khi nó vào. Ba bộ phát hiện cũ vẫn giữ nguyên vai trò của chúng —
/// nhật ký hành vi để review — còn ở đây là rào chắn.
/// </remarks>
public interface IDisciplineGate
{
    /// <summary>Định danh ổn định, ví dụ <c>discipline.loss_streak</c>.</summary>
    string Key { get; }

    GateResult Evaluate(DisciplineContext context);
}
