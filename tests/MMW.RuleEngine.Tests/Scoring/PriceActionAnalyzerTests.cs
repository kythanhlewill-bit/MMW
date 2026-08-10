using MMW.Application.Indicators;
using MMW.Application.MarketData.Models;
using MMW.Application.Trading.Scoring;
using MMW.Application.Trading.Structure;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.Scoring;

/// <summary>
/// V2 §2 — <c>PriceActionAnalyzer</c> phải KHÓ kích hoạt.
/// </summary>
/// <remarks>
/// Mẫu hình chỉ là hợp lưu mềm nên một dương tính giả không tự phát ra lệnh nào. Nhưng nó đẩy
/// <c>technical.market_structure</c> từ 3 lên 8 điểm và cộng thêm 2 điểm động lượng — chênh 7
/// trên thang 85, đủ để một setup tầm thường vượt ngưỡng 55. Đó là lý do những ràng buộc dưới
/// đây đáng có test riêng thay vì tin vào mắt người đọc mã.
///
/// Bộ nến nền: 60 nến với <c>High − Low = 2</c> và giá đóng phẳng ⟹ ATR(14) = 2, nên mọi ngưỡng
/// tính theo ATR quy ra con số tròn và đọc được ngay trong test.
/// </remarks>
public class PriceActionAnalyzerTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Mặc định của <c>EngineSetting.PatternMaxAgeBars</c>.</summary>
    private const int MaxAge = 12;

    // ── Phá neckline bằng GIÁ ĐÓNG ──────────────────────────────────────

    [Fact]
    public void Hai_day_chi_ho_tro_long_sau_khi_NEN_DONG_pha_neckline()
    {
        var swings = new FixedSwingDetector(
            Point(20, false, 90m),
            Point(30, true, 120m),
            Point(40, false, 90.5m));
        var analyzer = new PriceActionAnalyzer(swings, new IndicatorService());

        // Neckline = đỉnh cao nhất giữa hai đáy = 101.
        var beforeBreak = analyzer.Analyze(Candles(60, 100m, 101m, 99m), 2, 100m);
        var afterBreak = analyzer.Analyze(
            Candles(60, 100m, 101m, 99m, lastClose: 105m, lastHigh: 106m), 2, 105m);

        Assert.Null(beforeBreak.DoubleBottom);

        // Vừa phá xong ⟹ tuổi 0 ⟹ trọng số đầy đủ.
        Assert.Equal(0, afterBreak.DoubleBottom);
        Assert.True(afterBreak.Supports(TradeDirection.Long, MaxAge));
        Assert.False(afterBreak.Supports(TradeDirection.Short, MaxAge));
    }

    /// <summary>
    /// Giá ticker chạy trong nến KHÔNG được hoàn thành một mẫu hình.
    /// </summary>
    /// <remarks>
    /// Đây là canh parity chạy thật ↔ kiểm thử lịch sử. Chạy thật truyền giá ticker vào
    /// <c>currentPrice</c>, kiểm thử lịch sử truyền giá đóng nến. Nếu mẫu hình đọc
    /// <c>currentPrice</c> thì cùng một chuỗi nến cho hai kết quả khác nhau ở hai môi trường,
    /// và ở chạy thật tín hiệu bật/tắt liên tục suốt 15 phút.
    /// </remarks>
    [Fact]
    public void Gia_ticker_chay_trong_nen_khong_lam_hoan_thanh_mau_hinh()
    {
        var candles = Candles(60, 100m, 101m, 99m);
        var swings = new FixedSwingDetector(
            Point(20, false, 90m),
            Point(30, true, 120m),
            Point(40, false, 90.5m));
        var analyzer = new PriceActionAnalyzer(swings, new IndicatorService());

        // Cùng chuỗi nến, chỉ khác giá ticker: kết luận về mẫu hình phải y hệt nhau.
        var atNeckline = analyzer.Analyze(candles, 2, 100m);
        var farAbove = analyzer.Analyze(candles, 2, 130m);

        Assert.Equal(atNeckline.DoubleBottom, farAbove.DoubleBottom);
        Assert.Null(farAbove.DoubleBottom);
    }

    // ── Hai đáy: thời gian và độ nảy (§2.3) ─────────────────────────────

    [Fact]
    public void Hai_day_qua_gan_nhau_ve_thoi_gian_khong_phai_hai_day()
    {
        // Hai đáy cách nhau 6 nến trên một vùng đi ngang phẳng thoả mọi điều kiện về GIÁ.
        // Đó không phải hai đáy, đó là một cái nền.
        var tooClose = Analyze(Point(20, false, 90m), Point(26, false, 90.3m));
        var farEnough = Analyze(Point(20, false, 90m), Point(28, false, 90.3m));

        Assert.Null(tooClose.DoubleBottom);
        Assert.Equal(0, farEnough.DoubleBottom);
    }

    [Fact]
    public void Hai_day_khong_co_cu_nay_that_chi_la_mot_vung_tich_luy()
    {
        // Đỉnh trung gian giữa hai đáy nằm ở 101 (đỉnh của mọi nến nền).
        // Đáy 100,5 ⟹ nảy 0,5 < 1,0 ATR. Đáy 97,3 ⟹ nảy 3,7 > 1,0 ATR.
        var flat = Analyze(Point(20, false, 100.2m), Point(40, false, 100.5m));
        var real = Analyze(Point(20, false, 97m), Point(40, false, 97.3m));

        Assert.Null(flat.DoubleBottom);
        Assert.Equal(0, real.DoubleBottom);
    }

    // ── Vai-đầu-vai (§2.1, §2.2) ────────────────────────────────────────

    [Fact]
    public void Vai_dau_vai_nguoc_hop_le_duoc_nhan_dien()
    {
        var signals = Analyze(
            Point(10, false, 95m),
            Point(20, false, 92m),
            Point(30, false, 95.2m));

        Assert.Equal(0, signals.InverseHeadAndShoulders);
        Assert.True(signals.Supports(TradeDirection.Long, MaxAge));
    }

    [Fact]
    public void Dau_khong_nho_du_08_ATR_thi_khong_phai_vai_dau_vai()
    {
        // Đầu nhô 1,0 — vượt ngưỡng cũ 0,25 ATR = 0,5 nhưng dưới ngưỡng V2 0,8 ATR = 1,6.
        // Chính đây là nguồn dương tính giả lớn nhất của V1: một đường zigzag bất kỳ.
        var signals = Analyze(
            Point(10, false, 95m),
            Point(20, false, 94m),
            Point(30, false, 95.2m));

        Assert.Null(signals.InverseHeadAndShoulders);
    }

    [Fact]
    public void Hai_vai_lech_qua_05_ATR_thi_khong_phai_vai_dau_vai()
    {
        // ATR ở bộ nến này là 2,36 (nến cuối phá vỡ làm biên độ thật rộng ra).
        // Lệch 1,4 — vẫn dưới dung sai cũ 0,6 ATR = 1,41 nhưng vượt dung sai V2 0,5 ATR = 1,18.
        var signals = Analyze(
            Point(10, false, 95m),
            Point(20, false, 92m),
            Point(30, false, 96.4m));

        Assert.Null(signals.InverseHeadAndShoulders);
    }

    [Fact]
    public void Ba_diem_xoay_dinh_nhau_khong_phai_mau_hinh()
    {
        var signals = Analyze(
            Point(10, false, 95m),
            Point(14, false, 92m),
            Point(18, false, 95.2m));

        Assert.Null(signals.InverseHeadAndShoulders);
    }

    [Fact]
    public void Neckline_doc_qua_05_ATR_thi_khong_phai_vai_dau_vai()
    {
        // Đoạn TRÁI cao 103, đoạn PHẢI cao 101 ⟹ neckline nghiêng 2,0 > 0,5 ATR ≈ 1,04.
        var steep = Candles(60, 100m, 101m, 99m, lastClose: 105m, lastHigh: 106m);
        steep[15] = steep[15] with { High = 103m };

        var signals = new PriceActionAnalyzer(
                new FixedSwingDetector(
                    Point(10, false, 95m),
                    Point(20, false, 92m),
                    Point(30, false, 95.2m)),
                new IndicatorService())
            .Analyze(steep, 2, 105m);

        Assert.Null(signals.InverseHeadAndShoulders);
    }

    /// <summary>
    /// Neckline lấy trên HỢP hai đoạn, không phải chỉ đoạn đầu→vai phải.
    /// </summary>
    /// <remarks>
    /// Lấy một nửa thường cho mức dễ phá hơn ⟹ xác nhận sớm hơn thực tế. Đó là sai lệch một
    /// chiều, và chiều của nó là chiều làm đẹp kết quả kiểm thử.
    /// </remarks>
    [Fact]
    public void Neckline_lay_tren_HOP_hai_doan_chu_khong_chi_nua_phai()
    {
        // Đoạn trái đỉnh 101,8; đoạn phải đỉnh 101. Neckline thật = 101,8.
        var candles = Candles(60, 100m, 101m, 99m, lastClose: 101.4m, lastHigh: 102m);
        candles[15] = candles[15] with { High = 101.8m };

        var swings = new FixedSwingDetector(
            Point(10, false, 95m),
            Point(20, false, 92m),
            Point(30, false, 95.2m));
        var analyzer = new PriceActionAnalyzer(swings, new IndicatorService());

        // Giá đóng 101,4 vượt đoạn PHẢI nhưng chưa vượt neckline thật ⟹ chưa phá.
        Assert.Null(analyzer.Analyze(candles, 2, 101.4m).InverseHeadAndShoulders);

        var broken = Candles(60, 100m, 101m, 99m, lastClose: 102.5m, lastHigh: 103m);
        broken[15] = broken[15] with { High = 101.8m };
        Assert.Equal(0, analyzer.Analyze(broken, 2, 102.5m).InverseHeadAndShoulders);
    }

    // ── Phân kỳ RSI (§2.4) ──────────────────────────────────────────────

    /// <summary>
    /// Phân kỳ ở RSI 50 không nói lên điều gì; phân kỳ từ vùng quá bán mới là kiệt sức.
    /// </summary>
    /// <remarks>
    /// Hai chuỗi giá dưới đây thoả ĐÚNG NHƯ NHAU điều kiện về độ lớn phân kỳ và khoảng cách hai
    /// điểm xoay. Khác biệt duy nhất là RSI tại điểm xoay ĐẦU: chuỗi răng cưa cho ~50, chuỗi giảm
    /// đều cho 0. Nên test này ghim đúng một ràng buộc, không phải ba.
    /// </remarks>
    [Fact]
    public void Phan_ky_RSI_doi_diem_xoay_dau_nam_trong_vung_cuc_tri()
    {
        var swings = new FixedSwingDetector(Point(70, false, 100m), Point(80, false, 99m));
        var analyzer = new PriceActionAnalyzer(swings, new IndicatorService());

        var oscillating = analyzer.Analyze(FromCloses(Oscillate(100, until: 70)), 2, 100m);
        var exhausted = analyzer.Analyze(FromCloses(FallThenRise(100, bottom: 70)), 2, 100m);

        Assert.Null(oscillating.BullishRsiDivergence);
        Assert.NotNull(exhausted.BullishRsiDivergence);
    }

    [Fact]
    public void Phan_ky_giua_hai_diem_xoay_qua_gan_nhau_bi_bo_qua()
    {
        var closes = FallThenRise(100, bottom: 70);

        var tooClose = Analyze(closes, Point(70, false, 100m), Point(73, false, 99m));
        var farEnough = Analyze(closes, Point(70, false, 100m), Point(80, false, 99m));

        Assert.Null(tooClose.BullishRsiDivergence);
        Assert.NotNull(farEnough.BullishRsiDivergence);
    }

    /// <summary>
    /// RSI tại một điểm xoay nằm quá gần đầu cửa sổ chưa hội tụ, nên không dùng được.
    /// </summary>
    /// <remarks>
    /// Làm trơn Wilder là một hồi quy khởi tạo ở đầu chuỗi. Với chỉ số nhỏ, giá trị tính được
    /// lệch đáng kể so với RSI cuộn thật — và nó lệch một cách im lặng: hàm vẫn trả về một số
    /// trông hợp lý.
    /// </remarks>
    [Fact]
    public void Diem_xoay_qua_som_trong_cua_so_khong_du_de_tinh_RSI()
    {
        var closes = FallThenRise(100, bottom: 40);

        var tooEarly = Analyze(closes, Point(38, false, 100m), Point(50, false, 99m));

        Assert.Null(tooEarly.BullishRsiDivergence);
    }

    // ── Fibonacci (§2.6) ────────────────────────────────────────────────

    [Fact]
    public void Fibonacci_chi_la_hop_luu_trong_vung_golden_pocket()
    {
        var candles = Candles(60, 100m, 101m, 99m);
        var swings = new FixedSwingDetector(Point(20, false, 90m), Point(40, true, 120m));
        var analyzer = new PriceActionAnalyzer(swings, new IndicatorService());

        // Nhịp đẩy 90 → 120 (rộng 30). Hồi càng sâu thì giá càng THẤP:
        // hồi 50% ⟹ giá 105, hồi 61,8% ⟹ giá 101,46.
        Assert.True(analyzer.Analyze(candles, 2, 105m).FibonacciLong);
        Assert.True(analyzer.Analyze(candles, 2, 102m).FibonacciLong);

        // Hồi 38,2% ⟹ giá 108,54 — V1 cộng điểm ở đây, V2 thì không: hồi nông như vậy chỉ nói
        // rằng giá chưa đi đâu cả.
        Assert.False(analyzer.Analyze(candles, 2, 108.6m).FibonacciLong);
        Assert.False(analyzer.Analyze(candles, 2, 118m).FibonacciLong);

        // Hồi 66,7% — sâu hơn cả vùng, tức nhịp đẩy đang bị phủ định chứ không phải được kiểm định.
        Assert.False(analyzer.Analyze(candles, 2, 100m).FibonacciLong);
    }

    [Fact]
    public void Nhip_day_nho_hon_15_ATR_khong_sinh_vung_hoi()
    {
        var candles = Candles(60, 100m, 101m, 99m);

        // Nhịp 100 → 102,5 chỉ 1,25 ATR. Mức hồi 50% rơi đúng 101,25 — và nếu không có sàn thì
        // một vùng rộng 0,15 giá sẽ được coi là "vùng hồi Fibonacci".
        var tiny = new PriceActionAnalyzer(
                new FixedSwingDetector(Point(20, false, 100m), Point(40, true, 102.5m)),
                new IndicatorService())
            .Analyze(candles, 2, 101.25m);

        // Cùng hình dạng, nhịp 100 → 106 = 3,0 ATR.
        var real = new PriceActionAnalyzer(
                new FixedSwingDetector(Point(20, false, 100m), Point(40, true, 106m)),
                new IndicatorService())
            .Analyze(candles, 2, 103m);

        Assert.False(tiny.FibonacciLong);
        Assert.True(real.FibonacciLong);
    }

    [Fact]
    public void Fibonacci_duoc_tinh_san_cho_CA_HAI_chieu()
    {
        // Một bản ghi phải dùng được cho cả hai chiều, nếu không §4 phải quét lại từ đầu.
        var candles = Candles(60, 100m, 101m, 99m);
        var signals = new PriceActionAnalyzer(
                new FixedSwingDetector(Point(20, true, 120m), Point(40, false, 90m)),
                new IndicatorService())
            .Analyze(candles, 2, 105m);

        // Nhịp giảm 120 → 90, giá 105 là mức hồi 50%.
        Assert.True(signals.FibonacciShort);
        Assert.Equal(signals.FibonacciShort, signals.FibonacciConfluence(TradeDirection.Short));
        Assert.Equal(signals.FibonacciLong, signals.FibonacciConfluence(TradeDirection.Long));
    }

    /// <summary>
    /// Một nhịp suy biến ở giữa danh sách điểm xoay không được huỷ toàn bộ phần quét còn lại.
    /// </summary>
    [Fact]
    public void Nhip_suy_bien_khong_huy_phan_quet_Fibonacci_con_lai()
    {
        var candles = Candles(60, 100m, 101m, 99m);

        // Nhịp gần nhất (45→50) suy biến: hai điểm xoay cùng giá 110. Nhịp cũ hơn (20→40)
        // là một nhịp tăng thật 90→120, và giá 105 nằm ở mức hồi 50% của nó.
        var swings = new FixedSwingDetector(
            Point(20, false, 90m),
            Point(40, true, 120m),
            Point(45, false, 110m),
            Point(50, true, 110m));
        var analyzer = new PriceActionAnalyzer(swings, new IndicatorService());

        Assert.True(analyzer.Analyze(candles, 2, 105m).FibonacciLong);
    }

    // ── Bậc thang ───────────────────────────────────────────────────────

    [Fact]
    public void Ba_dinh_va_ba_day_cao_dan_duoc_nhan_la_bac_thang_tang()
    {
        var candles = Candles(60, 100m, 102m, 98m);
        var swings = new FixedSwingDetector(
            Point(5, false, 90m), Point(10, true, 105m),
            Point(20, false, 95m), Point(30, true, 110m),
            Point(40, false, 100m), Point(50, true, 115m));
        var signals = new PriceActionAnalyzer(swings, new IndicatorService())
            .Analyze(candles, 2, 108m);

        // Điểm xoay muộn nhất xác nhận ở nến 52; nến cuối là 59 ⟹ tuổi 7.
        Assert.Equal(7, signals.BullishStaircase);
        Assert.Null(signals.BearishStaircase);
        Assert.True(signals.Supports(TradeDirection.Long, MaxAge));
    }

    // ── Tuổi và trọng số (§2.5) ─────────────────────────────────────────

    [Fact]
    public void Trong_so_giam_tuyen_tinh_theo_tuoi_va_bang_0_khi_het_han()
    {
        Assert.Equal(1m, PriceActionSignals.Weight(0, 12));
        Assert.Equal(0.5m, PriceActionSignals.Weight(6, 12));
        Assert.Equal(0m, PriceActionSignals.Weight(12, 12));
        Assert.Equal(0m, PriceActionSignals.Weight(20, 12));
        Assert.Equal(0m, PriceActionSignals.Weight(null, 12));
    }

    /// <summary>
    /// Một mẫu hình đã cũ 8 tiếng không được chấm ngang một mẫu hình vừa hoàn thành.
    /// </summary>
    [Fact]
    public void Mau_hinh_qua_han_khong_con_la_hop_luu()
    {
        var fresh = Signals(doubleBottom: 0);
        var stale = Signals(doubleBottom: 12);

        Assert.Equal(1m, fresh.NetConfluence(TradeDirection.Long, MaxAge));
        Assert.Equal(0m, stale.NetConfluence(TradeDirection.Long, MaxAge));
        Assert.False(stale.Supports(TradeDirection.Long, MaxAge));
        Assert.Empty(stale.SupportingNames(TradeDirection.Long, MaxAge));
    }

    /// <summary>
    /// Bằng chứng thuận và ngược cùng lúc phải triệt tiêu nhau, không phải "thuận thắng".
    /// </summary>
    [Fact]
    public void Hop_luu_rong_bang_khong_khi_bang_chung_hai_chieu_can_nhau()
    {
        var signals = Signals(doubleBottom: 0, bearishRsiDivergence: 0);

        Assert.True(signals.Supports(TradeDirection.Long, MaxAge));
        Assert.True(signals.Opposes(TradeDirection.Long, MaxAge));
        Assert.Equal(0m, signals.NetConfluence(TradeDirection.Long, MaxAge));
        Assert.Equal(0m, signals.NetConfluence(TradeDirection.Short, MaxAge));
    }

    /// <summary>Bằng chứng thuận đã cũ không cân được bằng chứng ngược còn mới.</summary>
    [Fact]
    public void Bang_chung_moi_nang_hon_bang_chung_cu()
    {
        var signals = Signals(doubleBottom: 9, bearishRsiDivergence: 0);

        // Thuận: 1 − 9/12 = 0,25. Ngược: 1,0. Ròng = −0,75.
        Assert.Equal(-0.75m, signals.NetConfluence(TradeDirection.Long, MaxAge));
    }

    // ── Bộ dựng ─────────────────────────────────────────────────────────

    private static PriceActionSignals Analyze(params SwingPoint[] points) =>
        new PriceActionAnalyzer(new FixedSwingDetector(points), new IndicatorService())
            .Analyze(Candles(60, 100m, 101m, 99m, lastClose: 105m, lastHigh: 106m), 2, 105m);

    private static PriceActionSignals Analyze(IReadOnlyList<decimal> closes, params SwingPoint[] points) =>
        new PriceActionAnalyzer(new FixedSwingDetector(points), new IndicatorService())
            .Analyze(FromCloses(closes), 2, closes[^1]);

    private static PriceActionSignals Signals(
        int? doubleBottom = null, int? bearishRsiDivergence = null) => new(
        BullishStaircase: null,
        BearishStaircase: null,
        DoubleBottom: doubleBottom,
        DoubleTop: null,
        InverseHeadAndShoulders: null,
        HeadAndShoulders: null,
        BullishRsiDivergence: null,
        BearishRsiDivergence: bearishRsiDivergence,
        FibonacciLong: false,
        FibonacciShort: false);

    /// <param name="lastClose">Ghi đè giá đóng của nến CUỐI — nến quyết định mẫu hình đã hoàn thành hay chưa.</param>
    private static List<Candle> Candles(
        int count, decimal close, decimal high, decimal low,
        decimal? lastClose = null, decimal? lastHigh = null) =>
        Enumerable.Range(0, count).Select(i =>
        {
            var open = Start.AddMinutes(15 * i);
            var isLast = i == count - 1;
            var c = isLast ? lastClose ?? close : close;
            var h = isLast ? lastHigh ?? high : high;
            return new Candle(open, close, h, low, c, 100m, open.AddMinutes(15).AddTicks(-1));
        }).ToList();

    private static List<Candle> FromCloses(IReadOnlyList<decimal> closes) =>
        closes.Select((p, i) =>
        {
            var open = Start.AddMinutes(15 * i);
            return new Candle(open, p, p + 1m, p - 1m, p, 100m, open.AddMinutes(15).AddTicks(-1));
        }).ToList();

    /// <summary>Răng cưa ±1 tới <paramref name="until"/> rồi tăng đều. RSI tại mốc đó ≈ 50.</summary>
    private static List<decimal> Oscillate(int count, int until) =>
        Enumerable.Range(0, count)
            .Select(i => i <= until ? 100m + i % 2 : 100m + (until % 2) + (i - until))
            .ToList();

    /// <summary>Giảm đều tới <paramref name="bottom"/> rồi tăng đều. RSI tại đáy = 0.</summary>
    private static List<decimal> FallThenRise(int count, int bottom) =>
        Enumerable.Range(0, count)
            .Select(i => i <= bottom ? 200m - i : 200m - bottom + (i - bottom))
            .ToList();

    private static SwingPoint Point(int index, bool high, decimal price)
    {
        var occurred = Start.AddMinutes(15 * index);
        return new SwingPoint(index, high, price, occurred, index + 2, occurred.AddMinutes(30));
    }

    private sealed class FixedSwingDetector(params SwingPoint[] points) : ISwingDetector
    {
        public IReadOnlyList<SwingPoint> Detect(IReadOnlyList<Candle> candles, int pivotBars) => points;
    }
}
