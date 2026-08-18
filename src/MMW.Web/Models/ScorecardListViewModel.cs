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
}
