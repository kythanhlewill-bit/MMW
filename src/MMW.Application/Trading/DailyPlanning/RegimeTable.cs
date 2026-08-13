using MMW.Domain.Enums;

namespace MMW.Application.Trading.DailyPlanning;

/// <summary>Tham số ngày do một dòng của bảng FR-019 áp đặt.</summary>
public sealed record RegimeParameters(
    AllowedDirections AllowedDirections,
    decimal RiskMultiplier,
    int MaxTradesToday);

/// <summary>
/// Bảng ánh xạ FR-019 và phép hợp nhất FR-020.
/// </summary>
/// <remarks>
/// Tách khỏi <see cref="DayRegimeClassifier"/> để bảng trở thành thứ kiểm thử trực tiếp được.
/// Kiểm nó gián tiếp qua chuỗi nến là làm được nhưng mong manh: test sẽ đỏ vì bộ dữ liệu lệch
/// một chút chứ không phải vì bảng sai — và đó là loại test dạy người ta bỏ qua nó.
///
/// Thêm một điều kiện ngày mới = thêm một dòng vào <see cref="Rows"/> (Nguyên tắc V).
/// </remarks>
public static class RegimeTable
{
    /// <summary>
    /// Trần số lệnh mỗi ngày trong GIAI ĐOẠN QUAN SÁT TESTNET (đặt 2026-08-13).
    /// </summary>
    /// <remarks>
    /// <b>Đây là giá trị tạm, PHẢI hạ lại trước khi chạy tiền thật.</b>
    ///
    /// Bậc thang thật của bảng FR-019 là 5 / 3 / 2 tuỳ mức nguy hiểm của ngày. Bậc thang đó bị
    /// làm phẳng ở đây có chủ ý: tính tới 2026-08-13 hệ thống chạy 4 ngày và sinh ra <b>0 lệnh</b>,
    /// nên không có gì để quan sát. Trần thấp không bảo vệ được gì khi chưa lệnh nào vào, mà lại
    /// chặn đúng thứ đang cần: một mẫu đủ lớn để đo.
    ///
    /// Rủi ro thật của việc nâng nằm ở tiền, và tiền đang là tiền ảo (<c>UseTestnet=true</c>).
    /// Các rào còn lại — cap notional, cổng chi phí, cổng chặn giờ, gate chống trùng vị thế —
    /// giữ nguyên, nên nâng trần KHÔNG mở thêm đường nào ngoài số lượng.
    ///
    /// Hạ lại: đổi hằng số này về 5 và trả ba dòng dưới về 3 / 2 / 2.
    /// </remarks>
    public const int ObservationMaxTradesPerDay = 20;

    /// <summary>
    /// Dòng NỀN, luôn khớp. Không có nó thì các tổ hợp ngoài bảng FR-019 — ví dụ "xu hướng
    /// tăng + biến động cao", vốn rất thường gặp — sẽ không khớp dòng nào và rơi vào trạng
    /// thái không xác định.
    ///
    /// Chiều của dòng nền lấy theo CẤU TRÚC, vì ràng buộc đã chốt với người dùng là ngày trend
    /// chỉ vào một chiều thuận trend. Bảng FR-019 chỉ nói về chiều ở hai dòng đầu (trend +
    /// biến động bình thường), nên nếu để dòng nền cho "cả hai" thì ngày "tăng + biến động
    /// cao" sẽ mở cửa cho lệnh bán ngược xu hướng.
    /// </summary>
    private static RegimeParameters BaseRow(DayStructure structure) => new(
        structure switch
        {
            DayStructure.TrendUp => AllowedDirections.LongOnly,
            DayStructure.TrendDown => AllowedDirections.ShortOnly,
            _ => AllowedDirections.Both,
        },
        RiskMultiplier: 1.0m,
        MaxTradesToday: ObservationMaxTradesPerDay);

    /// <summary>Bảng FR-019. Mỗi phần tử: điều kiện khớp và tham số nó áp đặt.</summary>
    private static readonly (Func<DayStructure, VolatilityRegime, bool, bool> Matches, RegimeParameters Parameters)[] Rows =
    {
        ((s, v, _) => s == DayStructure.TrendUp && v == VolatilityRegime.Normal,
            new RegimeParameters(AllowedDirections.LongOnly, 1.0m, ObservationMaxTradesPerDay)),

        ((s, v, _) => s == DayStructure.TrendDown && v == VolatilityRegime.Normal,
            new RegimeParameters(AllowedDirections.ShortOnly, 1.0m, ObservationMaxTradesPerDay)),

        ((s, v, _) => s == DayStructure.Range && v == VolatilityRegime.Low,
            new RegimeParameters(AllowedDirections.Both, 0.5m, ObservationMaxTradesPerDay)),

        // Vùng biến động CAO (phân vị 75–90) trước đây không khớp dòng nào và rơi vào BaseRow:
        // rủi ro 1.0 và 5 lệnh, y hệt một ngày yên bình. Đó là lỗ hổng nguy hiểm nhất của bảng.
        //
        // Với khung giữ lệnh 1–4 tiếng và dừng lỗ tính theo ATR, 75–90 chính là vùng dừng lỗ bị
        // quét nhiều nhất: biên độ đã đủ lớn để nến chọc thủng mọi mức kỹ thuật, nhưng chưa đủ
        // lớn để bị gọi là Extreme và tự thu nhỏ. Vùng Extreme ít nguy hiểm hơn CHÍNH VÌ nó đã
        // bị cap 0.3 từ dòng dưới.
        ((_, v, _) => v == VolatilityRegime.High,
            new RegimeParameters(AllowedDirections.Both, 0.6m, ObservationMaxTradesPerDay)),

        ((_, v, _) => v == VolatilityRegime.Extreme,
            new RegimeParameters(AllowedDirections.Both, 0.3m, ObservationMaxTradesPerDay)),

        ((_, _, hasEvent) => hasEvent,
            new RegimeParameters(AllowedDirections.Both, 0.4m, ObservationMaxTradesPerDay)),
    };

    /// <summary>
    /// Áp bảng FR-019 rồi hợp nhất theo FR-020: <c>MIN</c> hệ số, <c>MIN</c> số lệnh,
    /// <b>giao</b> của các chiều.
    /// </summary>
    /// <remarks>
    /// Lấy giá trị nhỏ nhất chứ không lấy dòng khớp đầu tiên, và giao thay vì hợp — cả hai đều
    /// nghiêng về phía thận trọng. Đây là quy tắc cần nhất quán tuyệt đối, vì nó chạy vào đúng
    /// những ngày nguy hiểm nhất.
    /// </remarks>
    public static RegimeParameters Resolve(DayStructure structure, VolatilityRegime volatility, bool hasHighImpactEvent)
    {
        var result = BaseRow(structure);

        foreach (var (matches, p) in Rows)
        {
            if (!matches(structure, volatility, hasHighImpactEvent)) continue;

            result = new RegimeParameters(
                Intersect(result.AllowedDirections, p.AllowedDirections),
                Math.Min(result.RiskMultiplier, p.RiskMultiplier),
                Math.Min(result.MaxTradesToday, p.MaxTradesToday));
        }

        return EnforceNoDirectionMeansNoTrades(result);
    }

    /// <summary>Bất biến 5: không cho chiều nào thì số lệnh tối đa phải bằng 0.</summary>
    /// <remarks>
    /// Cưỡng chế ở nơi tính chứ không dựa vào việc "hiện chưa có tổ hợp nào cho ra None".
    /// Một dòng mới thêm sau này có thể tạo ra tổ hợp đó, và khi ấy "cho phép 5 lệnh theo
    /// không chiều nào" là một trạng thái vô nghĩa mà mã phía sau sẽ diễn giải tuỳ hứng.
    /// </remarks>
    public static RegimeParameters EnforceNoDirectionMeansNoTrades(RegimeParameters p) =>
        p.AllowedDirections == AllowedDirections.None ? p with { MaxTradesToday = 0 } : p;

    /// <summary>Giao của hai tập chiều được phép.</summary>
    public static AllowedDirections Intersect(AllowedDirections a, AllowedDirections b)
    {
        // Bit 0 = mua, bit 1 = bán — đúng theo giá trị của enum (LongOnly=1, ShortOnly=2, Both=3).
        var intersection = (int)a & (int)b;
        return (AllowedDirections)intersection;
    }
}

/// <summary>Ánh xạ phân vị biến động sang vùng (bước 2 của thuật toán).</summary>
public static class VolatilityBands
{
    public const decimal LowBelow = 25m;
    public const decimal HighAbove = 75m;
    public const decimal ExtremeAbove = 90m;

    /// <summary>
    /// Null ⟹ <see cref="VolatilityRegime.Normal"/>.
    /// </summary>
    /// <remarks>
    /// Mặc định <c>Extreme</c> nghe an toàn hơn, nhưng nó biến "thiếu dữ liệu" thành "hệ số
    /// 0.3 mỗi ngày" cho tới khi có đủ 60 phiên lịch sử — và trader sẽ tắt hệ thống trước khi
    /// đến ngày đó. Phần phạt thiếu dữ liệu đã có đường riêng ở bước 5.
    /// </remarks>
    public static VolatilityRegime From(decimal? percentile) => percentile switch
    {
        null => VolatilityRegime.Normal,
        < LowBelow => VolatilityRegime.Low,
        <= HighAbove => VolatilityRegime.Normal,
        <= ExtremeAbove => VolatilityRegime.High,
        _ => VolatilityRegime.Extreme,
    };
}
