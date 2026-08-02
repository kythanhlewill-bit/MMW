using MMW.Application.MarketData.Models;
using MMW.Application.Trading.Structure;
using Xunit;

namespace MMW.RuleEngine.Tests;

/// <summary>
/// Điểm xoay fractal: đỉnh tại <c>i</c> khi <c>High[i]</c> lớn hơn <c>High</c> của <c>N</c> nến
/// hai bên (R-007).
/// </summary>
/// <remarks>
/// Hệ quả quan trọng nhất là ĐỘ TRỄ: một điểm xoay chỉ được xác nhận sau <c>N</c> nến.
/// Đây là chủ ý, không phải khiếm khuyết — nó loại bỏ hoàn toàn khả năng nhìn trước tương lai
/// trong kiểm thử lịch sử. Định nghĩa "đỉnh cao nhất trong cửa sổ trượt" thì không có tính chất
/// đó: đỉnh "gần nhất" sẽ đổi mỗi nến, và backtest sẽ đẹp một cách gian lận.
/// </remarks>
public class SwingDetectorTests
{
    private readonly SwingDetector _detector = new();
    private static readonly DateTime T0 = new(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Dựng chuỗi nến từ danh sách (high, low); thân nến không quan trọng ở đây.</summary>
    private static List<Candle> Build(params (decimal High, decimal Low)[] bars)
    {
        var list = new List<Candle>(bars.Length);
        for (var i = 0; i < bars.Length; i++)
        {
            var open = T0.AddMinutes(15 * i);
            var mid = (bars[i].High + bars[i].Low) / 2m;
            list.Add(new Candle(open, mid, bars[i].High, bars[i].Low, mid, 10m,
                open.AddMinutes(15).AddMilliseconds(-1)));
        }
        return list;
    }

    [Fact]
    public void Nhan_dien_diem_xoay_dinh_don_gian()
    {
        // Chỉ số:      0     1     2       3     4
        // High:      100   101   [110]   102   99      → đỉnh tại 2 với N=2
        var candles = Build((100, 90), (101, 91), (110, 92), (102, 93), (99, 89));

        var pivots = _detector.Detect(candles, pivotBars: 2);

        var high = Assert.Single(pivots, p => p.IsHigh);
        Assert.Equal(2, high.Index);
        Assert.Equal(110m, high.Price);
    }

    [Fact]
    public void Nhan_dien_diem_xoay_day_don_gian()
    {
        var candles = Build((100, 95), (99, 94), (98, 80), (99, 93), (100, 96));

        var pivots = _detector.Detect(candles, pivotBars: 2);

        var low = Assert.Single(pivots, p => !p.IsHigh);
        Assert.Equal(2, low.Index);
        Assert.Equal(80m, low.Price);
    }

    [Fact]
    public void Diem_xoay_duoc_XAC_NHAN_dung_N_nen_sau_do()
    {
        var candles = Build((100, 90), (101, 91), (110, 92), (102, 93), (99, 89));

        var high = _detector.Detect(candles, pivotBars: 2).Single(p => p.IsHigh);

        // Xảy ra tại nến 2, nhưng chỉ biết được khi nến 4 đã đóng.
        Assert.Equal(2, high.Index);
        Assert.Equal(4, high.ConfirmedAtIndex);
        Assert.Equal(candles[4].CloseTime, high.ConfirmedAtUtc);
    }

    [Fact]
    public void KHONG_nhin_truoc_tuong_lai_diem_xoay_chi_hien_khi_du_N_nen_sau()
    {
        // Bất biến quan trọng nhất của cả lớp này: cắt chuỗi ngay sau nến đỉnh thì
        // đỉnh đó KHÔNG được xuất hiện. Nếu nó xuất hiện, mọi con số backtest đều nói dối.
        var full = Build((100, 90), (101, 91), (110, 92), (102, 93), (99, 89));

        for (var cut = 3; cut <= 4; cut++)
        {
            var partial = full.Take(cut).ToList();
            Assert.DoesNotContain(_detector.Detect(partial, pivotBars: 2), p => p.IsHigh && p.Index == 2);
        }

        // Đủ 5 nến (index 0..4) thì mới thấy.
        Assert.Contains(_detector.Detect(full, pivotBars: 2), p => p.IsHigh && p.Index == 2);
    }

    [Fact]
    public void N_nen_dau_va_N_nen_cuoi_khong_bao_gio_la_diem_xoay()
    {
        var candles = Build((999, 1), (101, 91), (102, 92), (103, 93), (998, 2));

        var pivots = _detector.Detect(candles, pivotBars: 2);

        Assert.DoesNotContain(pivots, p => p.Index < 2 || p.Index > candles.Count - 3);
    }

    [Fact]
    public void Dinh_bang_nhau_hai_ben_thi_KHONG_phai_diem_xoay()
    {
        // So sánh dùng "lớn hơn hẳn", không phải "lớn hơn hoặc bằng". Vùng đi ngang phẳng
        // không nên sinh ra một loạt điểm xoay giả.
        var candles = Build((100, 90), (110, 91), (110, 92), (110, 93), (100, 89));

        Assert.DoesNotContain(_detector.Detect(candles, pivotBars: 2), p => p.IsHigh);
    }

    [Fact]
    public void Chuoi_ngan_hon_2N_cong_1_tra_ve_rong()
    {
        Assert.Empty(_detector.Detect(Build((100, 90), (101, 91), (102, 92)), pivotBars: 2));
        Assert.Empty(_detector.Detect(new List<Candle>(), pivotBars: 2));
    }

    [Fact]
    public void Diem_xoay_tra_ve_theo_thu_tu_chi_so_tang_dan()
    {
        var candles = Build(
            (100, 90), (101, 91), (110, 92), (102, 93), (99, 80),
            (103, 94), (112, 95), (104, 96), (100, 91));

        var pivots = _detector.Detect(candles, pivotBars: 2);

        Assert.True(pivots.Count >= 2);
        Assert.Equal(pivots.Select(p => p.Index).OrderBy(i => i), pivots.Select(p => p.Index));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void PivotBars_khong_hop_le_thi_nem(int n)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _detector.Detect(Build((100, 90)), n));
    }
}
