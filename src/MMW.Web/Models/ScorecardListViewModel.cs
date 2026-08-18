using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Web.Models;

/// <summary>Dữ liệu cho màn hình phiếu chấm điểm.</summary>
public class ScorecardListViewModel
{
    public string? Symbol { get; set; }
    public VetoReason? Veto { get; set; }
    public ScorecardOutcome? Outcome { get; set; }

    /// <summary>Chỉ lấy phiếu có tổng điểm LỚN HƠN giá trị này. Null nghĩa là không lọc.</summary>
    public int? MinScore { get; set; }

    public IReadOnlyList<EntryScorecard> Items { get; set; } = Array.Empty<EntryScorecard>();

    /// <summary>Đếm theo lý do từ chối, xếp giảm dần.</summary>
    public IReadOnlyDictionary<VetoReason, int> VetoCounts { get; set; } = new Dictionary<VetoReason, int>();

    /// <summary>Năm tiêu chí hay về 0 điểm nhất — dữ liệu để cải tiến thuật toán.</summary>
    public IReadOnlyDictionary<string, int> ZeroPointCriteria { get; set; } = new Dictionary<string, int>();

    public static string Vn(DateTime utc) => utc.AddHours(7).ToString("HH:mm dd/MM");

    public static string VetoLabel(VetoReason reason) => reason switch
    {
        VetoReason.NoDailyPlan => "Chưa có kế hoạch ngày",
        VetoReason.DirectionNotAllowed => "Chiều không được phép",
        VetoReason.HtfMisaligned => "Khung lớn ngược chiều",
        VetoReason.InBlackoutWindow => "Trong cửa sổ chặn",
        VetoReason.LossStreakStop => "Chuỗi thua liên tiếp",
        VetoReason.DailyLossStop => "Chạm giới hạn lỗ ngày",
        VetoReason.RevengeWindow => "Cửa sổ trả thù",
        VetoReason.Oversized => "Kích thước quá lớn",
        VetoReason.MaxTradesReached => "Hết hạn mức lệnh ngày",
        VetoReason.InsufficientData => "Thiếu dữ liệu",
        VetoReason.DuplicateCandle => "Trùng cây nến",
        VetoReason.PositionAlreadyOpen => "Đang có vị thế trên mã này",
        VetoReason.ConcurrentPositionLimit => "Hết hạn mức vị thế mở",
        VetoReason.InsufficientRoom => "Không đủ chỗ chạy tới mục tiêu",
        VetoReason.NotAtRangeEdge => "Không ở biên của ngày đi ngang",
        VetoReason.DirectionUnclear => "Hai chiều chấm ngang nhau",
        _ => reason.ToString(),
    };

    public static string OutcomeLabel(ScorecardOutcome outcome) => outcome switch
    {
        ScorecardOutcome.Entered => "Đã vào lệnh",
        ScorecardOutcome.BelowThreshold => "Dưới ngưỡng",
        ScorecardOutcome.SetupMissing => "Đủ điểm, thiếu setup",
        _ => "Bị từ chối",
    };

    public static string TriggerStateLabel(SetupTriggerState state) => state switch
    {
        SetupTriggerState.NotEvaluated => "Chưa xét",
        SetupTriggerState.LegacyAccepted => "Nhận theo luật cũ",
        SetupTriggerState.NoBreakOfStructure => "Chưa phá cấu trúc",
        SetupTriggerState.BreakUnretested => "Đã phá nhưng chưa retest",
        SetupTriggerState.RetestFailed => "Retest thủng mức",
        SetupTriggerState.RetestStale => "Retest đã cũ",
        SetupTriggerState.ImpulseWeak => "Cú đẩy yếu",
        SetupTriggerState.PullbackVolumeExpanded => "Nhịp hồi có khối lượng nở",
        SetupTriggerState.ReclaimWeak => "Nến giành lại yếu",
        SetupTriggerState.RangeNotSwept => "Chưa quét biên",
        SetupTriggerState.RangeRejectionWeak => "Từ chối biên yếu",
        SetupTriggerState.Confirmed => "Đã xác nhận",
        SetupTriggerState.CostRejected => "Chi phí quá cao",
        SetupTriggerState.RangeGeometryWeak => "Hình dạng vùng yếu",
        SetupTriggerState.RangeConfirmationMissing => "Thiếu xác nhận vùng",
        SetupTriggerState.CompressionMissing => "Chưa nén giá",
        SetupTriggerState.BreakoutMissing => "Chưa phá biên",
        SetupTriggerState.BreakoutWeak => "Phá biên yếu",
        SetupTriggerState.BreakoutRetestMissing => "Thiếu retest sau phá biên",
        SetupTriggerState.StrategyFiltered => "Bị lọc theo phiên bản",
        SetupTriggerState.MaTrendMissing => "MA chưa thuận chiều",
        SetupTriggerState.MaImpulseWeak => "Cú đẩy MA yếu",
        SetupTriggerState.MaPullbackMissing => "Chưa hồi về MA",
        SetupTriggerState.MaPullbackStale => "Nhịp hồi MA đã cũ",
        SetupTriggerState.MaRejectionMissing => "Chưa có cú từ chối",
        SetupTriggerState.MaDeepZoneMissing => "Chưa về vùng MA99",
        _ => state.ToString(),
    };

    public static string SetupLabel(SetupType setup) => setup switch
    {
        SetupType.None => "—",
        SetupType.TrendPullback => "Hồi trong xu hướng",
        SetupType.LegacyV2 => "Luật cũ V2",
        SetupType.RangeRejection => "Từ chối tại biên",
        SetupType.StrongTrendBreakout => "Phá biên xu hướng mạnh",
        SetupType.RectangleRangeFade => "Đảo tại biên hộp",
        SetupType.RectangleBreakout => "Phá hộp",
        SetupType.TriangleBreakout => "Phá tam giác",
        SetupType.MaPullback => "Hồi về MA7",
        SetupType.MaCrossFast => "Cắt MA khối lượng lớn",
        SetupType.MaDeepPullback => "Hồi sâu về MA99",
        _ => setup.ToString(),
    };

    /// <summary>
    /// Một câu trả lời cho "vì sao phiếu này không thành lệnh", chọn theo kết cục.
    /// </summary>
    /// <remarks>
    /// Ba kết cục hỏng có ba nguồn sự thật khác nhau, và lấy nhầm nguồn thì câu trả lời sẽ đúng
    /// ngữ pháp mà sai nội dung. Phiếu bị veto mang lý do ở <c>VetoReason</c>; phiếu thiếu setup
    /// mang lý do ở <c>TriggerState</c> — <c>VetoDetail</c> lúc đó chỉ chép lại phép nhân sizing
    /// và không nói được vì sao setup vắng; phiếu dưới ngưỡng thì chính phép nhân đó là lý do.
    /// </remarks>
    public static string BlockReason(EntryScorecard card) => card.Outcome switch
    {
        ScorecardOutcome.Entered => "",

        ScorecardOutcome.Vetoed => card.VetoReason is { } reason
            ? VetoLabel(reason)
            : "Bị từ chối",

        ScorecardOutcome.SetupMissing =>
            $"{TriggerStateLabel(card.TriggerState)}"
            + (string.IsNullOrWhiteSpace(card.TriggerDetail) ? "" : $" — {card.TriggerDetail}"),

        _ => string.IsNullOrWhiteSpace(card.VetoDetail)
            ? $"Điểm {card.TotalScore}/{card.AvailableMaxPoints} chưa đủ ngưỡng vào lệnh"
            : card.VetoDetail!,
    };
}
