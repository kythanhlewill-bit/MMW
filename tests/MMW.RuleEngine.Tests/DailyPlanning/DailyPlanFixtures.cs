using MMW.Application.MarketData.Models;
using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.RuleEngine.Tests.DailyPlanning;

/// <summary>
/// Bộ dựng nến cho các test kế hoạch ngày.
/// </summary>
/// <remarks>
/// Hai kiểu chuỗi, mỗi kiểu điều khiển đúng MỘT tính chất, để mỗi test chỉ khẳng định thứ mà
/// bộ dữ liệu của nó thực sự chi phối:
///
/// <see cref="FlatClose"/> — giá đóng cửa cố định, biên độ thật của nến bằng đúng con số cho
/// trước. Nhờ vậy chuỗi ATR điều khiển được chính xác, còn cấu trúc thì luôn là đi ngang (mọi
/// đỉnh trong một khối đều bằng nhau nên không có điểm xoay chặt nào).
///
/// <see cref="ZigZag"/> — đường giá gấp khúc tạo ra đỉnh/đáy xoay ở vị trí biết trước. Chuỗi
/// này ngắn nên phân vị biến động sẽ THIẾU; test dùng nó chỉ khẳng định về cấu trúc.
/// </remarks>
internal static class DailyPlanFixtures
{
    public static readonly DateTime Day0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Nến ngày có <c>Close = Open = close</c> và biên độ thật đúng bằng <c>range</c>.
    /// </summary>
    /// <remarks>
    /// Với giá đóng cửa không đổi thì <c>TR = max(H−L, |H−prevClose|, |L−prevClose|)
    /// = max(r, r/2, r/2) = r</c>, nên chuỗi ATR là trung bình Wilder của chính dãy
    /// <paramref name="ranges"/> — điều khiển được đến từng con số.
    /// </remarks>
    public static List<Candle> FlatClose(IEnumerable<decimal> ranges, decimal close = 1000m)
    {
        var list = new List<Candle>();
        var i = 0;
        foreach (var r in ranges)
        {
            list.Add(new Candle(
                OpenTime: Day0.AddDays(i),
                Open: close,
                High: close + r / 2m,
                Low: close - r / 2m,
                Close: close,
                Volume: 100m,
                CloseTime: Day0.AddDays(i + 1).AddTicks(-1)));
            i++;
        }
        return list;
    }

    /// <summary>Nối một dãy giá thành nến ngày, mỗi nến có biên độ ±1 quanh giá.</summary>
    public static List<Candle> ZigZag(IReadOnlyList<decimal> path)
    {
        var list = new List<Candle>(path.Count);
        for (var i = 0; i < path.Count; i++)
        {
            var p = path[i];
            list.Add(new Candle(
                OpenTime: Day0.AddDays(i),
                Open: p,
                High: p + 1m,
                Low: p - 1m,
                Close: p,
                Volume: 100m,
                CloseTime: Day0.AddDays(i + 1).AddTicks(-1)));
        }
        return list;
    }

    /// <summary>
    /// Đường giá 20 phiên có đáy xoay 100 → 104 và đỉnh xoay 110 → 116: đỉnh cao dần, đáy
    /// cao dần ⟹ xu hướng tăng. Điểm xoay xác nhận với <c>SwingPivotBars = 2</c>.
    /// </summary>
    /// <remarks>
    /// Dài đúng 20 phiên vì cửa sổ đọc cấu trúc là 20 phiên gần nhất — chuỗi ngắn hơn sẽ bị
    /// tính là thiếu dữ liệu và mọi khẳng định về cấu trúc trở nên vô nghĩa.
    ///
    /// Ba phiên đuôi đi xuống đều nên không sinh thêm điểm xoay nào: phiên 17 không phải đáy
    /// (đáy của nó cao hơn đáy phiên 18), còn phiên 18–19 chưa đủ hai nến sau để xác nhận.
    /// </remarks>
    public static IReadOnlyList<decimal> UptrendPath => new decimal[]
    {
        106, 103, 100, 103, 106, 108, 110, 108, 106, 105,
        104, 107, 110, 113, 116, 113, 110, 108, 106, 104,
    };

    /// <summary>Ảnh gương của <see cref="UptrendPath"/> qua mức 200: đỉnh thấp dần, đáy thấp dần.</summary>
    public static IReadOnlyList<decimal> DowntrendPath =>
        UptrendPath.Select(p => 200m - p).ToList();

    /// <summary>
    /// Đỉnh cao dần nhưng đáy THẤP dần — mở rộng biên độ hai phía. Không thoả xu hướng tăng
    /// (đòi hỏi cả hai cùng cao dần) cũng không thoả xu hướng giảm ⟹ đi ngang.
    /// </summary>
    public static IReadOnlyList<decimal> RangePath => new decimal[]
    {
        106, 103, 100, 103, 106, 108, 110, 108, 105, 101,
        96, 101, 106, 111, 116, 111, 106, 104, 102, 100,
    };

    public static EngineSetting Settings() => EngineSettingDefaults.Create(tradingAccountId: 1);

    public static ScheduledEvent Event(MacroEventImpact impact, ScheduledEventKind kind = ScheduledEventKind.Cpi) => new()
    {
        Kind = kind,
        Title = kind.ToString(),
        OccursAtUtc = Day0.AddHours(13),
        Impact = impact,
        Origin = ScheduledEventOrigin.Seeded,
        SourceKey = $"test:{kind}:{impact}",
    };
}
