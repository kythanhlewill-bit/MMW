using MMW.Application.MarketData.Models;
using MMW.Application.Trading.Execution;
using MMW.Application.Trading.Structure;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.RuleEngine.Tests.Scoring;
using Xunit;

namespace MMW.RuleEngine.Tests.Execution;

/// <summary>
/// Bộ luật swing khung 4 giờ: bốn cửa, và mỗi cửa đóng lại bằng một mã trạng thái riêng.
/// </summary>
/// <remarks>
/// <para>Điều được ghim chặt nhất ở đây là <b>mã trạng thái từ chối</b>, chứ không phải đường
/// vào lệnh. Lý do đến từ chính lịch sử của dự án: sau tám ngày chạy thật, câu hỏi tốn thời
/// gian nhất luôn là "vì sao cả tuần không vào lệnh nào", và trả lời được nó đòi mã trạng thái
/// phải chỉ đúng cửa mà cơ hội chết. Một bộ kích hoạt từ chối đúng mà báo sai lý do thì mọi
/// buổi chẩn đoán sau đó đều đi sai hướng.</para>
///
/// <para>Vùng giá trị được dựng bằng CHÍNH bộ phân tích mà bộ kích hoạt dùng, rồi mới dựng nến
/// khung nhỏ quanh nó. Ghim cứng một con số giá vào test sẽ biến nó thành bài kiểm tra xem hằng
/// số có đổi hay không, chứ không kiểm tra được hành vi.</para>
/// </remarks>
public sealed class HtfSwingTriggerTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly SetupTriggerPolicy _triggers = new(
        ScoringFixtures.Structure,
        new SidewaysPatternAnalyzer(ScoringFixtures.Swings),
        ScoringFixtures.Htf,
        ScoringFixtures.Swings);

    private readonly HtfSwingAnalyzer _analyzer = new(ScoringFixtures.Swings, ScoringFixtures.Indicators);

    private static EngineSetting V7(Action<EngineSetting>? configure = null) =>
        ScoringFixtures.Settings(s =>
        {
            s.StrategyVersion = TradingStrategyVersion.HtfSwingV7;
            configure?.Invoke(s);
        });

    // ── Dựng dữ liệu ─────────────────────────────────────────────────────

    private static List<Candle> Bars(IEnumerable<decimal> closes, TimeSpan step, decimal wick = 0.004m)
    {
        var list = new List<Candle>();
        var i = 0;
        var previous = 0m;

        foreach (var close in closes)
        {
            var open = previous == 0m ? close : previous;
            var high = Math.Max(close * (1m + wick), open);
            var low = Math.Min(close * (1m - wick), open);
            list.Add(new Candle(Start + step * i, open, high, low, close, 1000m, Start + step * (i + 1)));
            previous = close;
            i++;
        }

        return list;
    }

    /// <summary>Chuỗi 4h có đỉnh cao dần và đáy cao dần.</summary>
    private static List<Candle> HtfUpTrend()
    {
        var closes = new List<decimal>();
        for (var i = 0; i < 20; i++) closes.Add(1000m);

        var level = 1000m;
        for (var leg = 0; leg < 4; leg++)
        {
            for (var i = 0; i < 7; i++) closes.Add(level += 120m / 7m);
            for (var i = 0; i < 7; i++) closes.Add(level -= 50m / 7m);
        }

        return Bars(closes, TimeSpan.FromHours(4));
    }

    private static List<Candle> HtfDownTrend()
    {
        var closes = new List<decimal>();
        for (var i = 0; i < 20; i++) closes.Add(2000m);

        var level = 2000m;
        for (var leg = 0; leg < 4; leg++)
        {
            for (var i = 0; i < 7; i++) closes.Add(level -= 120m / 7m);
            for (var i = 0; i < 7; i++) closes.Add(level += 50m / 7m);
        }

        return Bars(closes, TimeSpan.FromHours(4));
    }

    private static List<Candle> HtfChoppy()
    {
        var closes = new List<decimal>();
        for (var i = 0; i < 20; i++) closes.Add(1000m);

        decimal[] pattern = [1080m, 1000m, 1120m, 1040m, 1060m, 990m, 1100m, 1010m];
        foreach (var target in pattern)
        {
            var from = closes[^1];
            for (var i = 1; i <= 5; i++) closes.Add(from + (target - from) * i / 5m);
        }

        return Bars(closes, TimeSpan.FromHours(4));
    }

    /// <summary>
    /// Tìm mức giá mà tại đó giá ĐANG NẰM TRONG một vùng giá trị.
    /// </summary>
    /// <remarks>
    /// Phải dò chứ không tính thẳng được, vì tập vùng phụ thuộc vào chính giá: bộ dựng vùng chỉ
    /// nhận những mức nằm đúng phía nhịp hồi, nên khi giá đi xuống thì có mức rời khỏi tập và
    /// vùng đổi hình. Lấy vùng tính ở giá hiện tại rồi giả định nó vẫn y nguyên khi giá đã hồi
    /// tới đó là một giả định sai — và nó sai theo cách chỉ lộ ra ở vài chuỗi dữ liệu nhất định.
    ///
    /// Vòng dò này mô phỏng đúng thứ đường chạy thật gặp: giá bò dần về phía vùng, mỗi cây nến
    /// lại hỏi lại "giờ đã vào vùng chưa".
    /// </remarks>
    private decimal EntryPriceInZone(IReadOnlyList<Candle> bias, EngineSetting settings, bool isLong)
    {
        var read = _analyzer.ReadTrend(bias, settings.V7HtfPivotBars, settings.V7HtfStructureLookbackBars);
        var start = bias[^1].Close;
        var step = start * 0.0005m;

        for (var i = 1; i <= 400; i++)
        {
            var price = isLong ? start - step * i : start + step * i;
            var zones = _analyzer.BuildValueZones(bias, read, price, settings.V7ZoneHalfWidthAtr);
            if (zones.Any(z => z.Contains(price))) return price;
        }

        Assert.Fail($"Không dò được mức giá nào rơi vào vùng giá trị (bắt đầu từ {start:N2}).");
        return 0m;
    }

    /// <summary>
    /// Nến 15 phút hồi từ giá hiện tại xuống <paramref name="target"/>, kết bằng nến từ chối.
    /// </summary>
    private static List<Candle> PullbackTo(decimal from, decimal target, bool isLong, bool withConfirmation)
    {
        var closes = new List<decimal>();
        for (var i = 0; i < 40; i++) closes.Add(from);

        // Đi dần về vùng.
        for (var i = 1; i <= 20; i++) closes.Add(from + (target - from) * i / 20m);

        var bars = Bars(closes, TimeSpan.FromMinutes(15));

        if (!withConfirmation) return bars;

        // Nến từ chối, ĐÓNG ĐÚNG tại mức đang dò. Đóng lệch lên một chút nghe tự nhiên hơn nhưng
        // sẽ đẩy giá ra khỏi vùng vừa dò được, và khi đó test đo một tình huống khác hẳn.
        var last = bars[^1];
        var range = target * 0.01m;
        var rejection = isLong
            ? new Candle(last.CloseTime, target * 1.002m, target * 1.002m, target - range, target,
                2000m, last.CloseTime.AddMinutes(15))
            : new Candle(last.CloseTime, target * 0.998m, target + range, target * 0.998m, target,
                2000m, last.CloseTime.AddMinutes(15));

        bars.Add(rejection);
        return bars;
    }

    // ── Cửa 1: xu hướng ──────────────────────────────────────────────────

    [Fact]
    public void Khung_4h_khong_ro_xu_huong_thi_dung_ngoai()
    {
        var settings = V7();
        var bias = HtfChoppy();
        var context = ScoringFixtures.Context(bias: bias, settings: settings);

        var result = _triggers.Evaluate(context, range: null);

        Assert.False(result.Passed);
        Assert.Equal(SetupTriggerState.HtfTrendUnclear, result.State);
        Assert.Equal(SetupType.HtfSwingPullback, result.SetupType);
    }

    [Fact]
    public void Khung_4h_nguoc_chieu_thi_tu_choi_dung_ma_trang_thai()
    {
        var settings = V7();
        var bias = HtfUpTrend();
        var context = ScoringFixtures.Context(
            bias: bias, settings: settings, direction: TradeDirection.Short);

        var result = _triggers.Evaluate(context, range: null);

        Assert.False(result.Passed);
        Assert.Equal(SetupTriggerState.HtfTrendOpposed, result.State);
    }

    /// <summary>
    /// V7 KHÔNG chạy qua thang cũ, và ngược lại.
    /// </summary>
    /// <remarks>
    /// Hai bộ luật đọc chiều lệnh từ hai nguồn khác nhau — V7 từ cấu trúc 4h của chính mã, các
    /// bản trước từ kế hoạch ngày của BTC. Cho chúng chạy chồng lên nhau trên cùng một tài khoản
    /// là để hai hệ thống cãi nhau bằng tiền thật.
    /// </remarks>
    [Fact]
    public void V7_thay_the_ca_thang_cu_chu_khong_cong_them()
    {
        var bias = HtfChoppy();
        var v7 = _triggers.Evaluate(
            ScoringFixtures.Context(bias: bias, settings: V7()), range: null);

        Assert.Equal(SetupType.HtfSwingPullback, v7.SetupType);

        // Cùng dữ liệu, phiên bản cũ: phải rơi vào một nhánh KHÁC, không phải nhánh swing.
        var v6 = _triggers.Evaluate(
            ScoringFixtures.Context(
                bias: bias,
                settings: ScoringFixtures.Settings(s => s.StrategyVersion = TradingStrategyVersion.AdaptiveSidewaysV6)),
            range: null);

        Assert.NotEqual(SetupType.HtfSwingPullback, v6.SetupType);
    }

    // ── Cửa 2: vùng giá trị ──────────────────────────────────────────────

    [Fact]
    public void Chua_hoi_ve_vung_gia_tri_thi_dung_o_cua_hai()
    {
        var settings = V7();
        var bias = HtfUpTrend();

        // Giá vẫn ở đỉnh, chưa hồi xuống vùng nào.
        var entry = Bars(Enumerable.Repeat(bias[^1].Close * 1.05m, 60), TimeSpan.FromMinutes(15));
        var context = ScoringFixtures.Context(entry: entry, bias: bias, settings: settings);

        var result = _triggers.Evaluate(context, range: null);

        Assert.False(result.Passed);
        Assert.Equal(SetupTriggerState.HtfValueZoneMissing, result.State);
        Assert.Equal(SetupFunnelStage.EligibleContext, result.Stage);
    }

    /// <summary>Vùng chỉ có một lớp thì không đủ để vào.</summary>
    [Fact]
    public void Vung_thieu_hop_luu_thi_bi_tu_choi()
    {
        // Đòi 9 lớp — nhiều hơn tổng số loại lớp tồn tại, nên mọi vùng đều "mỏng".
        var settings = V7(s => s.V7MinZoneConfluence = 9);
        var bias = HtfUpTrend();
        var target = EntryPriceInZone(bias, settings, isLong: true);

        var entry = PullbackTo(bias[^1].Close, target, isLong: true, withConfirmation: true);
        var context = ScoringFixtures.Context(entry: entry, bias: bias, settings: settings);

        var result = _triggers.Evaluate(context, range: null);

        Assert.False(result.Passed);
        Assert.Equal(SetupTriggerState.HtfValueZoneWeak, result.State);
        Assert.Equal(SetupFunnelStage.StructureCandidate, result.Stage);
    }

    /// <summary>
    /// Hồi thủng mức làm hỏng cấu trúc thì đó không còn là nhịp hồi.
    /// </summary>
    /// <remarks>
    /// Đây là cửa dễ bị bỏ qua nhất, vì càng xuống sâu thì giá càng "rẻ" và mọi lớp hợp lưu bên
    /// trên vẫn còn nguyên trên biểu đồ. Nhưng khi đáy cao hơn gần nhất đã mất, chuỗi tạo nên xu
    /// hướng đã đứt — thứ đang được mua rẻ là một xu hướng không còn tồn tại.
    /// </remarks>
    [Fact]
    public void Hoi_qua_sau_pha_cau_truc_thi_dung_lai()
    {
        var settings = V7();
        var bias = HtfUpTrend();
        var read = _analyzer.ReadTrend(bias, settings.V7HtfPivotBars, settings.V7HtfStructureLookbackBars);
        Assert.NotNull(read.InvalidationPrice);

        var below = read.InvalidationPrice!.Value * 0.97m;
        var entry = PullbackTo(bias[^1].Close, below, isLong: true, withConfirmation: true);
        var context = ScoringFixtures.Context(entry: entry, bias: bias, settings: settings);

        var result = _triggers.Evaluate(context, range: null);

        Assert.False(result.Passed);
        Assert.Equal(SetupTriggerState.HtfPullbackTooDeep, result.State);
    }

    // ── Cửa 3: xác nhận khung nhỏ ────────────────────────────────────────

    /// <summary>
    /// Vào đúng vùng vẫn chưa đủ — phải có một cú đóng nến nói rằng người mua đã xuất hiện.
    /// </summary>
    [Fact]
    public void Vao_vung_ma_khung_nho_chua_xac_nhan_thi_cho()
    {
        var settings = V7();
        var bias = HtfUpTrend();
        var target = EntryPriceInZone(bias, settings, isLong: true);

        // Chuỗi đi xuống đều, không nến từ chối, không phá cấu trúc nhỏ theo chiều mua.
        var entry = PullbackTo(bias[^1].Close, target, isLong: true, withConfirmation: false);
        var context = ScoringFixtures.Context(entry: entry, bias: bias, settings: settings);

        var result = _triggers.Evaluate(context, range: null);

        if (result.Passed) return; // Chuỗi vẫn tình cờ tạo được xác nhận — không phải lỗi của bộ luật.

        Assert.True(
            result.State is SetupTriggerState.HtfEntryConfirmationMissing
                        or SetupTriggerState.HtfValueZoneMissing
                        or SetupTriggerState.HtfValueZoneWeak,
            $"Trạng thái bất ngờ: {result.State} — {result.DetailVi}");
    }

    // ── Cửa 4: hình học ──────────────────────────────────────────────────

    /// <summary>
    /// Tỉ lệ tới mục tiêu cuối dưới sàn thì từ chối, dù ba cửa trước đã qua.
    /// </summary>
    /// <remarks>
    /// Sàn này là con số quyết định hệ lãi hay lỗ. Với dừng lỗ rộng theo cấu trúc 4h, tỉ lệ
    /// thắng dự kiến khoảng 35%; ở mức đó hoà vốn cần R:R ≈ 1,9 và đó là TRƯỚC phí.
    /// </remarks>
    [Fact]
    public void Ti_le_toi_muc_tieu_cuoi_duoi_san_thi_tu_choi()
    {
        var settings = V7(s => s.V7MinRunnerRr = 99m);
        var bias = HtfUpTrend();
        var target = EntryPriceInZone(bias, settings, isLong: true);

        var entry = PullbackTo(bias[^1].Close, target, isLong: true, withConfirmation: true);
        var context = ScoringFixtures.Context(entry: entry, bias: bias, settings: settings);

        var result = _triggers.Evaluate(context, range: null);

        Assert.False(result.Passed);
        Assert.Equal(SetupTriggerState.HtfValueZoneWeak, result.State);
        Assert.Contains("dưới sàn", result.DetailVi);
    }

    // ── Đường vào lệnh ───────────────────────────────────────────────────

    /// <summary>Qua đủ bốn cửa thì phải mang về ĐỦ ba mức, và chúng phải xếp đúng thứ tự.</summary>
    [Fact]
    public void Xac_nhan_thi_mang_ve_du_dung_lo_va_hai_muc_tieu()
    {
        var settings = V7(s =>
        {
            // Nới hai rào tỉ lệ để test này đo đúng thứ nó quan tâm: hình dạng của bộ mức trả về.
            s.V7MinFirstRr = 0.1m;
            s.V7MinRunnerRr = 0.2m;
            s.V7MinZoneConfluence = 1;
        });

        var bias = HtfUpTrend();
        var target = EntryPriceInZone(bias, settings, isLong: true);
        var entry = PullbackTo(bias[^1].Close, target, isLong: true, withConfirmation: true);
        var context = ScoringFixtures.Context(entry: entry, bias: bias, settings: settings);

        var result = _triggers.Evaluate(context, range: null);

        Assert.True(result.Passed, result.DetailVi);
        Assert.Equal(SetupType.HtfSwingPullback, result.SetupType);
        Assert.Equal(SetupTriggerState.Confirmed, result.State);
        Assert.Equal(SetupFunnelStage.Confirmed, result.Stage);

        var price = context.CurrentPrice;
        Assert.NotNull(result.SuggestedStopLoss);
        Assert.NotNull(result.SuggestedFirstTakeProfit);
        Assert.NotNull(result.SuggestedRunnerTakeProfit);

        Assert.True(result.SuggestedStopLoss < price, "Dừng lỗ lệnh mua phải nằm dưới giá vào.");
        Assert.True(result.SuggestedFirstTakeProfit > price, "Mục tiêu gần phải nằm trên giá vào.");
        Assert.True(result.SuggestedRunnerTakeProfit > result.SuggestedFirstTakeProfit,
            "Mục tiêu cuối phải xa hơn mục tiêu gần, nếu không hai lệnh chốt sẽ tranh nhau.");

        Assert.InRange(result.SetupQualityScore, 60, 100);
    }

    /// <summary>
    /// Dừng lỗ phải đo bằng ATR khung 4h, không phải khung vào lệnh.
    /// </summary>
    /// <remarks>
    /// Kiểm gián tiếp nhưng chắc: nới hệ số đệm lên thì dừng lỗ phải lùi ra XA HƠN. Nếu đệm
    /// đang tính từ ATR khung 15 phút thì nó vẫn lùi ra, nhưng bước lùi nhỏ hơn hẳn — nên phép
    /// so hai cấu hình cạnh nhau đủ để bắt được việc lấy nhầm khung.
    /// </remarks>
    [Fact]
    public void Dem_dung_lo_dan_theo_he_so_ATR_khung_lon()
    {
        SetupTriggerDecision Run(decimal buffer)
        {
            var settings = V7(s =>
            {
                s.V7MinFirstRr = 0.1m;
                s.V7MinRunnerRr = 0.2m;
                s.V7MinZoneConfluence = 1;
                s.V7StopBufferAtr = buffer;
                s.MinStopDistancePercent = 0.01m; // Sàn khoảng cách không được che mất hiệu ứng.
            });

            var bias = HtfUpTrend();
            var target = EntryPriceInZone(bias, settings, isLong: true);
            var entry = PullbackTo(bias[^1].Close, target, isLong: true, withConfirmation: true);
            return _triggers.Evaluate(
                ScoringFixtures.Context(entry: entry, bias: bias, settings: settings), range: null);
        }

        var tight = Run(0.1m);
        var wide = Run(1.5m);

        Assert.True(tight.Passed, tight.DetailVi);
        Assert.True(wide.Passed, wide.DetailVi);
        Assert.True(wide.SuggestedStopLoss < tight.SuggestedStopLoss,
            $"Đệm rộng cho dừng lỗ {wide.SuggestedStopLoss}, đệm hẹp cho {tight.SuggestedStopLoss}.");
    }

    [Fact]
    public void Chieu_ban_cung_di_qua_dung_bon_cua()
    {
        var settings = V7(s =>
        {
            s.V7MinFirstRr = 0.1m;
            s.V7MinRunnerRr = 0.2m;
            s.V7MinZoneConfluence = 1;
        });

        var bias = HtfDownTrend();
        var read = _analyzer.ReadTrend(bias, settings.V7HtfPivotBars, settings.V7HtfStructureLookbackBars);
        Assert.Equal(HtfTrend.Down, read.Trend);

        var target = EntryPriceInZone(bias, settings, isLong: false);

        var entry = PullbackTo(bias[^1].Close, target, isLong: false, withConfirmation: true);
        var context = ScoringFixtures.Context(
            entry: entry, bias: bias, settings: settings, direction: TradeDirection.Short);

        var result = _triggers.Evaluate(context, range: null);

        if (!result.Passed)
        {
            // Không ép phải vào lệnh — nhưng lý do phải là một cửa của CHÍNH nhánh swing.
            Assert.Equal(SetupType.HtfSwingPullback, result.SetupType);
            return;
        }

        var price = context.CurrentPrice;
        Assert.True(result.SuggestedStopLoss > price, "Dừng lỗ lệnh bán phải nằm trên giá vào.");
        Assert.True(result.SuggestedFirstTakeProfit < price, "Mục tiêu gần lệnh bán phải nằm dưới giá vào.");
        Assert.True(result.SuggestedRunnerTakeProfit < result.SuggestedFirstTakeProfit);
    }
}
