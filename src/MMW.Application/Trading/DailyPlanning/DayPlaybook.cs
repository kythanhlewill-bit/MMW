using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Application.Trading.DailyPlanning;

/// <summary>
/// Trả lời đúng một câu: ngày này dùng playbook vào lệnh nào — đi ngang hay theo xu hướng.
/// </summary>
/// <remarks>
/// <b>Vì sao cần lớp này thay vì đọc thẳng <see cref="DailyPlan.DayRegime"/>.</b> Chú thích của
/// <see cref="DayStructure"/> đã cảnh báo từ đầu: <c>DayRegime</c> là NHÃN của cả ngày, và nhãn
/// đó có thể là <c>HighVolatility</c> hay <c>EventDay</c> — hai giá trị không nói gì về cấu trúc
/// giá. <see cref="DayRegimeClassifier"/> tính cấu trúc ở bước 1 rồi <c>Label()</c> ghi đè nó
/// bằng nhãn nguy hiểm ở bước 4, nên thông tin cấu trúc chỉ còn sống trong
/// <see cref="DailyPlan.BtcStructure"/>.
///
/// Hậu quả đo được ngày 2026-08-13: <see cref="Execution.SetupTriggerPolicy"/> rẽ nhánh theo
/// nhãn, nên hai ngày liên tiếp có tin CPI/PPI (nhãn <c>EventDay</c>) rơi vào nhánh xu hướng rồi
/// bị bác ngay dòng đầu vì nhãn không phải TrendUp/TrendDown. <b>302 phiếu, không phiếu nào đi
/// quá bậc 1 của phễu setup</b> — máy không hề đi tìm setup, chứ không phải tìm mà không thấy.
///
/// <b>Đây là lỗ hổng logic, không phải lựa chọn thận trọng.</b> Bằng chứng nằm ngay trong
/// <see cref="RegimeTable"/>: dòng sự kiện cho <c>AllowedDirections.Both</c>, hệ số rủi ro 0,4 và
/// 2 lệnh mỗi ngày; dòng biến động cao cho 0,6 và 3 lệnh. Bảng rủi ro nói "vào nhỏ và ít", còn bộ
/// kích hoạt lại nói "không có playbook nào" — hai chỗ mâu thuẫn nhau, và bảng rủi ro mới là chỗ
/// diễn đạt ý định.
///
/// Sửa ở đây chỉ mở lại ĐƯỜNG ĐI, không nới một tầng bảo vệ nào: hệ số rủi ro theo ngày, quota
/// lệnh, cửa sổ chặn giờ quanh tin, và khoản trừ điểm của <c>market.day_regime_match</c> (4/10
/// cho ngày có tin) đều giữ nguyên.
/// </remarks>
public static class DayPlaybook
{
    /// <summary>Cấu trúc giá thật của ngày, lấy lại từ dưới lớp nhãn nguy hiểm.</summary>
    /// <remarks>
    /// Với ba nhãn cấu trúc thì bản thân nhãn CHÍNH LÀ cấu trúc — quan trọng vì
    /// <see cref="IntradayRegimeOverridePolicy"/> lật Range→TrendUp/TrendDown bằng cách đổi nhãn
    /// trên một bản sao, trong khi <c>BtcStructure</c> của bản sao vẫn giữ chữ "Range" của kế
    /// hoạch gốc. Đọc nhãn trước nên override trong phiên vẫn có hiệu lực.
    ///
    /// Chỉ khi nhãn là nguy hiểm mới lùi về <c>BtcStructure</c>. Không đọc được thì trả
    /// <see cref="DayStructure.Range"/>, đúng bằng cách <see cref="DayRegimeClassifier"/> tự xử
    /// lý khi thiếu nến (xem <c>ReadStructure</c>). Lùi về Range là an toàn vì nó không tự mở
    /// lệnh: bộ dò Rectangle/Triangle phải tìm thấy hình học thật mới cho qua, còn thị trường
    /// đang chạy xu hướng thì nó trả về <c>RangeGeometryWeak</c>/<c>CompressionMissing</c>.
    /// </remarks>
    public static DayStructure StructureOf(DailyPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return StructureOf(plan.DayRegime, plan.BtcStructure);
    }

    /// <inheritdoc cref="StructureOf(DailyPlan)"/>
    public static DayStructure StructureOf(DayRegime regime, string? btcStructure) => regime switch
    {
        DayRegime.TrendUp => DayStructure.TrendUp,
        DayRegime.TrendDown => DayStructure.TrendDown,
        DayRegime.Range => DayStructure.Range,
        _ => Enum.TryParse<DayStructure>(btcStructure, ignoreCase: true, out var parsed)
             && Enum.IsDefined(parsed)
            ? parsed
            : DayStructure.Range,
    };

    /// <summary>Ngày này dùng playbook đi ngang.</summary>
    public static bool UsesRangePlaybook(DailyPlan plan) => StructureOf(plan) == DayStructure.Range;

    /// <summary>Chiều lệnh có thuận cấu trúc ngày không.</summary>
    public static bool IsTrendAligned(DayStructure structure, TradeDirection direction) =>
        (structure == DayStructure.TrendUp && direction == TradeDirection.Long)
        || (structure == DayStructure.TrendDown && direction == TradeDirection.Short);
}
