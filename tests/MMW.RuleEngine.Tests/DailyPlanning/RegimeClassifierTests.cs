using MMW.Application.MarketData.Models;
using MMW.Application.Trading.DailyPlanning;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.DailyPlanning;

/// <summary>
/// T066 / FR-019 — bảng ánh xạ từ trạng thái ngày sang tham số, mỗi dòng một test.
/// </summary>
/// <remarks>
/// Bảng được kiểm ở <see cref="RegimeTable.Resolve"/> chứ không kiểm gián tiếp qua nến. Dựng
/// một chuỗi nến vừa cho ra đúng cấu trúc mong muốn vừa cho ra đúng phân vị biến động mong
/// muốn là việc làm được nhưng mong manh: test sẽ đỏ vì bộ dữ liệu lệch một chút, chứ không
/// phải vì bảng sai — và đó là loại test dạy người ta bỏ qua nó.
///
/// Phần nối từ nến sang cấu trúc và sang vùng biến động được kiểm riêng ở cuối tệp, mỗi test
/// chỉ khẳng định tính chất mà bộ dữ liệu của nó thực sự chi phối.
/// </remarks>
public class RegimeClassifierTests
{
    private static readonly IDayRegimeClassifier Classifier = new DayRegimeClassifier(new Application.Trading.Structure.SwingDetector(), new Application.Indicators.IndicatorService());

    // ── Bảng FR-019, năm dòng ───────────────────────────────────────────

    [Fact]
    public void Dong_1_xu_huong_tang_va_dao_dong_binh_thuong()
    {
        var r = RegimeTable.Resolve(DayStructure.TrendUp, VolatilityRegime.Normal, hasHighImpactEvent: false);

        Assert.Equal(AllowedDirections.LongOnly, r.AllowedDirections);
        Assert.Equal(1.0m, r.RiskMultiplier);
        Assert.Equal(5, r.MaxTradesToday);
    }

    [Fact]
    public void Dong_2_xu_huong_giam_va_dao_dong_binh_thuong()
    {
        var r = RegimeTable.Resolve(DayStructure.TrendDown, VolatilityRegime.Normal, hasHighImpactEvent: false);

        Assert.Equal(AllowedDirections.ShortOnly, r.AllowedDirections);
        Assert.Equal(1.0m, r.RiskMultiplier);
        Assert.Equal(5, r.MaxTradesToday);
    }

    [Fact]
    public void Dong_3_di_ngang_va_dao_dong_thap()
    {
        var r = RegimeTable.Resolve(DayStructure.Range, VolatilityRegime.Low, hasHighImpactEvent: false);

        Assert.Equal(AllowedDirections.Both, r.AllowedDirections);
        Assert.Equal(0.5m, r.RiskMultiplier);
        Assert.Equal(3, r.MaxTradesToday);
    }

    [Fact]
    public void Dong_4_bat_ky_cau_truc_nao_cong_dao_dong_cuc_doan()
    {
        var r = RegimeTable.Resolve(DayStructure.Range, VolatilityRegime.Extreme, hasHighImpactEvent: false);

        Assert.Equal(AllowedDirections.Both, r.AllowedDirections);
        Assert.Equal(0.3m, r.RiskMultiplier);
        Assert.Equal(2, r.MaxTradesToday);
    }

    [Fact]
    public void Dong_5_ngay_co_su_kien_tac_dong_cao()
    {
        var r = RegimeTable.Resolve(DayStructure.Range, VolatilityRegime.Normal, hasHighImpactEvent: true);

        Assert.Equal(AllowedDirections.Both, r.AllowedDirections);
        Assert.Equal(0.4m, r.RiskMultiplier);
        Assert.Equal(2, r.MaxTradesToday);
    }

    // ── Dòng nền: ngày trend chỉ được đánh một chiều, bất kể biến động ──

    [Theory]
    [InlineData(VolatilityRegime.Low)]
    [InlineData(VolatilityRegime.Normal)]
    [InlineData(VolatilityRegime.High)]
    [InlineData(VolatilityRegime.Extreme)]
    public void Ngay_xu_huong_tang_khong_bao_gio_cho_ban(VolatilityRegime vol)
    {
        // Ràng buộc đã chốt với người dùng: ngày trend chỉ vào MỘT chiều thuận trend.
        // Bảng FR-019 chỉ nói về chiều ở hai dòng đầu, nên nếu không có dòng nền theo cấu
        // trúc thì ngày "tăng + biến động cao" sẽ rơi ra ngoài bảng và cho phép cả hai chiều.
        var r = RegimeTable.Resolve(DayStructure.TrendUp, vol, hasHighImpactEvent: false);

        Assert.Equal(AllowedDirections.LongOnly, r.AllowedDirections);
    }

    [Fact]
    public void Ngay_di_ngang_dao_dong_binh_thuong_khong_khop_dong_nao_thi_dung_dong_nen()
    {
        var r = RegimeTable.Resolve(DayStructure.Range, VolatilityRegime.Normal, hasHighImpactEvent: false);

        Assert.Equal(AllowedDirections.Both, r.AllowedDirections);
        Assert.Equal(1.0m, r.RiskMultiplier);
        Assert.Equal(5, r.MaxTradesToday);
    }

    // ── Vùng biến động theo phân vị ─────────────────────────────────────

    [Theory]
    [InlineData(0, VolatilityRegime.Low)]
    [InlineData(24.9, VolatilityRegime.Low)]
    [InlineData(25, VolatilityRegime.Normal)]
    [InlineData(50, VolatilityRegime.Normal)]
    [InlineData(75, VolatilityRegime.Normal)]
    [InlineData(75.1, VolatilityRegime.High)]
    [InlineData(90, VolatilityRegime.High)]
    [InlineData(90.1, VolatilityRegime.Extreme)]
    [InlineData(100, VolatilityRegime.Extreme)]
    public void Phan_vi_anh_xa_dung_vung_bien_dong(double percentile, VolatilityRegime expected)
    {
        Assert.Equal(expected, VolatilityBands.From((decimal)percentile));
    }

    [Fact]
    public void Thieu_phan_vi_thi_mac_dinh_binh_thuong_chu_khong_cuc_doan()
    {
        // Mặc định Extreme nghe có vẻ an toàn hơn, nhưng nó biến "thiếu dữ liệu" thành
        // "0.3 hệ số mỗi ngày" và trader sẽ tắt hệ thống. Phần phạt thiếu dữ liệu đã có
        // đường riêng ở bước 5 (trần 0.5).
        Assert.Equal(VolatilityRegime.Normal, VolatilityBands.From(null));
    }

    // ── Nối từ nến sang cấu trúc ────────────────────────────────────────

    [Theory]
    [InlineData(nameof(DailyPlanFixtures.UptrendPath), DayStructure.TrendUp)]
    [InlineData(nameof(DailyPlanFixtures.DowntrendPath), DayStructure.TrendDown)]
    [InlineData(nameof(DailyPlanFixtures.RangePath), DayStructure.Range)]
    public void Doc_dung_cau_truc_tu_chuoi_nen(string pathName, DayStructure expected)
    {
        var path = pathName switch
        {
            nameof(DailyPlanFixtures.UptrendPath) => DailyPlanFixtures.UptrendPath,
            nameof(DailyPlanFixtures.DowntrendPath) => DailyPlanFixtures.DowntrendPath,
            _ => DailyPlanFixtures.RangePath,
        };

        var result = Classifier.Classify(Inputs(DailyPlanFixtures.ZigZag(path)), DailyPlanFixtures.Settings());

        Assert.Equal(expected.ToString(), result.BtcStructure);
    }

    [Fact]
    public void Ngay_xu_huong_tang_chi_cho_mua()
    {
        var result = Classifier.Classify(
            Inputs(DailyPlanFixtures.ZigZag(DailyPlanFixtures.UptrendPath)), DailyPlanFixtures.Settings());

        Assert.Equal(AllowedDirections.LongOnly, result.AllowedDirections);
    }

    [Fact]
    public void Ngay_xu_huong_giam_chi_cho_ban()
    {
        var result = Classifier.Classify(
            Inputs(DailyPlanFixtures.ZigZag(DailyPlanFixtures.DowntrendPath)), DailyPlanFixtures.Settings());

        Assert.Equal(AllowedDirections.ShortOnly, result.AllowedDirections);
    }

    // ── Nối từ nến sang vùng biến động ──────────────────────────────────

    [Fact]
    public void Bien_do_gan_day_lon_hon_han_lich_su_thi_la_cuc_doan()
    {
        // 79 phiên biên độ 10, rồi 25 phiên biên độ 100. Giá trị hiện tại là lớn nhất chuỗi.
        var candles = DailyPlanFixtures.FlatClose(
            Enumerable.Repeat(10m, 79).Concat(Enumerable.Repeat(100m, 25)));

        var result = Classifier.Classify(Inputs(candles), DailyPlanFixtures.Settings());

        Assert.Equal(VolatilityRegime.Extreme, result.Volatility);
        Assert.DoesNotContain(DailyPlanInputNames.AtrPercentile, result.MissingInputs);
    }

    [Fact]
    public void Bien_do_gan_day_nho_hon_han_lich_su_thi_la_thap()
    {
        var candles = DailyPlanFixtures.FlatClose(
            Enumerable.Repeat(100m, 79).Concat(Enumerable.Repeat(10m, 25)));

        var result = Classifier.Classify(Inputs(candles), DailyPlanFixtures.Settings());

        Assert.Equal(VolatilityRegime.Low, result.Volatility);
    }

    [Fact]
    public void Bien_do_gan_day_nam_giua_lich_su_thi_la_binh_thuong()
    {
        // Cần BA mức chứ không phải hai. ATR làm trơn Wilder đơn điệu bên trong mỗi khối biên
        // độ không đổi, nên chuỗi hai mức kết thúc ở mức thấp luôn cho giá trị hiện tại là
        // NHỎ NHẤT chuỗi — tức luôn ra "thấp", không bao giờ ra "bình thường".
        //
        // Bố cục: 50 phiên yên (10), 20 phiên dữ dội (60), 34 phiên vừa (20). Giá trị hiện tại
        // hạ về khoảng 22, nằm trên toàn bộ khối yên và dưới toàn bộ khối dữ dội.
        var candles = DailyPlanFixtures.FlatClose(
            Enumerable.Repeat(10m, 50)
                .Concat(Enumerable.Repeat(60m, 20))
                .Concat(Enumerable.Repeat(20m, 34)));

        var result = Classifier.Classify(Inputs(candles), DailyPlanFixtures.Settings());

        Assert.Equal(VolatilityRegime.Normal, result.Volatility);
        Assert.InRange(result.AtrPercentile!.Value, VolatilityBands.LowBelow, VolatilityBands.HighAbove);
    }

    [Fact]
    public void Chuoi_qua_ngan_thi_khong_co_phan_vi_va_bao_thieu()
    {
        var result = Classifier.Classify(
            Inputs(DailyPlanFixtures.FlatClose(Enumerable.Repeat(10m, 40))), DailyPlanFixtures.Settings());

        Assert.Null(result.AtrPercentile);
        Assert.Equal(VolatilityRegime.Normal, result.Volatility);
        Assert.Contains(DailyPlanInputNames.AtrPercentile, result.MissingInputs);
    }

    // ── Ngày có tin ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(MacroEventImpact.Low, false)]
    [InlineData(MacroEventImpact.Medium, false)]
    [InlineData(MacroEventImpact.High, true)]
    [InlineData(MacroEventImpact.Critical, true)]
    public void Chi_su_kien_tu_muc_cao_tro_len_moi_lam_ngay_thanh_ngay_tin(
        MacroEventImpact impact, bool expectEventDay)
    {
        var inputs = Inputs(DailyPlanFixtures.ZigZag(DailyPlanFixtures.RangePath)) with
        {
            TodayEvents = new[] { DailyPlanFixtures.Event(impact) },
        };

        var result = Classifier.Classify(inputs, DailyPlanFixtures.Settings());

        Assert.Equal(expectEventDay, result.MaxTradesToday == 2);
    }

    // ── Bất biến 1 và 2 của contract ────────────────────────────────────

    [Fact]
    public void Cung_dau_vao_cho_cung_ket_qua()
    {
        var inputs = Inputs(DailyPlanFixtures.ZigZag(DailyPlanFixtures.UptrendPath));
        var settings = DailyPlanFixtures.Settings();

        var a = Classifier.Classify(inputs, settings);
        var b = Classifier.Classify(inputs, settings);

        // So từng thành phần chứ không dùng `Assert.Equal(a, b)`: record so sánh
        // `IReadOnlyList<string>` theo THAM CHIẾU, nên hai lần gọi luôn khác nhau vì tạo ra
        // hai đối tượng danh sách khác nhau. Đó là đặc điểm của ngôn ngữ, không phải phát biểu
        // về nghiệp vụ — thứ cần khẳng định là mọi giá trị đều trùng.
        Assert.Equal(a.Regime, b.Regime);
        Assert.Equal(a.Volatility, b.Volatility);
        Assert.Equal(a.AllowedDirections, b.AllowedDirections);
        Assert.Equal(a.RiskMultiplier, b.RiskMultiplier);
        Assert.Equal(a.MaxTradesToday, b.MaxTradesToday);
        Assert.Equal(a.BtcStructure, b.BtcStructure);
        Assert.Equal(a.AtrPercentile, b.AtrPercentile);
        Assert.Equal(a.MissingInputs, b.MissingInputs);
    }

    [Fact]
    public void Bien_dong_cuc_doan_thi_he_so_rui_ro_khong_qua_0_3()
    {
        var candles = DailyPlanFixtures.FlatClose(
            Enumerable.Repeat(10m, 79).Concat(Enumerable.Repeat(100m, 25)));

        var result = Classifier.Classify(Complete(candles), DailyPlanFixtures.Settings());

        Assert.Equal(VolatilityRegime.Extreme, result.Volatility);
        Assert.True(result.RiskMultiplier <= 0.3m, $"Hệ số {result.RiskMultiplier} vượt trần 0.3 của ngày cực đoan.");
    }

    [Fact]
    public void Khong_cho_chieu_nao_thi_so_lenh_toi_da_phai_bang_khong()
    {
        // Bất biến 5. Hiện chưa có tổ hợp nào cho ra None, nhưng bất biến phải được cưỡng chế
        // ở nơi tính chứ không phải dựa vào việc "hiện chưa xảy ra".
        var r = RegimeTable.Resolve(DayStructure.TrendUp, VolatilityRegime.Normal, hasHighImpactEvent: false)
            with { AllowedDirections = AllowedDirections.None };

        Assert.Equal(0, RegimeTable.EnforceNoDirectionMeansNoTrades(r).MaxTradesToday);
    }

    // ── Trợ giúp ────────────────────────────────────────────────────────

    /// <summary>Đầu vào thiếu toàn bộ nguồn tuỳ chọn — dùng cho test chỉ quan tâm cấu trúc.</summary>
    private static DailyPlanInputs Inputs(IReadOnlyList<Candle> btc) => new()
    {
        BtcDailyCandles = btc,
        SymbolDailyCandles = btc,
        TodayEvents = Array.Empty<ScheduledEvent>(),
    };

    /// <summary>Đầu vào đủ mọi nguồn — không bị phạt thiếu dữ liệu.</summary>
    private static DailyPlanInputs Complete(IReadOnlyList<Candle> btc) => Inputs(btc) with
    {
        FundingRate = 0.0001m,
        OpenInterestChange24hPercent = 1.2m,
        LongShortAccountRatio = 1.1m,
        FearGreedIndex = 55,
    };
}
