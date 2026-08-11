using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using MMW.Domain.Enums;

namespace MMW.Domain.Entities;

/// <summary>
/// Kết cục thực tế của một <see cref="EntryScorecard"/> khi cho chạy tiếp trên giá — kể cả phiếu
/// đã bị veto và không bao giờ thành lệnh.
/// </summary>
/// <remarks>
/// Tồn tại để chất vấn các CỔNG bằng số thay vì bằng cảm nhận. Phiếu bị chặn rồi giá đi ngược
/// nghĩa là cổng cứu được một lệnh lỗ; bị chặn mà giá chạm mục tiêu nghĩa là cổng chặn nhầm. Cả
/// hai đều là thông tin, và cả hai đều mất sạch nếu không ghi lại.
///
/// <b>Không tự tính lại luật khớp lệnh.</b> Bản ghi này sinh ra từ chính
/// <c>SimulatedTradePosition</c> mà kiểm thử lịch sử dùng, nên quy ước "cùng một nến chạm cả stop
/// lẫn mục tiêu thì tính stop", cách tính phí theo khối lượng và trượt giá đều là MỘT bộ luật duy
/// nhất. Chép tay bộ luật đó sang chỗ thứ hai là bảo đảm hai con số sẽ trôi khỏi nhau.
///
/// <see cref="ResolverVersion"/> là chốt chặn cho chuyện đó: đổi luật phân giải thì tăng số này,
/// bản ghi cũ vẫn nằm nguyên nhưng không bị trộn chung với bản ghi mới trong cùng một phép thống
/// kê. Không có nó thì một lần sửa luật sẽ âm thầm làm bẩn toàn bộ lịch sử đo.
/// </remarks>
public class ScorecardOutcomeReview : BaseEntity
{
    public long EntryScorecardId { get; set; }
    public EntryScorecard EntryScorecard { get; set; } = null!;

    public DateTime ResolvedAtUtc { get; set; }

    /// <summary>Phiên bản bộ luật phân giải. Xem ghi chú của lớp.</summary>
    public int ResolverVersion { get; set; }

    // ── Cửa sổ đo ───────────────────────────────────────────────────────
    [Required, MaxLength(10)]
    public string BarInterval { get; set; } = "15m";

    /// <summary>Số nến tối đa được chạy trước khi đóng cưỡng bức.</summary>
    public int HorizonBars { get; set; }

    /// <summary>Nến đầu tiên được tính — LUÔN mở sau thời điểm chấm điểm, không nhìn trộm.</summary>
    public DateTime FirstBarUtc { get; set; }

    // ── Kết cục ─────────────────────────────────────────────────────────
    public ScorecardReviewOutcome Outcome { get; set; }

    public DateTime? ExitAtUtc { get; set; }
    public int BarsToExit { get; set; }

    // ── Kinh tế, quy về R ───────────────────────────────────────────────
    /// <summary>Kết quả TRƯỚC mọi chi phí. Đọc một mình con số này là tự lừa.</summary>
    [Precision(9, 4)] public decimal GrossR { get; set; }

    [Precision(9, 4)] public decimal FeeR { get; set; }
    [Precision(9, 4)] public decimal SlippageR { get; set; }
    [Precision(9, 4)] public decimal FundingR { get; set; }

    /// <summary>Kết quả sau phí, trượt giá và phí vốn — con số duy nhất đáng kết luận.</summary>
    [Precision(9, 4)] public decimal NetR { get; set; }

    /// <summary>
    /// Khoảng entry→stop theo % giá. Cột này tồn tại vì nó GIẢI THÍCH cột chi phí.
    /// </summary>
    /// <remarks>
    /// Khối lượng bằng ngân sách rủi ro chia khoảng stop, mà phí thu trên khối lượng — nên stop
    /// càng hẹp, phí tính theo R càng lớn, theo tỉ lệ nghịch. Không có cột này thì một tập lệnh
    /// lỗ vì stop quá sát trông y hệt một tập lệnh lỗ vì tín hiệu kém.
    /// </remarks>
    [Precision(9, 4)] public decimal StopDistancePercent { get; set; }

    [Precision(9, 4)] public decimal MaxFavorableExcursionR { get; set; }
    [Precision(9, 4)] public decimal MaxAdverseExcursionR { get; set; }
}
