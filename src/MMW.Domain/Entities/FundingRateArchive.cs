using Microsoft.EntityFrameworkCore;

namespace MMW.Domain.Entities;

/// <summary>Kho lịch sử phí vốn, nạp từ <c>/fapi/v1/fundingRate</c>.</summary>
/// <remarks>
/// Thực thể này tồn tại nhờ một phát hiện ở T001: khác với nhóm <c>/futures/data/*</c> chỉ giữ
/// 30 ngày, endpoint lịch sử phí vốn có đủ ít nhất 2 năm. Nhờ vậy tiêu chí
/// <c>market.funding_crowding</c> (4 điểm) kiểm thử lịch sử được thay vì mất trắng.
///
/// ⚠️ Khác biệt về độ trung thực phải nhớ: chạy thật dùng <c>lastFundingRate</c> — tỷ lệ
/// DỰ PHÓNG cho kỳ thanh toán sắp tới. Kho này lưu tỷ lệ ĐÃ THANH TOÁN. Dùng tỷ lệ đã thanh
/// toán làm giá trị hiệu lực cho 8 giờ trước đó là một xấp xỉ, vẫn hơn hẳn chấm 0 điểm,
/// nhưng phải ghi vào <c>BacktestRun.Limitations</c> và phải nằm trong danh sách loại trừ
/// tường minh của test tương đương.
///
/// Ước lượng: 2 symbol × 3 mốc/ngày × 2 năm ≈ 4.4k dòng.
/// </remarks>
public class FundingRateArchive : BaseEntity
{
    public string Symbol { get; set; } = string.Empty;

    /// <summary>Mốc thanh toán. Bản ghi tại T là tỷ lệ đã thanh toán cho chu kỳ kết thúc tại T.</summary>
    public DateTime FundingTimeUtc { get; set; }

    [Precision(9, 8)] public decimal FundingRate { get; set; }

    [Precision(18, 8)] public decimal? MarkPrice { get; set; }
}
