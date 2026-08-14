using MMW.Application.Trading.Execution;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.RuleEngine.Tests.Scoring;
using Xunit;

namespace MMW.RuleEngine.Tests.Execution;

/// <summary>
/// Cổng chi phí phải chấm ĐÚNG cái lệnh sẽ được gửi sàn.
/// </summary>
/// <remarks>
/// Bộ kiểm thử này khoá lại một lỗ hổng phát hiện ngày 14/08/2026. <see cref="TradeExecutionPlanner.Plan"/>
/// mô tả kế hoạch nhiều chân với chân sau là lệnh chờ maker; trình mô phỏng backtest thực hiện
/// đầy đủ, nhưng <c>ScorecardExecutionService</c> chưa từng gọi planner — nó đọc thẳng mức giá
/// trên phiếu và ghi cứng <c>OrderType.Market</c>. Suốt thời gian đó cổng chi phí chấm một kế
/// hoạch không bao giờ chạy.
///
/// Sai lệch đo được trên phiếu thật lúc 13:31 ngày 14/08 (ETHUSDT bán, StrongTrendBreakout):
/// cổng thấy netRR 1,287 trong khi lệnh thật chỉ đạt 1,019 — lạc quan hơn 26%. Cùng chiều lạc
/// quan, tức là cổng có thể cho qua những lệnh mà kinh tế thật không gánh nổi.
/// </remarks>
public sealed class LiveExecutionPlanTests
{
    private readonly TradeExecutionPlanner _planner = new();
    private readonly ExecutionViabilityPolicy _viability = new();
    private readonly EngineSetting _settings = EngineSettingDefaults.Create(1);

    public LiveExecutionPlanTests()
    {
        // Bản chạy thật đang ở V6 (EngineSettings.Id = 1, tài khoản testnet). `Plan` rẽ nhánh
        // theo settings chứ không theo phiếu, nên để mặc định V2 là đo nhầm một chiến lược khác.
        _settings.StrategyVersion = TradingStrategyVersion.AdaptiveSidewaysV6;
    }

    // ── Kế hoạch chạy thật đúng bằng cái sẽ gửi sàn ──────────────────────

    [Fact]
    public void PlanLive_la_mot_chan_thi_truong_dung_ba_muc_gia_cua_phieu()
    {
        var card = Card();

        var plan = _planner.PlanLive(card)!;

        Assert.Equal(TradeExecutionPlanner.LiveMode, plan.Mode);
        Assert.Single(plan.Entries);
        Assert.False(plan.Entries[0].IsLimit);
        Assert.Equal(card.SuggestedEntry, plan.Entries[0].Price);
        Assert.Equal(card.SuggestedStopLoss, plan.StopLoss);
        Assert.Equal(card.SuggestedTakeProfit, plan.FirstTakeProfit);

        // Trade chỉ có một EntryPrice và một Quantity, nên mọi thứ nhiều chân đều phải vắng mặt.
        Assert.Null(plan.RunnerTakeProfit);
        Assert.Equal(1m, plan.FirstTakeProfitFraction);
        Assert.False(plan.MoveRunnerStopToBreakeven);
    }

    /// <summary>
    /// Bất biến mà <c>ScorecardExecutionService</c> dựa vào để tính khối lượng.
    /// </summary>
    /// <remarks>
    /// Khối lượng = (tiền rủi ro × FinalSizeR) / |entry − stop|. Phép đó chỉ đúng khi chân duy
    /// nhất tiêu TRỌN ngân sách rủi ro. Một kế hoạch hai chân lọt vào đây sẽ khiến lệnh gửi sàn
    /// mang khối lượng của cả 1R trong khi cổng chi phí tưởng nó chỉ mang 0,6R.
    /// </remarks>
    [Fact]
    public void PlanLive_luon_dung_mot_chan_va_tron_ngan_sach_rui_ro()
    {
        foreach (var direction in new[] { TradeDirection.Long, TradeDirection.Short })
        {
            var plan = _planner.PlanLive(Card(direction))!;

            Assert.Single(plan.Entries);
            Assert.Equal(1m, plan.Entries.Sum(e => e.RiskWeight));
        }
    }

    [Theory]
    [InlineData(null, 1885.42413467, 1866.87275645)]
    [InlineData(1878.31, null, 1866.87275645)]
    [InlineData(1878.31, 1885.42413467, null)]
    [InlineData(0d, 1885.42413467, 1866.87275645)]
    [InlineData(1878.31, 1878.31, 1866.87275645)]   // entry trùng stop ⟹ khối lượng vô hạn
    public void PlanLive_tra_null_khi_khong_dat_duoc_lenh(double? entry, double? stop, double? target)
    {
        var card = Card();
        card.SuggestedEntry = (decimal?)entry;
        card.SuggestedStopLoss = (decimal?)stop;
        card.SuggestedTakeProfit = (decimal?)target;

        Assert.Null(_planner.PlanLive(card));
    }

    // ── Sai lệch mà bản vá này đóng lại ─────────────────────────────────

    /// <summary>
    /// Số thật của phiếu 13:31 ngày 14/08/2026, tái dựng nguyên văn.
    /// </summary>
    /// <remarks>
    /// Bốn con số của nhánh kế hoạch (1,960R / 0,188R / 0,377R / 1,287) là những gì đã được ghi
    /// vào cột <c>NetRiskReward</c> và <c>ExpectedCostR</c> của phiếu trên máy chủ chạy thật.
    /// Giữ chúng ở đây để bất kỳ thay đổi nào trong mô hình phí đều lộ ra ngay.
    /// </remarks>
    [Fact]
    public void The_1331_ngay_14_08_do_hai_kieu_cho_hai_ket_qua_khac_nhau()
    {
        var card = Card();

        var planned = Evaluate(_planner.Plan(card, card.DailyPlan!, _settings), card);
        var live = Evaluate(_planner.PlanLive(card)!, card);

        // Kế hoạch 2 chân: 60% thị trường + 40% lệnh chờ maker, chốt tại 1,5R.
        Assert.Equal(1.960m, Math.Round(planned.GrossFirstTargetR, 3));
        Assert.Equal(0.188m, Math.Round(planned.TargetCostR, 3));
        Assert.Equal(0.377m, Math.Round(planned.StopCostR, 3));
        Assert.Equal(1.287m, Math.Round(planned.NetRiskReward, 3));

        // Cái thật sự chạy: một lệnh thị trường, chốt tại SuggestedTakeProfit.
        Assert.Equal(1.608m, Math.Round(live.GrossFirstTargetR, 3));
        Assert.Equal(1.019m, Math.Round(live.NetRiskReward, 3));

        // Chiều của sai lệch mới là điều nguy hiểm: cổng cũ LẠC QUAN hơn thực tế, nên nó có thể
        // cho qua lệnh mà kinh tế thật không gánh nổi — chứ không phải chỉ khắt khe thừa.
        Assert.True(planned.NetRiskReward > live.NetRiskReward);
        Assert.True(planned.CostToTargetPercent < live.CostToTargetPercent);
    }

    /// <summary>
    /// Vào bằng lệnh chờ rẻ hơn — nhưng một mình nó chưa qua nổi cổng ở bề rộng dừng lỗ này.
    /// </summary>
    /// <remarks>
    /// Đo trước khi làm để khỏi kỳ vọng nhầm: chuyển sang maker cắt được ~28% chi phí, nhưng hai
    /// phần ba chi phí nằm ở phía thoát lệnh dừng lỗ — vốn bắt buộc là taker — nên netRR chỉ lên
    /// 1,188 so với mức cần 1,50. Bề rộng dừng lỗ mới là biến quyết định.
    /// </remarks>
    [Fact]
    public void Vao_bang_lenh_cho_re_hon_nhung_chua_du_qua_cong()
    {
        var card = Card();
        var live = _planner.PlanLive(card)!;
        var maker = live with { Entries = [live.Entries[0] with { IsLimit = true }] };

        var asMarket = Evaluate(live, card);
        var asLimit = Evaluate(maker, card);

        Assert.True(asLimit.ExpectedCostR < asMarket.ExpectedCostR);
        Assert.Equal(0.265m, Math.Round(asLimit.ExpectedCostR, 3));
        Assert.Equal(1.188m, Math.Round(asLimit.NetRiskReward, 3));

        // Cả hai vẫn trượt cổng — mục 2 của kế hoạch không tự nó mở được lệnh nào.
        Assert.False(asMarket.Passed);
        Assert.False(asLimit.Passed);
    }

    // ── Backtest giữ nguyên đường cũ ────────────────────────────────────

    /// <summary>
    /// Trình mô phỏng thực hiện đủ nhiều chân, nên nó phải tiếp tục đo trên <c>Plan</c>.
    /// </summary>
    [Fact]
    public void Plan_van_giu_ke_hoach_nhieu_chan_cho_backtest()
    {
        var card = Card();
        var plan = _planner.Plan(card, card.DailyPlan!, _settings);

        Assert.Equal(2, plan.Entries.Count);
        Assert.False(plan.Entries[0].IsLimit);
        Assert.True(plan.Entries[1].IsLimit);
        Assert.Equal(1m, plan.Entries.Sum(e => e.RiskWeight));
    }

    // ── Dựng phiếu ──────────────────────────────────────────────────────

    private ExecutionViability Evaluate(TradeExecutionPlan plan, EntryScorecard card) =>
        _viability.Evaluate(
            plan,
            card.Direction!.Value,
            _settings,
            enforceV3Gates: true,
            setupType: card.SetupType);

    /// <summary>Phiếu 13:31 ngày 14/08/2026 — ETHUSDT bán, StrongTrendBreakout, chất lượng 100.</summary>
    private static EntryScorecard Card(TradeDirection direction = TradeDirection.Short)
    {
        var isShort = direction == TradeDirection.Short;
        return new EntryScorecard
        {
            Symbol = "ETHUSDT",
            Outcome = ScorecardOutcome.Entered,
            Direction = direction,
            SetupType = SetupType.StrongTrendBreakout,
            StrategyVersion = TradingStrategyVersion.AdaptiveSidewaysV6,
            SuggestedEntry = 1878.31m,
            SuggestedLimitEntry = isShort ? 1880.55m : 1876.07m,
            SuggestedStopLoss = isShort ? 1885.42413467m : 1871.19586533m,
            SuggestedTakeProfit = isShort ? 1866.87275645m : 1889.74724355m,
            DailyPlan = new DailyPlan
            {
                PlanDateUtc = new DateOnly(2026, 8, 14),
                DayRegime = DayRegime.Range,
                BtcStructure = "Range",
                AllowedDirections = AllowedDirections.Both,
                MaxTradesToday = 20,
            },
        };
    }
}
