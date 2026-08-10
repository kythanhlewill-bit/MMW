using MMW.Application.Backtest;
using MMW.Application.MarketData.Models;
using MMW.Application.Trading.Execution;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.Backtest;

/// <summary>
/// Bước 1 — thước đo phải đúng TRƯỚC khi tối ưu bất cứ thứ gì.
/// </summary>
/// <remarks>
/// Hai lỗi được chốt chặn ở đây đều thuộc loại không bao giờ tự lộ ra trong báo cáo: kết quả vẫn
/// là một con số hợp lý, chỉ là con số sai. Chúng làm hỏng chính <c>ExpectancyR</c> — thước đo mà
/// mọi quyết định tối ưu về sau dựa vào.
///
/// 1. Chia đều SỐ LƯỢNG giữa các tranche dùng chung một dừng lỗ khiến lệnh scale-in tiêu ít rủi
///    ro hơn ngân sách, và tiêu bao nhiêu thì tuỳ khớp được mấy tranche. Cộng expectancy của nó
///    với lệnh một điểm vào là cộng táo với cam.
/// 2. Bỏ qua phí vốn trên hợp đồng vĩnh cửu. Với dừng lỗ ~0,27% giá, một mốc thanh toán 0,01%
///    tốn ~0,037R — cùng bậc độ lớn với toàn bộ khoảng cách từ baseline tới hoà vốn.
/// </remarks>
public class TrancheRiskAndFundingTests
{
    private static readonly DateTime Start = new(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc);

    private static EngineSetting NoCosts() => new()
    {
        BacktestTakerFeePercent = 0m,
        BacktestMakerFeePercent = 0m,
        BacktestEntrySlippageBps = 0m,
        BacktestStopSlippageBps = 0m,
    };

    private static Candle Candle(decimal low, decimal high, int index = 1)
    {
        var open = Start.AddMinutes(index * 15);
        return new Candle(open, high, high, low, high, 100m, open.AddMinutes(15).AddTicks(-1));
    }

    /// <summary>Hai điểm vào 100 và 95, dừng lỗ 90 — điểm sau nằm gần stop bằng nửa điểm đầu.</summary>
    private static TradeExecutionPlan TwoTranches(decimal firstWeight = 0.5m, decimal target = 200m) => new(
        [
            new PlannedEntryTranche(100m, firstWeight),
            new PlannedEntryTranche(95m, 1m - firstWeight, IsLimit: true),
        ],
        StopLoss: 90m,
        FirstTakeProfit: target,
        RunnerTakeProfit: null,
        FirstTakeProfitFraction: 1m,
        MoveRunnerStopToBreakeven: false,
        Mode: "StrongTrendRunner");

    private static TradeExecutionPlan SingleTranche(
        decimal entry = 100m, decimal stop = 90m, decimal target = 200m) =>
        new([new PlannedEntryTranche(entry, 1m)], stop, target, null, 1m, false, "RangeQuick");

    private static SimulatedTradePosition Open(
        TradeExecutionPlan plan, TradeDirection direction = TradeDirection.Long, decimal sizeR = 1m) =>
        SimulatedTradePosition.Open(
            "BTCUSDT", direction, Start, sizeR, DayRegime.TrendUp, plan, NoCosts());

    // ── Ngân sách rủi ro ────────────────────────────────────────────────

    /// <summary>
    /// Khớp đủ mọi tranche rồi dừng lỗ mất ĐÚNG ngân sách — không hơn, và quan trọng hơn: không kém.
    /// </summary>
    /// <remarks>
    /// V1 chia đều SỐ LƯỢNG nên lệnh này chỉ mất 0,75R: tranche tại 95 chỉ cách stop một nửa
    /// khoảng cách của tranche tại 100, mà lại nắm đúng bằng số hợp đồng. Nghe như "vào lệnh nhẹ
    /// tay", nhưng hệ quả thật là mọi lệnh scale-in bị ghi nhận trên một thang R khác với lệnh
    /// một điểm vào.
    /// </remarks>
    [Fact]
    public void Khop_du_moi_tranche_roi_dung_lo_mat_dung_ngan_sach()
    {
        var position = Open(TwoTranches());

        Assert.False(position.Advance(Candle(low: 94m, high: 101m), NoCosts()));
        Assert.Equal(2, position.Entries.Count(e => e.IsFilled));

        Assert.True(position.Advance(Candle(low: 89m, high: 96m, index: 2), NoCosts()));

        // (90−100)×0,05 + (90−95)×0,10 = −0,5 + −0,5 = −1,0. V1 cho ra −0,75.
        Assert.Equal(-1m, position.RMultiple);
        Assert.Equal(TradeOutcome.Loss, position.Outcome);
    }

    /// <summary>Khớp một phần chỉ triển khai phần ngân sách của các tranche đã khớp.</summary>
    [Fact]
    public void Chi_khop_tranche_dau_thi_chi_an_phan_ngan_sach_cua_no()
    {
        // Giá chạy thẳng tới mục tiêu, không bao giờ lùi về 95.
        var position = Open(TwoTranches(firstWeight: 0.6m, target: 110m));

        Assert.True(position.Advance(Candle(low: 99m, high: 111m), NoCosts()));

        Assert.Single(position.Entries, e => e.IsFilled);

        // Đi hết một đơn vị rủi ro (100 → 110) tạo +1R trên PHẦN đã khớp, nhưng đường vốn chỉ
        // nhận +0,6R vì mới triển khai 60% ngân sách. Hai thước đo không được trộn với nhau.
        Assert.Equal(1m, position.RMultiple);
        Assert.Equal(0.6m, position.RealizedR);
    }

    /// <summary>
    /// Bất đối xứng cố hữu của scale-in: lệnh THUA luôn triển khai đủ ngân sách, lệnh THẮNG thì không.
    /// </summary>
    /// <remarks>
    /// Với dữ liệu OHLC, một tranche pullback nằm giữa giá vào và dừng lỗ KHÔNG THỂ bị bỏ qua
    /// trên đường giá rơi xuống stop — giá phải đi ngang qua nó. Nhưng lệnh thắng chạy thẳng lên
    /// thì không bao giờ khớp tranche đó.
    ///
    /// Nghĩa là: thua đủ 1R, thắng chỉ một phần. Chia đều rủi ro như hiện tại khiến bất đối xứng
    /// này lớn nhất có thể. Đây là lập luận định lượng cho việc dồn trọng số về tranche đầu
    /// (40/35/25) — nhưng chỉ nên làm CÙNG LÚC với state machine ở §7, khi tranche sâu không còn
    /// là lệnh limit đặt mù. Ghi lại ở đây để lần sau không phải phát hiện lại.
    /// </remarks>
    [Fact]
    public void Lenh_thua_luon_trien_khai_du_ngan_sach_con_lenh_thang_thi_khong()
    {
        var losing = Open(TwoTranches(firstWeight: 0.6m, target: 110m));
        Assert.True(losing.Advance(Candle(low: 89m, high: 101m), NoCosts()));
        Assert.Equal(2, losing.Entries.Count(e => e.IsFilled));
        Assert.Equal(-1m, losing.RMultiple);

        var winning = Open(TwoTranches(firstWeight: 0.6m, target: 110m));
        Assert.True(winning.Advance(Candle(low: 99m, high: 111m), NoCosts()));
        Assert.Single(winning.Entries, e => e.IsFilled);
        Assert.Equal(1m, winning.RMultiple);
        Assert.Equal(0.6m, winning.RealizedR);
    }

    [Fact]
    public void Tong_ngan_sach_ti_le_thang_voi_SizeR()
    {
        var position = Open(TwoTranches(), sizeR: 0.4m);

        Assert.False(position.Advance(Candle(low: 94m, high: 101m), NoCosts()));
        Assert.True(position.Advance(Candle(low: 89m, high: 96m, index: 2), NoCosts()));

        Assert.Equal(-1m, position.RMultiple);
        Assert.Equal(-0.4m, position.RealizedR);
    }

    /// <summary>
    /// Cùng ngân sách rủi ro, tranche vào sâu nắm NHIỀU hợp đồng hơn — vào sâu không miễn phí.
    /// </summary>
    [Fact]
    public void Tranche_vao_sau_nam_nhieu_hop_dong_hon_du_cung_trong_so_rui_ro()
    {
        var position = Open(TwoTranches());

        var (near, deep) = (position.Entries[0], position.Entries[1]);

        Assert.Equal(near.RiskWeight, deep.RiskWeight);
        Assert.Equal(10m, near.StopDistance);
        Assert.Equal(5m, deep.StopDistance);

        // Khoảng cách bằng nửa ⟹ khối lượng gấp đôi.
        Assert.Equal(near.Quantity * 2m, deep.Quantity);
    }

    /// <summary>
    /// Hệ quả tiền bạc của điều trên: cùng một ngân sách rủi ro, scale-in tốn NHIỀU phí hơn.
    /// </summary>
    /// <remarks>
    /// Bản review nói "entry gần stop hơn có RR hình học tốt hơn" — đúng, nhưng chỉ đúng ở phần
    /// gộp. Trừ phí ra thì mỗi đơn vị rủi ro ở tranche sâu lại ĐẮT hơn, vì nó cõng nhiều hợp đồng
    /// hơn. Đây là lý do việc chia tranche phải được tính vào chi phí chứ không chỉ vào RR.
    /// </remarks>
    [Fact]
    public void Scale_in_ton_nhieu_phi_hon_lenh_mot_diem_vao_cung_ngan_sach()
    {
        // Maker bằng taker để cô lập ĐÚNG một biến: khối lượng. Chênh lệch maker/taker được đo
        // riêng ở `SimulatedTradePositionTests`; trộn hai hiệu ứng vào một test thì không kết
        // luận được cái nào tạo ra chênh lệch.
        var setting = NoCosts();
        setting.BacktestTakerFeePercent = 0.1m;
        setting.BacktestMakerFeePercent = 0.1m;

        var single = SimulatedTradePosition.Open(
            "BTCUSDT", TradeDirection.Long, Start, 1m, DayRegime.TrendUp, SingleTranche(), setting);
        single.Advance(Candle(low: 89m, high: 101m), setting);

        var scaled = SimulatedTradePosition.Open(
            "BTCUSDT", TradeDirection.Long, Start, 1m, DayRegime.TrendUp, TwoTranches(), setting);
        scaled.Advance(Candle(low: 94m, high: 101m), setting);
        scaled.Advance(Candle(low: 89m, high: 96m, index: 2), setting);

        // Một điểm vào: (100 + 90) × 0,1% × 0,10 = 0,019R.
        Assert.Equal(0.019m, single.FeeR);

        // Hai điểm vào: (100×0,05 + 95×0,10 + 90×0,05 + 90×0,10) × 0,1% = 0,028R.
        Assert.Equal(0.028m, scaled.FeeR);
        Assert.True(scaled.FeeR > single.FeeR);

        // Và cả hai đều mất đúng 1R gộp, nên phần chênh là chi phí thuần.
        Assert.Equal(-1m - scaled.FeeR, scaled.RMultiple);
    }

    // ── Bất biến ở cổng vào ─────────────────────────────────────────────

    [Fact]
    public void Tranche_dat_qua_sat_dung_lo_bi_tu_choi()
    {
        // Điểm đầu 100 / stop 90 ⟹ UnitRisk 10, sàn 2,5. Điểm 92 chỉ cách stop 2.
        var plan = new TradeExecutionPlan(
            [new PlannedEntryTranche(100m, 0.5m), new PlannedEntryTranche(92m, 0.5m)],
            90m, 200m, null, 1m, false, "StrongTrendRunner");

        var error = Assert.Throws<ArgumentException>(() => Open(plan));
        Assert.Contains("khoảng cách", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tranche_nam_sai_phia_dung_lo_bi_tu_choi()
    {
        var plan = new TradeExecutionPlan(
            [new PlannedEntryTranche(100m, 0.5m), new PlannedEntryTranche(85m, 0.5m)],
            90m, 200m, null, 1m, false, "StrongTrendRunner");

        Assert.Throws<ArgumentException>(() => Open(plan));
    }

    [Fact]
    public void Tong_trong_so_rui_ro_khac_1_bi_tu_choi()
    {
        var plan = new TradeExecutionPlan(
            [new PlannedEntryTranche(100m, 0.5m), new PlannedEntryTranche(95m, 0.4m)],
            90m, 200m, null, 1m, false, "StrongTrendRunner");

        Assert.Throws<ArgumentException>(() => Open(plan));
    }

    // ── Phí vốn ─────────────────────────────────────────────────────────

    [Fact]
    public void Long_tra_phi_von_khi_ty_le_duong()
    {
        var position = Open(SingleTranche());

        position.SettleFunding(fundingRate: 0.0001m, markPrice: 100m);

        // 100 × 0,01% × 0,10 = 0,001R tiền ra.
        Assert.Equal(0.001m, position.FundingR);
        Assert.Equal(-0.001m, position.RealizedR);
        Assert.Equal(1, position.FundingSettlements);
    }

    [Fact]
    public void Short_nhan_phi_von_khi_ty_le_duong()
    {
        var position = Open(
            SingleTranche(entry: 100m, stop: 110m, target: 50m), TradeDirection.Short);

        position.SettleFunding(fundingRate: 0.0001m, markPrice: 100m);

        Assert.Equal(-0.001m, position.FundingR);
        Assert.Equal(0.001m, position.RealizedR);
    }

    /// <summary>Phí vốn chỉ tính trên phần khối lượng CÒN mở, không phải khối lượng ban đầu.</summary>
    [Fact]
    public void Phi_von_chi_tinh_tren_phan_khoi_luong_con_mo()
    {
        var plan = new TradeExecutionPlan(
            [new PlannedEntryTranche(100m, 1m)],
            StopLoss: 90m,
            FirstTakeProfit: 110m,
            RunnerTakeProfit: 130m,
            FirstTakeProfitFraction: 0.5m,
            MoveRunnerStopToBreakeven: false,
            Mode: "StrongTrendRunner");
        var position = Open(plan);

        Assert.False(position.Advance(Candle(low: 99m, high: 111m), NoCosts()));
        Assert.True(position.FirstTargetTaken);

        position.SettleFunding(fundingRate: 0.0001m, markPrice: 100m);

        // Còn nửa vị thế ⟹ nửa phí vốn.
        Assert.Equal(0.0005m, position.FundingR);
    }

    [Fact]
    public void Vi_the_da_dong_khong_tra_phi_von()
    {
        var position = Open(SingleTranche());
        Assert.True(position.Advance(Candle(low: 89m, high: 101m), NoCosts()));

        position.SettleFunding(fundingRate: 0.0001m, markPrice: 100m);

        Assert.Equal(0m, position.FundingR);
        Assert.Equal(0, position.FundingSettlements);
        Assert.Equal(-1m, position.RMultiple);
    }

    /// <summary>
    /// Tranche chưa khớp không nắm giữ gì nên không phải trả phí vốn.
    /// </summary>
    [Fact]
    public void Tranche_chua_khop_khong_phai_tra_phi_von()
    {
        var position = Open(TwoTranches());

        position.SettleFunding(fundingRate: 0.0001m, markPrice: 100m);

        // Chỉ tranche đầu (khối lượng 0,05) đang mở.
        Assert.Equal(100m * 0.0001m * 0.05m, position.FundingR);
    }

    /// <summary>
    /// Tỷ lệ âm đảo chiều dòng tiền — Long NHẬN. Bỏ qua dấu sẽ luôn phạt một chiều.
    /// </summary>
    [Fact]
    public void Ty_le_am_dao_chieu_dong_tien()
    {
        var position = Open(SingleTranche());

        position.SettleFunding(fundingRate: -0.0001m, markPrice: 100m);

        Assert.Equal(-0.001m, position.FundingR);
        Assert.Equal(0.001m, position.RealizedR);
    }
}
