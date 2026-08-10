using MMW.Application.MarketData.Models;
using MMW.Application.Trading.DailyPlanning;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.DailyPlanning;

public sealed class IntradayRegimeOverrideTests
{
    private static readonly DateTime Start = new(2026, 8, 4, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Hai_breakout_tang_lien_tiep_co_volume_moi_override_Range_sang_TrendUp()
    {
        var candles = Baseline();
        candles.Add(Candle(100m, 102m, 102m, 99m, 200m, 32));

        var beforeConfirmation = IntradayRegimeOverridePolicy.Resolve(Plan(), candles, Setting());
        Assert.False(beforeConfirmation.IsOverride);

        candles.Add(Candle(102m, 103m, 103m, 101.5m, 220m, 33));
        var confirmed = IntradayRegimeOverridePolicy.Resolve(Plan(), candles, Setting());

        Assert.True(confirmed.IsOverride);
        Assert.Equal(DayRegime.TrendUp, confirmed.Regime);
        Assert.Equal(AllowedDirections.LongOnly, confirmed.AllowedDirections);
    }

    [Fact]
    public void Hai_breakout_giam_lien_tiep_co_volume_override_sang_TrendDown()
    {
        var candles = Baseline();
        candles.Add(Candle(100m, 98m, 101m, 98m, 200m, 32));
        candles.Add(Candle(98m, 97m, 98.5m, 97m, 220m, 33));

        var result = IntradayRegimeOverridePolicy.Resolve(Plan(), candles, Setting());

        Assert.True(result.IsOverride);
        Assert.Equal(DayRegime.TrendDown, result.Regime);
        Assert.Equal(AllowedDirections.ShortOnly, result.AllowedDirections);
    }

    [Fact]
    public void Hai_nen_quay_lai_bien_huy_override_va_cooldown_chan_bat_lai_ngay()
    {
        var candles = Baseline();
        candles.Add(Candle(100m, 102m, 102m, 99m, 200m, 32));
        candles.Add(Candle(102m, 103m, 103m, 101.5m, 220m, 33));
        candles.Add(Candle(103m, 101m, 103m, 100.5m, 100m, 34));
        candles.Add(Candle(101m, 100m, 101.5m, 99.5m, 100m, 35));
        candles.Add(Candle(100m, 104m, 104m, 99.5m, 250m, 36));
        candles.Add(Candle(104m, 105m, 105m, 103.5m, 250m, 37));

        var result = IntradayRegimeOverridePolicy.Resolve(Plan(), candles, Setting());

        Assert.False(result.IsOverride);
        Assert.Equal(DayRegime.Range, result.Regime);
    }

    [Fact]
    public void Apply_tao_view_moi_va_khong_sua_ke_hoach_ngay_bat_bien()
    {
        var original = Plan();
        var decision = new IntradayRegimeDecision(
            DayRegime.TrendUp, AllowedDirections.LongOnly, true, "test");

        var effective = IntradayRegimeOverridePolicy.Apply(original, decision);

        Assert.NotSame(original, effective);
        Assert.Equal(DayRegime.Range, original.DayRegime);
        Assert.Equal(AllowedDirections.Both, original.AllowedDirections);
        Assert.Equal(DayRegime.TrendUp, effective.DayRegime);
        Assert.Equal(original.RiskMultiplier, effective.RiskMultiplier);
        Assert.Equal(original.MaxTradesToday, effective.MaxTradesToday);
    }

    [Fact]
    public void Ke_hoach_khong_phai_Range_khong_bi_override()
    {
        var plan = Plan();
        plan.DayRegime = DayRegime.TrendUp;
        plan.AllowedDirections = AllowedDirections.LongOnly;

        var result = IntradayRegimeOverridePolicy.Resolve(plan, Baseline(), Setting());

        Assert.False(result.IsOverride);
        Assert.Equal(DayRegime.TrendUp, result.Regime);
    }

    private static DailyPlan Plan() => new()
    {
        PlanDateUtc = DateOnly.FromDateTime(Start),
        DayRegime = DayRegime.Range,
        AllowedDirections = AllowedDirections.Both,
        RiskMultiplier = 0.6m,
        MaxTradesToday = 3,
    };

    private static EngineSetting Setting() => new()
    {
        VolumeBreakoutMultiple = 1.5m,
        MinCandleBodyRatio = 0.3m,
    };

    private static List<Candle> Baseline() => Enumerable.Range(0, 32)
        .Select(i => Candle(100m, 100m, 101m, 99m, 100m, i))
        .ToList();

    private static Candle Candle(
        decimal open, decimal close, decimal high, decimal low, decimal volume, int index)
    {
        var at = Start.AddMinutes(index * 15);
        return new Candle(at, open, high, low, close, volume, at.AddMinutes(15).AddTicks(-1));
    }
}
