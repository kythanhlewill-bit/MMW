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
    public void PlanLive_vao_bang_lenh_cho_khi_co_muc_thu_dong()
    {
        var card = Card();

        var plan = _planner.PlanLive(card)!;

        Assert.Equal(TradeExecutionPlanner.LiveMode, plan.Mode);
        Assert.Single(plan.Entries);
        Assert.True(plan.Entries[0].IsLimit);
        Assert.Equal(card.SuggestedLimitEntry, plan.Entries[0].Price);
        Assert.Equal(TradeExecutionPlanner.LiveLimitExpiryBars, plan.LimitEntryExpiryBars);
        Assert.Equal(card.SuggestedStopLoss, plan.StopLoss);
        Assert.Equal(card.SuggestedTakeProfit, plan.FirstTakeProfit);

        // Trade chỉ có một EntryPrice và một Quantity, nên mọi thứ nhiều chân đều phải vắng mặt.
        Assert.Null(plan.RunnerTakeProfit);
        Assert.Equal(1m, plan.FirstTakeProfitFraction);
        Assert.False(plan.MoveRunnerStopToBreakeven);
    }

    /// <summary>
    /// Mức chờ phải nằm đúng phía KHÔNG khớp ngay của sổ lệnh, nếu không thì lùi về lệnh thị trường.
    /// </summary>
    /// <remarks>
    /// Một lệnh chờ đặt sai phía sổ sẽ cắt qua và khớp thành taker. Nếu kế hoạch vẫn khai
    /// <c>IsLimit</c> trong tình huống đó thì cổng chi phí tính phí maker cho một cú khớp taker —
    /// tái lập đúng loại sai lệch mà <c>PlanLive</c> sinh ra để chấm dứt. Thà vào bằng lệnh thị
    /// trường và bị chấm đắt, còn hơn được chấm rẻ rồi trả đắt.
    /// </remarks>
    [Theory]
    // Bán: mức chờ phải CAO hơn giá hiện tại.
    [InlineData(TradeDirection.Short, 1876.00, false)]   // thấp hơn ⟹ khớp ngay
    [InlineData(TradeDirection.Short, 1878.31, false)]   // đúng bằng giá ⟹ cắt sổ
    [InlineData(TradeDirection.Short, 1880.55, true)]
    [InlineData(TradeDirection.Short, 1884.50, false)]   // sát dừng lỗ ⟹ khối lượng nổ
    // Mua: mức chờ phải THẤP hơn giá hiện tại.
    [InlineData(TradeDirection.Long, 1880.00, false)]
    [InlineData(TradeDirection.Long, 1876.07, true)]
    [InlineData(TradeDirection.Long, 1872.20, false)]    // sát dừng lỗ
    public void Muc_cho_sai_phia_hoac_sat_dung_lo_thi_lui_ve_lenh_thi_truong(
        TradeDirection direction, double limitEntry, bool expectLimit)
    {
        var card = Card(direction);
        card.SuggestedLimitEntry = (decimal)limitEntry;

        var plan = _planner.PlanLive(card)!;

        Assert.Equal(expectLimit, plan.Entries[0].IsLimit);
        Assert.Equal(expectLimit ? (decimal)limitEntry : card.SuggestedEntry, plan.Entries[0].Price);
        Assert.Equal(expectLimit, plan.LimitEntryExpiryBars is not null);
    }

    /// <summary>
    /// Mức chờ SÁT giá thị trường không phải mức chờ — nó là lệnh thị trường đội lốt.
    /// </summary>
    /// <remarks>
    /// Khoá lại sự cố ngày 25–26/08/2026. Điều kiện cũ chỉ đòi "thấp hơn giá hiện tại", nên phiếu
    /// #2826 đặt mua cách giá 1,3 phần vạn và phiếu #3084 cách 4,7 phần vạn. Cả hai bị Binance từ
    /// chối bằng -5022: <c>SuggestedEntry</c> là giá ticker lúc CHẤM, còn lệnh ra sàn 2–4 giây sau
    /// đó, và ETH đi qua khoảng cách đó trong chớp mắt.
    ///
    /// Bị từ chối vẫn còn là kết cục MAY. Kết cục xấu là mức chờ đủ mỏng để khớp thành taker mà
    /// vẫn được khai <c>IsLimit</c> — cổng chi phí chấm phí maker cho một cú khớp taker, đúng loại
    /// nói dối mà <c>PlanLive</c> sinh ra để chấm dứt.
    ///
    /// Trên 2.567 phiếu từng có mức chờ, 52% đặt dưới 10 phần vạn. Đây không phải ca hiếm.
    /// </remarks>
    [Theory]
    // Khoảng cách tới dừng lỗ của phiếu mẫu là 7,114 → ngưỡng tối thiểu 15% = 1,067.
    [InlineData(TradeDirection.Long, 1878.30, false)]    // sát giá 1 xu ⟹ chính là ca -5022
    [InlineData(TradeDirection.Long, 1877.50, false)]    // cách 0,81 — vẫn dưới ngưỡng
    [InlineData(TradeDirection.Long, 1877.24, true)]     // cách 1,07 — vừa đủ
    [InlineData(TradeDirection.Short, 1878.32, false)]
    [InlineData(TradeDirection.Short, 1879.00, false)]
    [InlineData(TradeDirection.Short, 1879.38, true)]
    public void Muc_cho_qua_sat_gia_thi_truong_thi_lui_ve_lenh_thi_truong(
        TradeDirection direction, double limitEntry, bool expectLimit)
    {
        var card = Card(direction);
        card.SuggestedLimitEntry = (decimal)limitEntry;

        var plan = _planner.PlanLive(card)!;

        Assert.Equal(expectLimit, plan.Entries[0].IsLimit);
        Assert.Equal(expectLimit ? (decimal)limitEntry : card.SuggestedEntry, plan.Entries[0].Price);
    }

    [Fact]
    public void Khong_co_muc_cho_thi_vao_bang_lenh_thi_truong()
    {
        var card = Card();
        card.SuggestedLimitEntry = null;

        var plan = _planner.PlanLive(card)!;

        Assert.False(plan.Entries[0].IsLimit);
        Assert.Equal(card.SuggestedEntry, plan.Entries[0].Price);
        Assert.Null(plan.LimitEntryExpiryBars);
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

        // Kế hoạch 2 chân của backtest: 60% thị trường + 40% lệnh chờ maker, chốt tại 1,5R.
        // Bốn con số này đúng bằng những gì đã ghi vào phiếu trên máy chủ chạy thật.
        Assert.Equal(1.960m, Math.Round(planned.GrossFirstTargetR, 3));
        Assert.Equal(0.188m, Math.Round(planned.TargetCostR, 3));
        Assert.Equal(0.377m, Math.Round(planned.StopCostR, 3));
        Assert.Equal(1.287m, Math.Round(planned.NetRiskReward, 3));

        // Cái sẽ thật sự chạy nếu bỏ lệnh chờ đi: một lệnh thị trường tại SuggestedEntry. Kém hơn
        // hẳn con số cổng cũ báo — tức là cổng cũ LẠC QUAN, nó có thể cho qua lệnh mà kinh tế
        // thật không gánh nổi chứ không phải chỉ khắt khe thừa.
        var market = Evaluate(MarketVariant(card), card);
        Assert.Equal(1.608m, Math.Round(market.GrossFirstTargetR, 3));
        Assert.Equal(1.019m, Math.Round(market.NetRiskReward, 3));
        Assert.True(planned.NetRiskReward > market.NetRiskReward);
    }

    /// <summary>
    /// Vào bằng lệnh chờ tại mức retest cải thiện mạnh netRR — nhưng cổng phí/mục tiêu vẫn chặn.
    /// </summary>
    /// <remarks>
    /// Ghi lại để khỏi kỳ vọng nhầm về mục 2. Lệnh chờ ăn tiền không phải nhờ phí maker rẻ hơn
    /// (phí vào chỉ chiếm 1/3 chi phí; 2/3 nằm ở phía thoát dừng lỗ vốn bắt buộc là taker) mà
    /// nhờ VÀO ĐƯỢC GIÁ TỐT HƠN: vào tại 1880,55 thay vì 1878,31 đẩy R:R hình học từ 1,61 lên
    /// 2,81, và netRR từ 1,019 lên 1,913 — vượt xa mức cần 1,50.
    ///
    /// Cổng còn lại là phí/mục tiêu, và nó có dạng rất gọn: phí/mục tiêu = ma sát ÷ khoảng cách
    /// tới chốt lời. Với ~10 bps ma sát, cổng 10% đòi chốt lời cách ít nhất 100 bps; thế này chỉ
    /// cách 72,7 bps nên ra 13,8%. Bề rộng dừng lỗ KHÔNG ảnh hưởng tỉ lệ này — cả tử và mẫu cùng
    /// tỉ lệ nghịch với nó.
    /// </remarks>
    [Fact]
    public void Lenh_cho_cai_thien_manh_netRR_nhung_cong_phi_tren_muc_tieu_van_chan()
    {
        var card = Card();

        var market = Evaluate(MarketVariant(card), card);
        var limit = Evaluate(_planner.PlanLive(card)!, card);

        Assert.Equal(2.806m, Math.Round(limit.GrossFirstTargetR, 3));
        Assert.Equal(1.913m, Math.Round(limit.NetRiskReward, 3));
        Assert.True(limit.NetRiskReward > market.NetRiskReward);

        // netRR đã qua ngưỡng 1,50 — cổng chặn giờ chỉ còn là phí/mục tiêu.
        Assert.True(limit.NetRiskReward >= _settings.V3MinNetRiskReward);
        Assert.Equal(13.8m, Math.Round(limit.CostToTargetPercent, 1));
        Assert.True(limit.CostToTargetPercent > _settings.V3MaxCostToTargetPercent);
        Assert.False(limit.Passed);
    }

    // ── Chốt hai phần ───────────────────────────────────────────────────

    /// <summary>
    /// Phiếu mang đủ hai mục tiêu thì kế hoạch chạy thật phải giữ CẢ HAI.
    /// </summary>
    /// <remarks>
    /// Đây là chỗ rò rỉ lớn nhất của bộ luật cũ, và nó rò suốt vì trông rất giống một quyết định
    /// thiết kế: <c>PlanLive</c> gán thẳng <c>RunnerTakeProfit = null</c> và
    /// <c>FirstTakeProfitFraction = 1</c>, nên đường chạy thật đặt đúng một lệnh chốt lời cỡ đầy
    /// đủ ở mục tiêu XA. Hệ quả là mọi lệnh không đi tới đích đều quay về chạm dừng lỗ và mất
    /// trọn 1R — kể cả những lệnh đã đi đúng hướng quá nửa đường.
    /// </remarks>
    [Fact]
    public void PlanLive_giu_ca_hai_muc_tieu_khi_phieu_co_du()
    {
        var card = Card(TradeDirection.Long);
        card.SuggestedFirstTakeProfit = 1900m;
        card.SuggestedRunnerTakeProfit = 1950m;
        card.SuggestedTakeProfit = 1950m;

        var settings = EngineSettingDefaults.Create(1);
        settings.StrategyVersion = TradingStrategyVersion.HtfSwingV7;

        var plan = _planner.PlanLive(card, settings)!;

        Assert.Equal(1900m, plan.FirstTakeProfit);
        Assert.Equal(1950m, plan.RunnerTakeProfit);
        Assert.Equal(settings.V7FirstTargetFraction, plan.FirstTakeProfitFraction);
        Assert.True(plan.MoveRunnerStopToBreakeven);
        Assert.Equal(settings.V7TrailPivotBars, plan.TrailRunnerPivotBars);
    }

    /// <summary>Không truyền cấu hình thì kế hoạch quay về một mục tiêu — hành vi trước V7.</summary>
    [Fact]
    public void PlanLive_khong_co_cau_hinh_thi_van_mot_muc_tieu()
    {
        var card = Card(TradeDirection.Long);
        card.SuggestedFirstTakeProfit = 1900m;
        card.SuggestedRunnerTakeProfit = 1950m;

        var plan = _planner.PlanLive(card)!;

        Assert.Null(plan.RunnerTakeProfit);
        Assert.Equal(1m, plan.FirstTakeProfitFraction);
        Assert.False(plan.MoveRunnerStopToBreakeven);
        Assert.Equal(0, plan.TrailRunnerPivotBars);
    }

    /// <summary>
    /// Hai mục tiêu xếp sai thứ tự thì bỏ hẳn phần runner, không "sửa hộ".
    /// </summary>
    /// <remarks>
    /// Nếu mục tiêu cuối lại gần hơn mục tiêu gần thì hai lệnh chốt sẽ tranh nhau trên sàn, và
    /// cái nào khớp trước là do may rủi. Quay về một mục tiêu là kết cục đúng: nó vẫn chạy được
    /// và nó trung thực về việc mình đang làm gì.
    /// </remarks>
    [Fact]
    public void PlanLive_bo_runner_khi_hai_muc_tieu_xep_sai_thu_tu()
    {
        var card = Card(TradeDirection.Long);
        card.SuggestedFirstTakeProfit = 1950m;
        card.SuggestedRunnerTakeProfit = 1900m; // Gần hơn mục tiêu gần — sai.

        var settings = EngineSettingDefaults.Create(1);
        settings.StrategyVersion = TradingStrategyVersion.HtfSwingV7;

        var plan = _planner.PlanLive(card, settings)!;

        Assert.Null(plan.RunnerTakeProfit);
        Assert.Equal(1m, plan.FirstTakeProfitFraction);
    }

    /// <summary>Mục tiêu gần nằm sai phía giá vào thì cũng bỏ runner.</summary>
    [Fact]
    public void PlanLive_bo_runner_khi_muc_tieu_gan_nam_sai_phia()
    {
        var card = Card(TradeDirection.Long);
        card.SuggestedFirstTakeProfit = card.SuggestedEntry!.Value - 10m;
        card.SuggestedRunnerTakeProfit = 1950m;

        var settings = EngineSettingDefaults.Create(1);
        settings.StrategyVersion = TradingStrategyVersion.HtfSwingV7;

        var plan = _planner.PlanLive(card, settings)!;

        Assert.Null(plan.RunnerTakeProfit);
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

    /// <summary>Cùng phiếu đó nhưng không có mức chờ — tức là phải vào bằng lệnh thị trường.</summary>
    private TradeExecutionPlan MarketVariant(EntryScorecard card)
    {
        var clone = Card(card.Direction!.Value);
        clone.SuggestedLimitEntry = null;
        return _planner.PlanLive(clone)!;
    }

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
