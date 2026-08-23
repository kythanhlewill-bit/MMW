using MMW.Application.Models;
using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Web.Models;

/// <summary>
/// Kết quả của MỘT nhóm lệnh, tính riêng.
/// </summary>
/// <param name="Expectancy">
/// Kỳ vọng theo R cho mỗi lệnh đã đóng. Đây là con số duy nhất trả lời được câu "hệ này lãi hay
/// lỗ" — tỉ lệ thắng thì không.
/// </param>
/// <param name="BreakevenWinRate">
/// Tỉ lệ thắng CẦN CÓ để hoà vốn, suy từ bội R trung bình của lệnh thắng và lệnh thua. Đặt cạnh
/// tỉ lệ thắng thật thì thấy ngay hệ đang lãi hay lỗ, và lãi/lỗ vì lý do gì.
/// </param>
public sealed record TradeStyleStats(
    TradeStyle Style,
    int Total,
    int Open,
    int Closed,
    int Wins,
    int Losses,
    decimal WinRate,
    decimal TotalPnl,
    decimal TotalR,
    decimal Expectancy,
    decimal AvgWinR,
    decimal AvgLossR,
    decimal? BreakevenWinRate)
{
    public string NameVi => LabelOf(Style);

    public static string LabelOf(TradeStyle style) => style switch
    {
        TradeStyle.HtfSwing => "Lệnh H4",
        _ => "Lệnh ngắn",
    };

    public static string DescriptionOf(TradeStyle style) => style switch
    {
        TradeStyle.HtfSwing => "Xu hướng đọc trên khung 4 giờ, giữ nhiều nhịp, chốt hai phần",
        _ => "Vào ra trong ngày theo kế hoạch ngày",
    };

    /// <summary>Có đang lãi kỳ vọng không. Null khi chưa đủ lệnh đã đóng để nói.</summary>
    public bool? IsProfitable => Closed < 5 ? null : Expectancy > 0m;

    /// <summary>
    /// Tách một danh sách lệnh thành thống kê của từng nhóm.
    /// </summary>
    /// <remarks>
    /// Luôn trả về ĐỦ cả hai nhóm, kể cả nhóm chưa có lệnh nào. Ẩn nhóm rỗng đi thì lúc bộ luật
    /// swing chạy cả tuần mà không vào lệnh nào, màn hình sẽ trông y hệt như lúc nó chưa được
    /// bật — và đó đúng là hai tình huống cần phân biệt nhất.
    /// </remarks>
    public static IReadOnlyList<TradeStyleStats> Split(IEnumerable<TradeDto> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);
        return SplitCore(trades.Select(Row.From).ToList());
    }

    /// <summary>Bản dùng cho thực thể lấy thẳng từ kho, không đi qua DTO.</summary>
    public static IReadOnlyList<TradeStyleStats> Split(IEnumerable<Trade> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);
        return SplitCore(trades.Select(Row.From).ToList());
    }

    /// <summary>
    /// Đúng những trường mà thống kê cần, tách khỏi việc dữ liệu đến từ đâu.
    /// </summary>
    /// <remarks>
    /// Có mặt vì hai màn hình đọc hai kiểu khác nhau — bảng điều khiển đọc thực thể còn sổ lệnh
    /// đọc DTO — và viết hai lần phép tính kỳ vọng là cách chắc chắn để hai màn hình cùng nói về
    /// một tài khoản mà ra hai con số.
    /// </remarks>
    private sealed record Row(
        TradeStyle Style, TradeStatus Status, TradeOutcome? Outcome, decimal? RealizedPnl, decimal? RMultiple)
    {
        public static Row From(TradeDto t) => new(t.Style, t.Status, t.Outcome, t.RealizedPnl, t.RMultiple);
        public static Row From(Trade t) => new(t.Style, t.Status, t.Outcome, t.RealizedPnl, t.RMultiple);
    }

    private static IReadOnlyList<TradeStyleStats> SplitCore(IReadOnlyList<Row> all) =>
        Enum.GetValues<TradeStyle>()
            .Select(style => Build(style, all.Where(t => t.Style == style).ToList()))
            .ToList();

    private static TradeStyleStats Build(TradeStyle style, IReadOnlyList<Row> trades)
    {
        var closed = trades.Where(t => t.Status == TradeStatus.Closed).ToList();
        var wins = closed.Where(t => t.Outcome == TradeOutcome.Win).ToList();
        var losses = closed.Where(t => t.Outcome == TradeOutcome.Loss).ToList();

        // Chỉ tính R trên lệnh CÓ R. Lệnh nhập tay thiếu rủi ro gốc thì không có R, và gán bừa
        // cho nó R = 0 sẽ kéo kỳ vọng về gần 0 bằng những lệnh vốn không nói gì về kỳ vọng cả.
        var withR = closed.Where(t => t.RMultiple is not null).ToList();
        var totalR = withR.Sum(t => t.RMultiple ?? 0m);

        var avgWinR = wins.Count == 0 ? 0m : wins.Where(t => t.RMultiple is not null).DefaultIfEmpty().Average(t => t?.RMultiple ?? 0m);
        var avgLossR = losses.Count == 0 ? 0m : losses.Where(t => t.RMultiple is not null).DefaultIfEmpty().Average(t => t?.RMultiple ?? 0m);

        // Tỉ lệ thắng hoà vốn = |Rthua| / (Rthắng + |Rthua|). Không tính được khi một trong hai
        // vế còn trống — và khi đó không được suy ra một con số nghe có vẻ hợp lý.
        var lossMagnitude = Math.Abs(avgLossR);
        decimal? breakeven = avgWinR > 0m && lossMagnitude > 0m
            ? lossMagnitude / (avgWinR + lossMagnitude) * 100m
            : null;

        return new TradeStyleStats(
            Style: style,
            Total: trades.Count,
            Open: trades.Count(t => t.Status == TradeStatus.Open),
            Closed: closed.Count,
            Wins: wins.Count,
            Losses: losses.Count,
            WinRate: closed.Count == 0 ? 0m : (decimal)wins.Count / closed.Count * 100m,
            TotalPnl: closed.Sum(t => t.RealizedPnl ?? 0m),
            TotalR: totalR,
            Expectancy: withR.Count == 0 ? 0m : totalR / withR.Count,
            AvgWinR: avgWinR,
            AvgLossR: avgLossR,
            BreakevenWinRate: breakeven);
    }
}

/// <summary>Nhãn tiếng Việt cho nhóm lệnh, dùng chung ở mọi màn hình.</summary>
public static class TradeStyleLabels
{
    public static string Name(TradeStyle style) => TradeStyleStats.LabelOf(style);

    /// <summary>Màu badge Tabler cho nhóm. Hai nhóm phải nhìn là phân biệt được ngay.</summary>
    public static string BadgeClass(TradeStyle style) => style switch
    {
        TradeStyle.HtfSwing => "bg-purple-lt",
        _ => "bg-blue-lt",
    };

    public static string Short(TradeStyle style) => style switch
    {
        TradeStyle.HtfSwing => "H4",
        _ => "Ngắn",
    };
}
