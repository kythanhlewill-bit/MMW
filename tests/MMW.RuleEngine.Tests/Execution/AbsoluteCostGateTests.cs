using MMW.Application.Trading.Execution;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.Execution;

/// <summary>
/// Hai cổng TUYỆT ĐỐI của <see cref="ExecutionViabilityPolicy"/>: sàn khoảng cách dừng lỗ và
/// trần chi phí theo R.
/// </summary>
/// <remarks>
/// Bộ kiểm thử này khoá lại một lỗ hổng đã chạy thật và đã mất tiền. Trước 2026-08-30 cổng chi
/// phí chỉ có hai ngưỡng, và cả hai đều là TỈ LỆ:
/// <code>
/// netRiskReward     ≥ V3MinNetRiskReward        (1,50)
/// cost / grossTarget ≤ V3MaxCostToTargetPercent  (15%)
/// </code>
///
/// Một tỉ lệ luôn có thể được thoả bằng cách kéo mẫu số ra xa. Phiếu #3496 (ETHUSDT, 27/08,
/// TriangleBreakout) làm đúng thế và đi qua cổng với những con số này:
/// <code>
/// dừng lỗ       6,35 bps   ⟵ sàn MinStopDistancePercent là 40 bps
/// ExpectedCostR 1,573      ⟵ phí một vòng ăn hết 1,57 lần ngân sách rủi ro
/// NetRiskReward 8,472      ⟹ cost/target chỉ 15% ⟹ CẢ HAI CỔNG TỈ LỆ ĐỀU THOẢ
/// </code>
/// Lệnh #63 sinh ra từ nó đóng ở −1,77R (−26,32 USDT), lệnh lỗ đậm nhất của cả đợt chạy thử.
///
/// Điều cần khoá không phải "phiếu này bị chặn" mà là LÝ DO nó bị chặn: hai cổng tỉ lệ vẫn thoả,
/// và chính hai cổng tuyệt đối mới là thứ chặn. Nếu ai đó nới sàn hay trần trong tương lai, test
/// này phải đỏ chứ không được im lặng cho qua.
/// </remarks>
public sealed class AbsoluteCostGateTests
{
    private readonly ExecutionViabilityPolicy _viability = new();

    private static EngineSetting Settings(Action<EngineSetting>? configure = null)
    {
        var s = EngineSettingDefaults.Create(1);
        s.StrategyVersion = TradingStrategyVersion.AdaptiveSidewaysV6;
        configure?.Invoke(s);
        return s;
    }

    /// <summary>Kế hoạch một chân, lệnh thị trường — đúng dạng mà đường chạy thật đặt.</summary>
    private static TradeExecutionPlan Plan(decimal entry, decimal stop, decimal target) =>
        new(
            [new PlannedEntryTranche(entry, 1m)],
            StopLoss: stop,
            FirstTakeProfit: target,
            RunnerTakeProfit: null,
            FirstTakeProfitFraction: 1m,
            MoveRunnerStopToBreakeven: false,
            Mode: TradeExecutionPlanner.LiveMode);

    private ExecutionViability Evaluate(TradeExecutionPlan plan, EngineSetting settings) =>
        _viability.Evaluate(plan, TradeDirection.Long, settings, enforceV3Gates: true,
            setupType: SetupType.TriangleBreakout);

    /// <summary>Dựng lại chính phiếu #3496: mức giá thật, dừng lỗ 6,4 phần vạn.</summary>
    private static TradeExecutionPlan Card3496() => Plan(entry: 2492.04m, stop: 2490.45m, target: 2527.00m);

    // ── Cái mà hai cổng tỉ lệ KHÔNG thấy ────────────────────────────────

    [Fact]
    public void Phieu_3496_bi_chan_du_ca_hai_cong_ti_le_deu_thoa()
    {
        var settings = Settings();
        var result = Evaluate(Card3496(), settings);

        // Hai cổng tỉ lệ vẫn cho qua — đây là điều làm lỗ hổng cũ khó thấy.
        Assert.True(result.NetRiskReward >= settings.V3MinNetRiskReward,
            $"netRR {result.NetRiskReward:N3} lẽ ra vẫn thoả ngưỡng tỉ lệ.");
        Assert.True(result.CostToTargetPercent <= settings.V6BreakoutMaxCostToTargetPercent,
            $"cost/target {result.CostToTargetPercent:N1}% lẽ ra vẫn thoả ngưỡng tỉ lệ.");

        // Nhưng kinh tế thật thì không gánh nổi, và hai cổng tuyệt đối nói ra điều đó.
        Assert.True(result.StopDistanceBps < settings.MinStopDistancePercent * 100m);
        Assert.True(result.ExpectedCostR > settings.MaxExpectedCostR);
        Assert.False(result.Passed);
    }

    [Fact]
    public void Ly_do_chan_noi_ro_ca_hai_con_so_de_doi_chieu_duoc()
    {
        var result = Evaluate(Card3496(), Settings());

        Assert.Contains("bps", result.DetailVi);
        Assert.Contains("Chi phí dự kiến", result.DetailVi);
    }

    // ── Sàn khoảng cách dừng lỗ, tách riêng khỏi trần chi phí ────────────

    /// <summary>
    /// Dừng lỗ dưới sàn bị chặn NGAY CẢ KHI chi phí bằng 0.
    /// </summary>
    /// <remarks>
    /// Hai cổng phải độc lập. Gộp làm một thì nới cái này sẽ âm thầm tắt cái kia. Dựng bằng biểu
    /// phí bằng 0 — một thế giới không có phí — để chứng minh sàn dừng lỗ tự nó chặn được, chứ
    /// không phải chỉ là hệ quả của trần chi phí.
    ///
    /// Sàn này vẫn cần thiết kể cả khi phí bằng 0, vì bề rộng dừng lỗ còn nói một điều khác:
    /// dừng lỗ 6 phần vạn nằm trong biên độ nhiễu của chính cây nến, nên nó bị quét vì ngẫu
    /// nhiên chứ không vì luận điểm của setup sai.
    /// </remarks>
    [Fact]
    public void Dung_lo_duoi_san_van_bi_chan_du_khong_mat_phi_nao()
    {
        var settings = Settings(s =>
        {
            s.BacktestTakerFeePercent = 0m;
            s.BacktestMakerFeePercent = 0m;
            s.BacktestEntrySlippageBps = 0m;
            s.BacktestStopSlippageBps = 0m;
        });

        var result = Evaluate(Card3496(), settings);

        Assert.Equal(0m, result.ExpectedCostR);                 // không có gì để trần chi phí bắt
        Assert.True(result.StopDistanceBps < settings.MinStopDistancePercent * 100m);
        Assert.False(result.Passed);                            // sàn dừng lỗ vẫn chặn
    }

    /// <summary>
    /// Chi phí vượt trần bị chặn NGAY CẢ KHI khoảng cách dừng lỗ đã qua sàn.
    /// </summary>
    /// <remarks>
    /// Chiều ngược lại của test trên. Dựng bằng cách nâng phí thay vì bóp dừng lỗ, để chứng minh
    /// trần chi phí tự nó chặn được chứ không phải chỉ là hệ quả của sàn dừng lỗ.
    /// </remarks>
    [Fact]
    public void Chi_phi_vuot_tran_van_bi_chan_du_dung_lo_da_qua_san()
    {
        var settings = Settings(s =>
        {
            s.BacktestTakerFeePercent = 0.5m;   // biểu phí đắt gấp 10
            s.BacktestMakerFeePercent = 0.2m;
        });

        // Dừng lỗ 0,5% — rộng hơn sàn 0,4%.
        var result = Evaluate(Plan(entry: 2500m, stop: 2487.5m, target: 2600m), settings);

        Assert.True(result.StopDistanceBps >= settings.MinStopDistancePercent * 100m);
        Assert.True(result.ExpectedCostR > settings.MaxExpectedCostR);
        Assert.False(result.Passed);
    }

    // ── Không được chặn nhầm lệnh lành ──────────────────────────────────

    /// <summary>
    /// Setup có dừng lỗ đủ rộng và phí trong trần vẫn phải qua.
    /// </summary>
    /// <remarks>
    /// Một cổng chặn tất cả cũng "không lỗ đồng nào", và đó là cách hỏng dễ xảy ra nhất khi thêm
    /// ngưỡng tuyệt đối. Mốc lấy từ chính bảng đo: nhóm dừng lỗ ≥0,7% có phí trung bình 0,126R và
    /// là nhóm duy nhất có net R dương.
    /// </remarks>
    [Fact]
    public void Dung_lo_du_rong_va_phi_trong_tran_thi_qua_cong()
    {
        var settings = Settings();
        var result = Evaluate(Plan(entry: 2500m, stop: 2480m, target: 2560m), settings);   // dừng lỗ 0,8%

        Assert.True(result.StopDistanceBps >= settings.MinStopDistancePercent * 100m);
        Assert.True(result.ExpectedCostR <= settings.MaxExpectedCostR,
            $"Chi phí {result.ExpectedCostR:N3}R phải nằm trong trần {settings.MaxExpectedCostR:N2}R.");
        Assert.True(result.Passed);
    }

    // ── Mức chờ thụ động không được gặm mất sàn ─────────────────────────

    /// <summary>
    /// Mức chờ kéo khoảng dừng lỗ hiệu dụng xuống dưới sàn thì bị LOẠI, không được dùng.
    /// </summary>
    /// <remarks>
    /// Đây là lỗ hổng thứ hai, và nó tinh vi hơn lỗ hổng cổng tỉ lệ. Sàn
    /// <c>MinStopDistancePercent</c> được áp trong bộ kích hoạt, đo từ GIÁ LÚC CHẤM. Nhưng lệnh
    /// đi ra sàn tại MỨC CHỜ, và mức chờ nằm về phía dừng lỗ — nên khoảng dừng lỗ thật luôn nhỏ
    /// hơn khoảng đã chấm. Ràng buộc cũ chỉ đòi mức chờ cách dừng lỗ 25% khoảng gốc, tức cho
    /// phép khoảng hiệu dụng co còn một phần tư và khối lượng nở gấp bốn.
    ///
    /// Hai lệnh thật đi qua đúng lối này, cả hai đều được chấm đúng 40,0 bps rồi thu lại:
    /// <code>
    /// #52 BTCUSDT MaPullback → 29,4 bps   (−1,15R)
    /// #65 ETHUSDT MaPullback → 25,1 bps   (−1,17R)
    /// </code>
    ///
    /// Loại mức chờ thì kế hoạch lùi về lệnh thị trường, nơi khoảng dừng lỗ đúng bằng con số đã
    /// chấm — chịu phí taker nhưng giữ được kinh tế mà cổng chi phí đã duyệt.
    /// </remarks>
    [Fact]
    public void Muc_cho_lam_khoang_dung_lo_tut_duoi_san_thi_bi_loai()
    {
        var settings = Settings();
        var planner = new TradeExecutionPlanner();

        // Dựng lại hình dạng của #52: chấm ở 79063,99 với dừng lỗ 78747,73 (đúng 40,0 bps),
        // mức chờ 78979,63 kéo khoảng hiệu dụng còn 29,4 bps.
        var card = new EntryScorecard
        {
            Symbol = "BTCUSDT",
            Direction = TradeDirection.Long,
            SetupType = SetupType.MaPullback,
            Outcome = ScorecardOutcome.Entered,
            SuggestedEntry = 79063.99m,
            SuggestedStopLoss = 78747.73m,
            SuggestedTakeProfit = 79700m,
            SuggestedLimitEntry = 78979.63m,
        };

        var plan = planner.PlanLive(card, settings)!;

        // Lùi về lệnh thị trường tại giá đã chấm, KHÔNG dùng mức chờ.
        var entry = Assert.Single(plan.Entries);
        Assert.False(entry.IsLimit);
        Assert.Equal(79063.99m, entry.Price);

        // Và khoảng dừng lỗ thật đúng bằng khoảng đã chấm.
        var bps = Math.Abs(entry.Price - plan.StopLoss) / entry.Price * 10_000m;
        Assert.True(bps >= settings.MinStopDistancePercent * 100m,
            $"Khoảng dừng lỗ thật {bps:N1} bps phải giữ được sàn {settings.MinStopDistancePercent * 100m:N0} bps.");
    }

    /// <summary>Mức chờ còn giữ được sàn thì vẫn được dùng — không chặn nhầm lệnh chờ lành.</summary>
    [Fact]
    public void Muc_cho_van_giu_duoc_san_thi_van_duoc_dung()
    {
        var settings = Settings();
        var planner = new TradeExecutionPlanner();

        // Chấm ở 80000 với dừng lỗ 79200 (100 bps). Mức chờ 79850 phải qua CẢ HAI ràng buộc:
        //   cách giá  ≥ 15% khoảng dừng lỗ = 120  ⟹  mức chờ ≤ 79880
        //   cách dừng ≥ max(25% khoảng, sàn 40bps) = max(200, 320) = 320  ⟹  mức chờ ≥ 79520
        // Còn lại 650 giá = 81,4 bps — vẫn trên sàn.
        var card = new EntryScorecard
        {
            Symbol = "BTCUSDT",
            Direction = TradeDirection.Long,
            SetupType = SetupType.MaPullback,
            Outcome = ScorecardOutcome.Entered,
            SuggestedEntry = 80000m,
            SuggestedStopLoss = 79200m,
            SuggestedTakeProfit = 82000m,
            SuggestedLimitEntry = 79850m,
        };

        var entry = Assert.Single(planner.PlanLive(card, settings)!.Entries);

        Assert.True(entry.IsLimit);
        Assert.Equal(79850m, entry.Price);
    }

    /// <summary>
    /// Bộ luật không bật cổng V3 thì hai ngưỡng này chỉ ĐO, không chặn.
    /// </summary>
    /// <remarks>
    /// <c>enforceV3Gates</c> là cờ duy nhất quyết định cổng có răng hay không, và nó phải giữ
    /// nguyên nghĩa cũ: V2 vẫn được đo economics để có số liệu đối chiếu, nhưng không bị chặn.
    /// Thêm ngưỡng mới mà quên cờ này sẽ đổi hành vi của một bộ luật không ai định đụng tới.
    /// </remarks>
    [Fact]
    public void Khong_bat_cong_V3_thi_chi_do_chu_khong_chan()
    {
        var settings = Settings(s => s.StrategyVersion = TradingStrategyVersion.AdaptiveV2);
        var result = _viability.Evaluate(
            Card3496(), TradeDirection.Long, settings, enforceV3Gates: false, SetupType.TriangleBreakout);

        Assert.True(result.ExpectedCostR > settings.MaxExpectedCostR);   // vẫn đo được
        Assert.True(result.Passed);                                     // nhưng không chặn
    }
}
