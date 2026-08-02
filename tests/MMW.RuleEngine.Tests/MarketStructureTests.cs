using MMW.Application.MarketData.Models;
using MMW.Application.Trading.Structure;
using Xunit;

namespace MMW.RuleEngine.Tests;

/// <summary>
/// Phá vỡ cấu trúc và kiểm định lại (R-007).
/// </summary>
/// <remarks>
/// "Phá vỡ cấu trúc" là khái niệm thường được mô tả bằng lời và mỗi người hiểu một kiểu —
/// không chấp nhận được với yêu cầu tất định. Định nghĩa đã chốt:
/// <list type="bullet">
/// <item>Phá vỡ tăng: giá ĐÓNG CỬA vượt điểm xoay đỉnh đã XÁC NHẬN gần nhất.</item>
/// <item>Kiểm định lại thành công: sau khi phá vỡ, giá quay về chạm vùng phá vỡ
/// ±0.25 ATR rồi đóng cửa trở lại đúng chiều, trong vòng <c>M</c> nến.</item>
/// </list>
/// Thang điểm: phá vỡ có kiểm định lại 10 · phá vỡ chưa kiểm định lại 5 · không phá vỡ 0.
/// </remarks>
public class MarketStructureTests
{
    private readonly MarketStructureAnalyzer _analyzer = new(new SwingDetector());
    private static readonly DateTime T0 = new(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);

    private static Candle Bar(int i, decimal high, decimal low, decimal close)
    {
        var open = T0.AddMinutes(15 * i);
        return new Candle(open, close, high, low, close, 10m, open.AddMinutes(15).AddMilliseconds(-1));
    }

    /// <summary>
    /// Chuỗi nền đi ngang tạo một đỉnh xoay đã xác nhận tại chỉ số 2 với giá 110.
    /// Chỉ số:   0    1    2      3    4
    /// High:   100  101  [110]  102   99
    /// </summary>
    private static List<Candle> BaseWithConfirmedHigh() => new()
    {
        Bar(0, 100, 90, 95), Bar(1, 101, 91, 96), Bar(2, 110, 92, 100),
        Bar(3, 102, 93, 97), Bar(4, 99, 89, 94),
    };

    [Fact]
    public void Khong_co_pha_vo_thi_tra_ve_None()
    {
        var candles = BaseWithConfirmedHigh();
        candles.Add(Bar(5, 100, 92, 95));   // đóng cửa 95, chưa vượt 110

        var r = _analyzer.Analyze(candles, pivotBars: 2, retestWindowBars: 6, atr: 4m);

        Assert.Equal(StructureBreak.None, r.Break);
        Assert.Null(r.BrokenLevel);
    }

    [Fact]
    public void Dong_cua_vuot_dinh_xoay_la_pha_vo_TANG()
    {
        var candles = BaseWithConfirmedHigh();
        candles.Add(Bar(5, 115, 100, 114));   // đóng cửa 114 > 110

        var r = _analyzer.Analyze(candles, pivotBars: 2, retestWindowBars: 6, atr: 4m);

        Assert.Equal(StructureBreak.BullishBreak, r.Break);
        Assert.Equal(110m, r.BrokenLevel);
        Assert.False(r.RetestConfirmed);
    }

    [Fact]
    public void Chi_cham_bang_rau_nen_ma_khong_dong_cua_vuot_thi_KHONG_tinh_la_pha_vo()
    {
        // Râu nến xuyên qua mức rồi rút về là chuyện xảy ra liên tục. Nếu tính đó là
        // phá vỡ thì tiêu chí này sẽ báo phá vỡ gần như mỗi ngày và mất hết ý nghĩa.
        var candles = BaseWithConfirmedHigh();
        candles.Add(Bar(5, 120, 100, 105));   // râu lên 120 nhưng đóng cửa 105 < 110

        var r = _analyzer.Analyze(candles, pivotBars: 2, retestWindowBars: 6, atr: 4m);

        Assert.Equal(StructureBreak.None, r.Break);
    }

    [Fact]
    public void Kiem_dinh_lai_THANH_CONG_trong_M_nen()
    {
        var candles = BaseWithConfirmedHigh();
        candles.Add(Bar(5, 115, 100, 114));   // phá vỡ
        candles.Add(Bar(6, 114, 109, 111));   // quay về chạm vùng 110 ±0.25×4 = ±1
        candles.Add(Bar(7, 118, 111, 117));   // đóng cửa trở lại trên vùng phá vỡ

        var r = _analyzer.Analyze(candles, pivotBars: 2, retestWindowBars: 6, atr: 4m);

        Assert.Equal(StructureBreak.BullishBreak, r.Break);
        Assert.True(r.RetestConfirmed);
    }

    [Fact]
    public void Kiem_dinh_lai_THAT_BAI_khi_dong_cua_thung_han_xuong_duoi()
    {
        var candles = BaseWithConfirmedHigh();
        candles.Add(Bar(5, 115, 100, 114));
        candles.Add(Bar(6, 114, 100, 101));   // đóng cửa 101, thủng hẳn dưới vùng 110±1

        var r = _analyzer.Analyze(candles, pivotBars: 2, retestWindowBars: 6, atr: 4m);

        Assert.Equal(StructureBreak.BullishBreak, r.Break);
        Assert.False(r.RetestConfirmed);
        Assert.True(r.RetestFailed);
    }

    [Fact]
    public void Kiem_dinh_lai_ngoai_cua_so_M_nen_thi_KHONG_tinh()
    {
        var candles = BaseWithConfirmedHigh();
        candles.Add(Bar(5, 115, 100, 114));            // phá vỡ tại chỉ số 5
        for (var i = 6; i <= 8; i++) candles.Add(Bar(i, 120, 116, 118));   // chạy xa, không quay lại
        candles.Add(Bar(9, 118, 109, 117));            // quay về chạm — nhưng đã quá 3 nến cửa sổ

        var r = _analyzer.Analyze(candles, pivotBars: 2, retestWindowBars: 3, atr: 4m);

        Assert.Equal(StructureBreak.BullishBreak, r.Break);
        Assert.False(r.RetestConfirmed);
    }

    [Fact]
    public void Pha_vo_GIAM_doi_xung_voi_pha_vo_tang()
    {
        // Đáy xoay đã xác nhận tại chỉ số 2 với giá 80.
        var candles = new List<Candle>
        {
            Bar(0, 105, 100, 102), Bar(1, 104, 99, 101), Bar(2, 103, 80, 90),
            Bar(3, 102, 95, 98), Bar(4, 106, 97, 103),
            Bar(5, 100, 75, 78),   // đóng cửa 78 < 80
        };

        var r = _analyzer.Analyze(candles, pivotBars: 2, retestWindowBars: 6, atr: 4m);

        Assert.Equal(StructureBreak.BearishBreak, r.Break);
        Assert.Equal(80m, r.BrokenLevel);
    }

    [Fact]
    public void Chuoi_qua_ngan_de_co_diem_xoay_thi_tra_None_chu_khong_nem()
    {
        var r = _analyzer.Analyze(new List<Candle> { Bar(0, 100, 90, 95) }, 2, 6, 4m);

        Assert.Equal(StructureBreak.None, r.Break);
        Assert.False(r.RetestConfirmed);
    }

    [Fact]
    public void Atr_bang_khong_khong_lam_no_phep_tinh_vung_kiem_dinh()
    {
        // ATR = 0 nghĩa là dải kiểm định co về đúng một điểm. Không được chia cho 0,
        // không được ném — chỉ đơn giản là rất khó thoả.
        var candles = BaseWithConfirmedHigh();
        candles.Add(Bar(5, 115, 100, 114));
        candles.Add(Bar(6, 114, 109, 111));

        var r = _analyzer.Analyze(candles, pivotBars: 2, retestWindowBars: 6, atr: 0m);

        Assert.Equal(StructureBreak.BullishBreak, r.Break);
    }
}
