using MMW.Application.Trading.Execution;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.Scoring;

public sealed class TradeExecutionPlannerTests
{
    private readonly TradeExecutionPlanner _planner = new();
    private readonly EngineSetting _settings = EngineSettingDefaults.Create(1);

    /// <summary>
    /// Ngày đi ngang: một điểm vào, chốt toàn bộ tại MỨC CẤU TRÚC — không còn chốt cứng tại 1R.
    /// </summary>
    /// <remarks>
    /// V1 chốt toàn bộ tại 1R. Toán chi phí nói rõ đó là mức không thắng nổi: với phí taker hai
    /// chiều cộng trượt giá, một lệnh thua tốn khoảng 1,5R còn một lệnh thắng tại 1R chỉ thu về
    /// 0,6R — tỉ lệ thắng hoà vốn khoảng 72%.
    ///
    /// Và từ V2, phiếu đã phải qua <c>technical.structural_room</c> với R:R tối thiểu 1,6 mới
    /// tới được đây. Chốt tại 1R nghĩa là tự nguyện vứt đi đúng phần chỗ chạy vừa được kiểm
    /// chứng để cho phép lệnh này tồn tại.
    /// </remarks>
    [Fact]
    public void Range_chi_vao_mot_lan_va_chot_toan_bo_tai_muc_cau_truc()
    {
        var card = Card(DayRegime.Range, TradeDirection.Long, score: 75, structure: 10, volume: 5);

        var plan = _planner.Plan(card, card.DailyPlan!, _settings);

        Assert.Equal("RangeQuick", plan.Mode);
        Assert.Single(plan.Entries);
        Assert.True(plan.Entries[0].IsLimit);
        Assert.Equal(8, plan.LimitEntryExpiryBars);
        Assert.Equal(122.5m, plan.FirstTakeProfit);   // mức cấu trúc của phiếu, không phải 1R
        Assert.Null(plan.RunnerTakeProfit);
        Assert.Equal(1m, plan.FirstTakeProfitFraction);
    }

    /// <summary>
    /// Phiếu không có mức cấu trúc thì lùi về bội R — và sàn là <c>MinStructuralRr</c>, không phải 1R.
    /// </summary>
    [Fact]
    public void Range_khong_co_muc_cau_truc_thi_lui_ve_san_MinStructuralRr()
    {
        var card = Card(DayRegime.Range, TradeDirection.Long, score: 75, structure: 10, volume: 5);
        card.SuggestedTakeProfit = null;

        var plan = _planner.Plan(card, card.DailyPlan!, _settings);

        // unitRisk = 15; sàn 1,6R ⟹ 100 + 1,6 × 15 = 124.
        Assert.Equal(124m, plan.FirstTakeProfit);
    }

    [Fact]
    public void Trend_manh_chia_ba_tranche_tong_size_khong_vuot_mot()
    {
        var card = Card(DayRegime.TrendUp, TradeDirection.Long, score: 75, structure: 8, volume: 5);

        var plan = _planner.Plan(card, card.DailyPlan!, _settings);

        Assert.Equal("StrongTrendRunner", plan.Mode);
        Assert.Equal(2, plan.Entries.Count);
        Assert.Equal(1m, plan.Entries.Sum(x => x.RiskWeight));
        Assert.Equal(new[] { 100m, 96.25m }, plan.Entries.Select(x => x.Price));
        Assert.Equal(new[] { 0.60m, 0.40m }, plan.Entries.Select(x => x.RiskWeight));
        Assert.Equal(122.5m, plan.FirstTakeProfit);
        Assert.Equal(145m, plan.RunnerTakeProfit);
        Assert.Equal(0.5m, plan.FirstTakeProfitFraction);
        Assert.True(plan.MoveRunnerStopToBreakeven);
    }

    [Fact]
    public void Trend_co_structure_va_volume_manh_khong_bat_buoc_phai_dat_70_diem()
    {
        var card = Card(DayRegime.TrendUp, TradeDirection.Long, score: 55, structure: 8, volume: 5);

        var plan = _planner.Plan(card, card.DailyPlan!, _settings);

        Assert.Equal("StrongTrendRunner", plan.Mode);
        Assert.Equal(2, plan.Entries.Count);
    }

    [Fact]
    public void Trend_thieu_volume_khong_duoc_gan_runner_may_moc()
    {
        var card = Card(DayRegime.TrendUp, TradeDirection.Long, score: 75, structure: 10, volume: 3);

        var plan = _planner.Plan(card, card.DailyPlan!, _settings);

        Assert.Equal("Standard", plan.Mode);
        Assert.Single(plan.Entries);
        Assert.True(plan.Entries[0].IsLimit);
        Assert.Null(plan.RunnerTakeProfit);
    }

    [Fact]
    public void Trend_nguoc_chieu_khong_duoc_scale_in()
    {
        var card = Card(DayRegime.TrendUp, TradeDirection.Short, score: 80, structure: 10, volume: 5);

        Assert.Equal("Standard", _planner.Plan(card, card.DailyPlan!, _settings).Mode);
    }

    [Fact]
    public void Standard_chot_mot_nua_tai_can_gan_va_giu_runner_toi_muc_xa()
    {
        var card = Card(DayRegime.TrendUp, TradeDirection.Long, score: 70, structure: 6, volume: 5);
        card.SuggestedFirstTakeProfit = 110m;
        card.SuggestedRunnerTakeProfit = 130m;
        card.SuggestedLimitEntry = 97m;

        var plan = _planner.Plan(card, card.DailyPlan!, _settings);

        Assert.Equal("Standard", plan.Mode);
        Assert.Equal(97m, plan.Entries.Single().Price);
        Assert.Equal(110m, plan.FirstTakeProfit);
        Assert.Equal(130m, plan.RunnerTakeProfit);
        Assert.Equal(0.5m, plan.FirstTakeProfitFraction);
        Assert.True(plan.MoveRunnerStopToBreakeven);
        Assert.Equal(3, plan.TrailRunnerPivotBars);
    }

    [Fact]
    public void Retest_qua_sat_stop_duoc_kep_de_khong_no_khoi_luong()
    {
        var card = Card(DayRegime.TrendUp, TradeDirection.Long, score: 75, structure: 8, volume: 5);
        card.SuggestedLimitEntry = 85.1m;

        var plan = _planner.Plan(card, card.DailyPlan!, _settings);

        // UnitRisk=15, sàn cách stop=3,75 ⟹ limit thấp nhất 88,75.
        Assert.Equal(88.75m, plan.Entries[1].Price);
    }

    [Fact]
    public void V3_chi_lap_ke_hoach_theo_setup_da_xac_nhan_va_khoa_loi_nhuan_dong()
    {
        var settings = EngineSettingDefaults.Create(1);
        settings.StrategyVersion = TradingStrategyVersion.TriggerFirstV3;
        var card = Card(DayRegime.TrendUp, TradeDirection.Long, score: 75, structure: 8, volume: 5);
        card.SetupType = SetupType.StrongTrendBreakout;
        card.TriggerState = SetupTriggerState.Confirmed;
        card.ExpectedCostR = 0.10m;

        var plan = _planner.Plan(card, card.DailyPlan!, settings);

        Assert.Equal("StrongTrendRunnerV3", plan.Mode);
        Assert.Equal(new[] { 0.60m, 0.40m }, plan.Entries.Select(x => x.RiskWeight));
        Assert.Equal(0.30m, plan.FirstTakeProfitFraction);
        Assert.True(plan.MoveRunnerStopToBreakeven);
    }

    [Fact]
    public void V3_tu_choi_lap_ke_hoach_khi_chua_co_setup_trigger()
    {
        var settings = EngineSettingDefaults.Create(1);
        settings.StrategyVersion = TradingStrategyVersion.TriggerFirstV3;
        var card = Card(DayRegime.TrendUp, TradeDirection.Long, score: 75, structure: 8, volume: 5);

        Assert.Throws<ArgumentException>(() => _planner.Plan(card, card.DailyPlan!, settings));
    }

    private static EntryScorecard Card(
        DayRegime regime, TradeDirection direction, int score, int structure, int volume)
    {
        var card = new EntryScorecard
        {
            Outcome = ScorecardOutcome.Entered,
            Direction = direction,
            TotalScore = score,
            SuggestedEntry = 100m,
            SuggestedStopLoss = direction == TradeDirection.Long ? 85m : 115m,
            SuggestedTakeProfit = direction == TradeDirection.Long ? 122.5m : 77.5m,
            DailyPlan = new DailyPlan
            {
                PlanDateUtc = new DateOnly(2026, 8, 3),
                DayRegime = regime,
                AllowedDirections = AllowedDirections.Both,
                MaxTradesToday = 5,
            },
        };
        card.Lines.Add(Line("technical.market_structure", structure, 10));
        card.Lines.Add(Line("technical.volume_confirmation", volume, 5));
        return card;
    }

    private static EntryScorecardLine Line(string key, int points, int max) => new()
    {
        CriterionKey = key,
        AwardedPoints = points,
        MaxPoints = max,
        Group = ScoreGroup.Technical,
        Reason = "test",
    };
}
