using MMW.Application.Indicators;
using MMW.Application.Trading.DailyPlanning;
using MMW.Application.Trading.Structure;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.DailyPlanning;

/// <summary>
/// Vùng biến động CAO (phân vị 75–90) phải bị siết, không được rơi vào dòng nền.
/// </summary>
/// <remarks>
/// Bảng FR-019 xử lý hai đầu cực đoan — <c>Extreme</c> và ngày có tin — nhưng khoảng 75–90 trước
/// đây không khớp dòng nào và nhận trọn tham số của dòng nền: rủi ro 1.0 và 5 lệnh, y hệt một
/// ngày yên bình.
///
/// Đó là khoảng nguy hiểm nhất với khung giữ lệnh 1–4 tiếng: biên độ đã đủ lớn để nến chọc thủng
/// mọi mức kỹ thuật, nhưng chưa đủ lớn để bị gọi là cực đoan và tự thu nhỏ.
/// </remarks>
public class HighVolatilityGapTests
{
    private static readonly IDayRegimeClassifier Classifier =
        new DayRegimeClassifier(new SwingDetector(), new IndicatorService());

    [Fact]
    public void Bien_dong_cao_bi_siet_rui_ro_va_so_lenh()
    {
        var r = RegimeTable.Resolve(DayStructure.Range, VolatilityRegime.High, hasHighImpactEvent: false);

        Assert.Equal(0.6m, r.RiskMultiplier);
        Assert.Equal(RegimeTable.ObservationMaxTradesPerDay, r.MaxTradesToday);
    }

    /// <summary>
    /// Ngày trend + biến động cao vẫn giữ ràng buộc một chiều, và nay bị siết rủi ro.
    /// </summary>
    [Fact]
    public void Ngay_trend_bien_dong_cao_giu_mot_chieu_nhung_nho_lai()
    {
        var r = RegimeTable.Resolve(DayStructure.TrendUp, VolatilityRegime.High, hasHighImpactEvent: false);

        Assert.Equal(AllowedDirections.LongOnly, r.AllowedDirections);
        Assert.Equal(0.6m, r.RiskMultiplier);
        Assert.Equal(RegimeTable.ObservationMaxTradesPerDay, r.MaxTradesToday);
    }

    /// <summary>
    /// Dòng mới KHÔNG được nới lỏng vùng cực đoan.
    /// </summary>
    /// <remarks>
    /// <c>Resolve</c> hợp nhất bằng <c>Math.Min</c> nên về lý thuyết không dòng nào nới được gì.
    /// Vẫn ghim lại: một lần ai đó đổi phép hợp nhất sang "dòng khớp cuối cùng thắng" thì vùng
    /// cực đoan sẽ âm thầm nhảy từ 0.3 lên 0.6 và không có test nào khác bắt được.
    /// </remarks>
    [Fact]
    public void Vung_cuc_doan_khong_bi_dong_moi_noi_long()
    {
        var r = RegimeTable.Resolve(DayStructure.Range, VolatilityRegime.Extreme, hasHighImpactEvent: false);

        Assert.Equal(0.3m, r.RiskMultiplier);
        Assert.Equal(RegimeTable.ObservationMaxTradesPerDay, r.MaxTradesToday);
    }

    [Theory]
    [InlineData(VolatilityRegime.Low)]
    [InlineData(VolatilityRegime.Normal)]
    public void Vung_yen_binh_khong_bi_dong_moi_dung_toi(VolatilityRegime volatility)
    {
        var r = RegimeTable.Resolve(DayStructure.TrendUp, volatility, hasHighImpactEvent: false);

        Assert.Equal(1.0m, r.RiskMultiplier);
        Assert.Equal(RegimeTable.ObservationMaxTradesPerDay, r.MaxTradesToday);
    }

    /// <summary>
    /// Nhãn ngày phải nói thật: biến động CAO cũng là <c>HighVolatility</c>, không chỉ cực đoan.
    /// </summary>
    /// <remarks>
    /// Trước đây nhãn chỉ đổi ở mức cực đoan, nên ngày phân vị 88 được dán TrendUp/TrendDown/
    /// Range và <c>market.day_regime_match</c> vẫn có thể cho 10/10 cho một lệnh thuận xu hướng.
    /// Tầng duy nhất phản ứng là <c>market.volatility_regime</c> với 4 điểm trên 85.
    /// </remarks>
    [Fact]
    public void Nhan_ngay_bien_dong_cao_khong_con_bi_giau_duoi_nhan_xu_huong()
    {
        // 79 phiên biên độ 10 rồi 25 phiên biên độ 100: ATR hiện tại nằm gần đỉnh chuỗi nhưng
        // KHÔNG phải giá trị lớn nhất, vì làm trơn Wilder còn đang bò lên trong khối sau.
        var candles = DailyPlanFixtures.FlatClose(
            Enumerable.Repeat(10m, 79).Concat(Enumerable.Repeat(100m, 25)));

        var result = Classifier.Classify(
            new DailyPlanInputs
            {
                BtcDailyCandles = candles,
                SymbolDailyCandles = candles,
                TodayEvents = Array.Empty<Domain.Entities.ScheduledEvent>(),
            },
            DailyPlanFixtures.Settings());

        // Khẳng định trước rằng chuỗi này thật sự rơi vào vùng cần kiểm, để test không âm thầm
        // đổi ý nghĩa khi ai đó chỉnh fixture.
        Assert.True(result.Volatility >= VolatilityRegime.High, $"Chuỗi phải cho vùng High trở lên, thực tế {result.Volatility}.");
        Assert.Equal(DayRegime.HighVolatility, result.Regime);
    }
}
