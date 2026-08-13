using MMW.Application.Trading.DailyPlanning;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.DailyPlanning;

public sealed class AdaptiveRegimePolicyTests
{
    [Fact]
    public void Range_ngay_thuong_giam_nhip_nhung_khong_cam()
    {
        var result = AdaptiveRegimePolicy.Apply(
            new DateOnly(2026, 8, 3), DayRegime.Range, 1m, RegimeTable.ObservationMaxTradesPerDay);

        Assert.Equal(0.6m, result.RiskMultiplier);
        Assert.Equal(RegimeTable.ObservationMaxTradesPerDay, result.MaxTradesToday);
    }

    [Theory]
    [InlineData(2026, 8, 8)]
    [InlineData(2026, 8, 9)]
    public void Cuoi_tuan_giam_con_hai_setup_chu_khong_cam_tuyet_doi(int y, int m, int d)
    {
        var result = AdaptiveRegimePolicy.Apply(
            new DateOnly(y, m, d), DayRegime.TrendUp, 1m, RegimeTable.ObservationMaxTradesPerDay);

        Assert.Equal(0.5m, result.RiskMultiplier);
        Assert.Equal(RegimeTable.ObservationMaxTradesPerDay, result.MaxTradesToday);
    }

    [Fact]
    public void Policy_chi_cap_xuong_khong_lam_tang_rui_ro_dang_thap()
    {
        var result = AdaptiveRegimePolicy.Apply(
            new DateOnly(2026, 8, 8), DayRegime.EventDay, 0.3m, 1);

        Assert.Equal(0.3m, result.RiskMultiplier);
        Assert.Equal(1, result.MaxTradesToday);
    }
}
