using MMW.Application.MarketData.Models;
using MMW.Application.Trading.Structure;
using MMW.Domain.Enums;
using MMW.RuleEngine.Tests.Scoring;
using Xunit;

namespace MMW.RuleEngine.Tests.Structure;

/// <summary>
/// Đọc cấu trúc khung 4 giờ: chuỗi đỉnh/đáy trước, chỉ báo sau.
/// </summary>
/// <remarks>
/// Điều đáng kiểm nhất ở đây không phải "nó có nhận ra xu hướng tăng không" mà là ba chỗ dễ sai
/// và sai thì rất tốn tiền:
///
/// • <b>Không đọc được ≠ đi ngang.</b> Chuỗi lẫn lộn phải trả <c>Unclear</c> để bộ luật swing
///   đứng ngoài, chứ không được suy ra một hướng nào đó rồi vào lệnh.
/// • <b>Vùng giá trị chỉ nằm đúng phía nhịp hồi.</b> Một mức nằm bên kia giá là MỤC TIÊU, không
///   phải chỗ để vào thuận xu hướng — lẫn hai thứ đó là vào lệnh ngược ngay tại đỉnh.
/// • <b>Hợp lưu phải đếm đúng.</b> Cỡ lệnh đi theo số lớp, nên đếm trùng một lớp hai lần là tự
///   cho phép mình vào nặng tay ở một vùng mỏng.
/// </remarks>
public sealed class HtfSwingAnalyzerTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly HtfSwingAnalyzer _analyzer = new(ScoringFixtures.Swings, ScoringFixtures.Indicators);

    private const int PivotBars = 3;
    private const int Lookback = 60;

    // ── Chuỗi giá dựng tay ───────────────────────────────────────────────

    /// <summary>
    /// Dựng chuỗi nến từ danh sách giá đóng.
    /// </summary>
    /// <remarks>
    /// Râu nến tính từ chính GIÁ ĐÓNG của nến đó, không tính từ khoảng mở–đóng. Cách thứ hai
    /// nghe tự nhiên hơn nhưng làm nến đảo chiều có đỉnh BẰNG ĐÚNG nến trước nó — vì nó mở ở
    /// đúng giá đóng của nến đỉnh — và bộ dò điểm xoay dùng so sánh "lớn hơn hẳn", nên hai đỉnh
    /// bằng nhau sẽ triệt tiêu chính cái pivot mà chuỗi thử nghiệm này dựng ra để có.
    /// </remarks>
    private static List<Candle> Series(IEnumerable<decimal> closes, decimal wick = 0.004m)
    {
        var list = new List<Candle>();
        var i = 0;
        var previous = 0m;

        foreach (var close in closes)
        {
            var open = previous == 0m ? close : previous;
            var high = close * (1m + wick);
            var low = close * (1m - wick);
            list.Add(new Candle(
                Start.AddHours(4 * i), open, Math.Max(high, open), Math.Min(low, open), close,
                1000m, Start.AddHours(4 * (i + 1))));
            previous = close;
            i++;
        }

        return list;
    }

    /// <summary>
    /// Chuỗi zigzag đi lên: mỗi nhịp tạo đỉnh cao hơn và đáy cao hơn.
    /// </summary>
    private static List<Candle> UpTrend()
    {
        var closes = new List<decimal>();

        // Đoạn nền phẳng để có đủ nến cho EMA/ATR trước khi cấu trúc bắt đầu.
        for (var i = 0; i < 20; i++) closes.Add(1000m);

        // Bốn nhịp: mỗi nhịp lên 120 rồi hồi 50 → đỉnh và đáy đều cao dần.
        var level = 1000m;
        for (var leg = 0; leg < 4; leg++)
        {
            for (var i = 0; i < 7; i++) closes.Add(level += 120m / 7m);
            for (var i = 0; i < 7; i++) closes.Add(level -= 50m / 7m);
        }

        return Series(closes);
    }

    private static List<Candle> DownTrend()
    {
        var closes = new List<decimal>();
        for (var i = 0; i < 20; i++) closes.Add(2000m);

        var level = 2000m;
        for (var leg = 0; leg < 4; leg++)
        {
            for (var i = 0; i < 7; i++) closes.Add(level -= 120m / 7m);
            for (var i = 0; i < 7; i++) closes.Add(level += 50m / 7m);
        }

        return Series(closes);
    }

    /// <summary>Chuỗi dao động quanh một mức, đỉnh và đáy không có hướng nhất quán.</summary>
    private static List<Candle> Choppy()
    {
        var closes = new List<decimal>();
        for (var i = 0; i < 20; i++) closes.Add(1000m);

        // Biên độ thay đổi thất thường: đỉnh sau thấp hơn nhưng đáy sau lại cao hơn.
        decimal[] pattern = [1080m, 1000m, 1120m, 1040m, 1060m, 990m, 1100m, 1010m];
        foreach (var target in pattern)
        {
            var from = closes[^1];
            for (var i = 1; i <= 5; i++) closes.Add(from + (target - from) * i / 5m);
        }

        return Series(closes);
    }

    // ── Đọc xu hướng ─────────────────────────────────────────────────────

    [Fact]
    public void Chuoi_dinh_va_day_cao_dan_la_xu_huong_tang()
    {
        var read = _analyzer.ReadTrend(UpTrend(), PivotBars, Lookback);

        Assert.Equal(HtfTrend.Up, read.Trend);
        Assert.True(read.Supports(TradeDirection.Long));
        Assert.False(read.Supports(TradeDirection.Short));
        Assert.NotNull(read.LastSwingHigh);
        Assert.NotNull(read.LastSwingLow);
        Assert.True(read.LastSwingHigh > read.PriorSwingHigh, read.DetailVi);
        Assert.True(read.LastSwingLow > read.PriorSwingLow, read.DetailVi);
    }

    [Fact]
    public void Chuoi_dinh_va_day_thap_dan_la_xu_huong_giam()
    {
        var read = _analyzer.ReadTrend(DownTrend(), PivotBars, Lookback);

        Assert.Equal(HtfTrend.Down, read.Trend);
        Assert.True(read.Supports(TradeDirection.Short));
        Assert.True(read.LastSwingHigh < read.PriorSwingHigh, read.DetailVi);
    }

    /// <summary>
    /// Chuỗi lẫn lộn KHÔNG được suy ra hướng nào.
    /// </summary>
    /// <remarks>
    /// Đây là ranh giới quan trọng nhất của cả bộ luật. Mọi phiên bản trước dùng chồng trung
    /// bình động, và trung bình động thì LUÔN nghiêng về một phía kể cả trong vùng nhiễu — nên
    /// chúng luôn có một câu trả lời, kể cả khi không có gì để trả lời. "Không đọc được" phải là
    /// một kết luận hợp lệ, nếu không thì bộ luật sẽ giao dịch cả những ngày nó mù.
    /// </remarks>
    [Fact]
    public void Chuoi_lan_lon_thi_khong_doc_duoc_xu_huong()
    {
        var read = _analyzer.ReadTrend(Choppy(), PivotBars, Lookback);

        Assert.Equal(HtfTrend.Unclear, read.Trend);
        Assert.False(read.Supports(TradeDirection.Long));
        Assert.False(read.Supports(TradeDirection.Short));
        Assert.Null(read.InvalidationPrice);
    }

    [Fact]
    public void Thieu_nen_thi_tra_ve_khong_doc_duoc_chu_khong_nem()
    {
        var read = _analyzer.ReadTrend(Series([1000m, 1010m, 1020m]), PivotBars, Lookback);

        Assert.Equal(HtfTrend.Unclear, read.Trend);
        Assert.Contains("cần ít nhất", read.DetailVi);
    }

    /// <summary>Mức làm hỏng cấu trúc của xu hướng tăng là đáy cao hơn gần nhất.</summary>
    [Fact]
    public void Muc_lam_hong_cau_truc_la_day_gan_nhat_khi_tang()
    {
        var read = _analyzer.ReadTrend(UpTrend(), PivotBars, Lookback);

        Assert.Equal(read.LastSwingLow, read.InvalidationPrice);
    }

    [Fact]
    public void Muc_lam_hong_cau_truc_la_dinh_gan_nhat_khi_giam()
    {
        var read = _analyzer.ReadTrend(DownTrend(), PivotBars, Lookback);

        Assert.Equal(read.LastSwingHigh, read.InvalidationPrice);
    }

    // ── Vùng giá trị ─────────────────────────────────────────────────────

    [Fact]
    public void Xu_huong_tang_chi_dung_vung_nam_duoi_gia()
    {
        var candles = UpTrend();
        var read = _analyzer.ReadTrend(candles, PivotBars, Lookback);
        var price = candles[^1].Close;

        var zones = _analyzer.BuildValueZones(candles, read, price, 0.25m);

        Assert.NotEmpty(zones);
        // Mép trên của mọi vùng phải nằm dưới hoặc ngay tại giá — không có vùng nào "ở trên đầu".
        Assert.All(zones, z => Assert.True(z.Low < price, $"Vùng {z.Low}–{z.High} nằm trên giá {price}."));
    }

    [Fact]
    public void Xu_huong_giam_chi_dung_vung_nam_tren_gia()
    {
        var candles = DownTrend();
        var read = _analyzer.ReadTrend(candles, PivotBars, Lookback);
        var price = candles[^1].Close;

        var zones = _analyzer.BuildValueZones(candles, read, price, 0.25m);

        Assert.NotEmpty(zones);
        Assert.All(zones, z => Assert.True(z.High > price, $"Vùng {z.Low}–{z.High} nằm dưới giá {price}."));
    }

    /// <summary>
    /// Vùng gần giá nhất phải đứng đầu danh sách.
    /// </summary>
    /// <remarks>
    /// Bộ kích hoạt lấy vùng đầu tiên chứa giá, và khi giá chưa vào vùng nào thì nó lấy vùng đầu
    /// tiên để báo "còn cách bao xa". Sắp sai thứ tự thì thông báo đó chỉ vào một vùng ở tận đâu.
    /// </remarks>
    [Fact]
    public void Vung_gan_gia_nhat_dung_dau_danh_sach()
    {
        var candles = UpTrend();
        var read = _analyzer.ReadTrend(candles, PivotBars, Lookback);
        var price = candles[^1].Close;

        var zones = _analyzer.BuildValueZones(candles, read, price, 0.25m);

        if (zones.Count < 2) return;
        for (var i = 1; i < zones.Count; i++)
            Assert.True(zones[i - 1].High >= zones[i].High, "Vùng không được sắp theo khoảng cách tới giá.");
    }

    /// <summary>Mỗi loại lớp chỉ được đếm MỘT lần trong một vùng.</summary>
    [Fact]
    public void Moi_lop_chi_dem_mot_lan_trong_cung_mot_vung()
    {
        var candles = UpTrend();
        var read = _analyzer.ReadTrend(candles, PivotBars, Lookback);

        // Nới rất rộng để mọi lớp chắc chắn gộp vào một vùng duy nhất.
        var zones = _analyzer.BuildValueZones(candles, read, candles[^1].Close, 5m);

        Assert.All(zones, z => Assert.Equal(z.Layers.Count, z.Layers.Distinct().Count()));
    }

    [Fact]
    public void Khong_doc_duoc_xu_huong_thi_khong_co_vung_nao()
    {
        var candles = Choppy();
        var read = _analyzer.ReadTrend(candles, PivotBars, Lookback);

        var zones = _analyzer.BuildValueZones(candles, read, candles[^1].Close, 0.25m);

        Assert.Empty(zones);
    }

    /// <summary>Bề rộng vùng phải đi theo ATR — nới hệ số thì vùng phải rộng ra.</summary>
    [Fact]
    public void Vung_rong_theo_he_so_ATR()
    {
        var candles = UpTrend();
        var read = _analyzer.ReadTrend(candles, PivotBars, Lookback);
        var price = candles[^1].Close;

        var narrow = _analyzer.BuildValueZones(candles, read, price, 0.1m);
        var wide = _analyzer.BuildValueZones(candles, read, price, 1.0m);

        Assert.NotEmpty(narrow);
        Assert.NotEmpty(wide);

        var narrowWidth = narrow.Sum(z => z.High - z.Low);
        var wideWidth = wide.Sum(z => z.High - z.Low);
        Assert.True(wideWidth > narrowWidth, $"Hẹp {narrowWidth:N2} mà rộng chỉ {wideWidth:N2}.");
    }

    [Fact]
    public void Vung_chua_gia_thi_Contains_tra_dung()
    {
        var zone = new HtfValueZone(100m, 110m, [HtfZoneLayer.Ema20, HtfZoneLayer.SwingLevel]);

        Assert.True(zone.Contains(100m));
        Assert.True(zone.Contains(105m));
        Assert.True(zone.Contains(110m));
        Assert.False(zone.Contains(99.99m));
        Assert.False(zone.Contains(110.01m));
        Assert.Equal(105m, zone.Mid);
        Assert.Equal(2, zone.Confluence);
    }
}
