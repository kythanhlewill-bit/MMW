using MMW.Application.Backtest;
using MMW.Application.MarketData.Models;
using MMW.Application.Trading.Execution;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.Backtest;

public class SimulatedTradePositionTests
{
    private static readonly DateTime Start = new(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc);

    private static EngineSetting NoCosts() => new()
    {
        BacktestTakerFeePercent = 0m,
        BacktestMakerFeePercent = 0m,
        BacktestEntrySlippageBps = 0m,
        BacktestStopSlippageBps = 0m,
    };

    private static Candle Candle(decimal low, decimal high, decimal close, int index = 1)
    {
        var open = Start.AddMinutes(index * 15);
        return new Candle(open, close, high, low, close, 100m, open.AddMinutes(15).AddTicks(-1));
    }

    [Fact]
    public void Ba_diem_vao_van_chi_dung_mot_ngan_sach_rui_ro()
    {
        var plan = new TradeExecutionPlan(
            [
                new PlannedEntryTranche(100m, 0.3m),
                new PlannedEntryTranche(95m, 0.3m),
                new PlannedEntryTranche(90m, 0.4m),
            ],
            StopLoss: 80m,
            FirstTakeProfit: 130m,
            RunnerTakeProfit: 160m,
            FirstTakeProfitFraction: 0.5m,
            MoveRunnerStopToBreakeven: true,
            Mode: "StrongTrendRunner");

        var position = SimulatedTradePosition.Open(
            "BTCUSDT", TradeDirection.Long, Start, 1m, DayRegime.TrendUp, plan, NoCosts());

        Assert.Single(position.Entries, e => e.IsFilled);

        // Nến thứ nhất chỉ khớp điểm 95; điểm 90 không khớp.
        Assert.False(position.Advance(Candle(94m, 101m, 98m), NoCosts()));
        Assert.Equal(2, position.Entries.Count(e => e.IsFilled));

        // Chốt 50%, dời stop về giá vốn bình quân theo KHỐI LƯỢNG + đệm 0,05R.
        //
        // Không phải 97,5 (bình quân theo trọng số rủi ro). Điểm 95 nằm gần dừng lỗ hơn nên cùng
        // một ngân sách rủi ro mua được nhiều hợp đồng hơn (0,020 so với 0,015), và giá vốn thật
        // bị kéo về phía nó: (100×0,015 + 95×0,020) / 0,035 ≈ 97,143.
        Assert.False(position.Advance(Candle(96m, 131m, 125m, 2), NoCosts()));
        Assert.True(position.FirstTargetTaken);
        Assert.Equal(3.4m / 0.035m + 1m, position.Stop);
        Assert.True(position.Entries[2].IsCancelled);

        // Runner quay lại mức bảo vệ phí + 0,05R: phần còn lại lãi thêm 0,0175R.
        // 130 chốt một nửa: (130−100)×0,0075 + (130−95)×0,010 = 0,225 + 0,350 = 0,575R.
        Assert.True(position.Advance(Candle(97m, 100m, 98m, 3), NoCosts()));
        Assert.Equal(TradeOutcome.Win, position.Outcome);
        Assert.Equal(0.5925m, position.RealizedR, 24);
        Assert.Equal(0.9875m, position.RMultiple, 24);
        Assert.InRange(position.RMultiple, 0m, 1m);
    }

    [Fact]
    public void R_multiple_khong_phu_thuoc_size_con_duong_von_thi_co()
    {
        var setting = NoCosts();
        var plan = new TradeExecutionPlan(
            [new PlannedEntryTranche(100m, 1m)], 90m, 110m, null, 1m, false, "RangeQuick");
        var small = SimulatedTradePosition.Open(
            "BTCUSDT", TradeDirection.Long, Start, 0.25m, DayRegime.Range, plan, setting, 1m);
        var full = SimulatedTradePosition.Open(
            "BTCUSDT", TradeDirection.Long, Start, 1m, DayRegime.Range, plan, setting, 1m);

        Assert.True(small.Advance(Candle(89m, 101m, 90m), setting));
        Assert.True(full.Advance(Candle(89m, 101m, 90m), setting));

        Assert.Equal(-1m, small.RMultiple);
        Assert.Equal(-1m, full.RMultiple);
        Assert.Equal(-0.25m, small.RealizedR);
        Assert.Equal(-1m, full.RealizedR);
        Assert.Equal(1m, small.PlannedSizeRBeforeDiscipline);
    }

    [Fact]
    public void Vua_khop_limit_vua_cham_target_trong_cung_nen_khong_duoc_tinh_thang_ngay()
    {
        var plan = new TradeExecutionPlan(
            [new PlannedEntryTranche(100m, 0.5m), new PlannedEntryTranche(95m, 0.5m)],
            80m, 130m, 160m, 0.5m, true, "StrongTrendRunner");
        var position = SimulatedTradePosition.Open(
            "BTCUSDT", TradeDirection.Long, Start, 1m, DayRegime.TrendUp, plan, NoCosts());

        Assert.False(position.Advance(Candle(94m, 131m, 100m), NoCosts()));
        Assert.False(position.FirstTargetTaken);
        Assert.Equal(2, position.Entries.Count(e => e.IsFilled));
    }

    [Fact]
    public void Cham_stop_va_target_cung_nen_thi_tinh_stop_truoc()
    {
        var plan = new TradeExecutionPlan(
            [new PlannedEntryTranche(100m, 1m)], 90m, 110m, null, 1m, false, "RangeQuick");
        var position = SimulatedTradePosition.Open(
            "BTCUSDT", TradeDirection.Long, Start, 1m, DayRegime.Range, plan, NoCosts());

        Assert.True(position.Advance(Candle(89m, 111m, 100m), NoCosts()));
        Assert.Equal(TradeOutcome.Loss, position.Outcome);
        Assert.Equal(-1m, position.RMultiple);
    }

    /// <summary>
    /// Phí tính theo LOẠI LỆNH của từng chân, không phải một mức chung.
    /// </summary>
    /// <remarks>
    /// Chốt lời là lệnh limit chờ sẵn ⟹ maker. V1 tính taker cho chân này, và đó là khoản phạt
    /// đánh vào một chuyện không xảy ra: lệnh chờ trong sổ không phải trả phí lấy thanh khoản.
    /// </remarks>
    [Fact]
    public void Chan_thi_truong_chiu_taker_con_chan_chot_loi_limit_chiu_maker()
    {
        var setting = NoCosts();
        setting.BacktestTakerFeePercent = 0.1m;
        setting.BacktestMakerFeePercent = 0.02m;

        var plan = new TradeExecutionPlan(
            [new PlannedEntryTranche(100m, 1m)], 90m, 110m, null, 1m, false, "RangeQuick");
        var position = SimulatedTradePosition.Open(
            "BTCUSDT", TradeDirection.Long, Start, 1m, DayRegime.Range, plan, setting);

        Assert.True(position.Advance(Candle(99m, 111m, 110m), setting));

        // Vào bằng lệnh thị trường: 100 × 0,1% × 0,10 = 0,010R (taker).
        // Ra bằng lệnh limit:       110 × 0,02% × 0,10 = 0,0022R (maker).
        Assert.Equal(0.010m, position.TakerFeeR);
        Assert.Equal(0.0022m, position.MakerFeeR);
        Assert.Equal(0.9878m, position.RMultiple);
    }

    /// <summary>Dừng lỗ là lệnh stop-market ⟹ taker cả hai chân, không có phần maker nào.</summary>
    [Fact]
    public void Chan_dung_lo_chiu_taker_vi_no_la_lenh_thi_truong()
    {
        var setting = NoCosts();
        setting.BacktestTakerFeePercent = 0.1m;
        setting.BacktestMakerFeePercent = 0.02m;

        var plan = new TradeExecutionPlan(
            [new PlannedEntryTranche(100m, 1m)], 90m, 110m, null, 1m, false, "RangeQuick");
        var position = SimulatedTradePosition.Open(
            "BTCUSDT", TradeDirection.Long, Start, 1m, DayRegime.Range, plan, setting);

        Assert.True(position.Advance(Candle(89m, 101m, 90m), setting));

        Assert.Equal(0m, position.MakerFeeR);
        Assert.Equal(0.019m, position.TakerFeeR);   // 100 × 0,1% × 0,1 + 90 × 0,1% × 0,1
        Assert.Equal(-1.019m, position.RMultiple);
    }

    [Fact]
    public void Qua_16_nen_chua_tung_dat_nua_R_thi_dong_bang_time_stop()
    {
        var setting = NoCosts();
        setting.TimeStopBars = 16;
        setting.TimeStopMinR = 0.5m;
        var plan = new TradeExecutionPlan(
            [new PlannedEntryTranche(100m, 1m)], 90m, 120m, null, 1m, false, "Standard");
        var position = SimulatedTradePosition.Open(
            "BTCUSDT", TradeDirection.Long, Start, 1m, DayRegime.TrendUp, plan, setting);

        for (var i = 1; i < 16; i++)
            Assert.False(position.Advance(Candle(98m, 104m, 101m, i), setting));

        Assert.True(position.Advance(Candle(98m, 104m, 101m, 16), setting));
        Assert.Equal(BacktestExitReason.TimeStop, position.ExitReason);
        Assert.Equal(0.1m, position.RMultiple);
    }

    [Fact]
    public void Da_tung_dat_nua_R_thi_khong_bi_time_stop_du_gia_quay_lai()
    {
        var setting = NoCosts();
        setting.TimeStopBars = 2;
        setting.TimeStopMinR = 0.5m;
        var plan = new TradeExecutionPlan(
            [new PlannedEntryTranche(100m, 1m)], 90m, 120m, null, 1m, false, "Standard");
        var position = SimulatedTradePosition.Open(
            "BTCUSDT", TradeDirection.Long, Start, 1m, DayRegime.TrendUp, plan, setting);

        Assert.False(position.Advance(Candle(99m, 106m, 104m, 1), setting));
        Assert.False(position.Advance(Candle(99m, 102m, 100m, 2), setting));
        Assert.False(position.IsClosed);
        Assert.Equal(0.6m, position.MaxFavorableExcursionR);
    }

    [Fact]
    public void Ly_do_thoat_phan_biet_target_stop_va_cuoi_ky()
    {
        var setting = NoCosts();
        var plan = new TradeExecutionPlan(
            [new PlannedEntryTranche(100m, 1m)], 90m, 110m, null, 1m, false, "RangeQuick");

        var target = SimulatedTradePosition.Open(
            "BTCUSDT", TradeDirection.Long, Start, 1m, DayRegime.Range, plan, setting);
        target.Advance(Candle(99m, 111m, 110m), setting);

        var stop = SimulatedTradePosition.Open(
            "BTCUSDT", TradeDirection.Long, Start, 1m, DayRegime.Range, plan, setting);
        stop.Advance(Candle(89m, 101m, 90m), setting);

        var end = SimulatedTradePosition.Open(
            "BTCUSDT", TradeDirection.Long, Start, 1m, DayRegime.Range, plan, setting);
        end.CloseAtMarket(Candle(99m, 101m, 100m), setting);

        Assert.Equal(BacktestExitReason.Target, target.ExitReason);
        Assert.Equal(BacktestExitReason.Stop, stop.ExitReason);
        Assert.Equal(BacktestExitReason.EndOfPeriod, end.ExitReason);
    }

    [Fact]
    public void Tranche_dung_chinh_xac_bien_025R_khong_bi_tu_choi_vi_sai_so_decimal()
    {
        var setting = NoCosts();
        var entry = 235.19752119685805649497592630m;
        var stop = 233.70750908436773849784682556m;
        var unitRisk = entry - stop;
        var boundary = stop + unitRisk * 0.25m;
        var plan = new TradeExecutionPlan(
            [
                new PlannedEntryTranche(entry, 0.6m),
                new PlannedEntryTranche(boundary, 0.4m, IsLimit: true),
            ],
            stop, entry + unitRisk * 1.5m, entry + unitRisk * 2m,
            0.5m, true, "StrongTrendRunner");

        var position = SimulatedTradePosition.Open(
            "ETHUSDT", TradeDirection.Long, Start, 1m, DayRegime.TrendUp, plan, setting);

        Assert.NotNull(position);
    }
}
