using MMW.Application.Trading.Discipline;
using MMW.Application.Trading.Discipline.Gates;
using MMW.Application.Trading.Scoring;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.RuleEngine.Tests.Scoring;
using Xunit;

namespace MMW.RuleEngine.Tests.Discipline;

/// <summary>
/// T107–T112 — sáu rào chắn kỷ luật. Mỗi rào kiểm hai chiều: chặn đúng khi vượt ngưỡng,
/// và KHÔNG chặn nhầm ở ngay dưới ngưỡng.
/// </summary>
/// <remarks>
/// Nửa sau quan trọng ngang nửa đầu. Một rào luôn chặn sẽ qua được một nửa số test, và nó phá
/// hệ thống theo cách khó thấy nhất: không bao giờ vào lệnh, mà zero lệnh lại là kết quả hợp lệ
/// của thiết kế này.
///
/// Gộp sáu tệp test mà tasks.md liệt kê thành một: mỗi rào chỉ có hai đến ba khẳng định, tách
/// ra sáu tệp bốn mươi dòng làm khó đọc hơn chứ không rõ hơn.
/// </remarks>
public class DisciplineGateTests
{
    private static readonly DateTime Now = new(2026, 8, 5, 14, 0, 0, DateTimeKind.Utc);

    private static DisciplineContext Context(
        TraderStatistics? stats = null,
        decimal plannedRisk = 1m,
        int maxTrades = 5,
        Action<EngineSetting>? configure = null,
        Action<RiskSetting>? risk = null)
    {
        var settings = EngineSettingDefaults.Create(1);
        configure?.Invoke(settings);

        var riskSettings = new RiskSetting { TradingAccountId = 1 };
        risk?.Invoke(riskSettings);

        return new DisciplineContext
        {
            TradingAccountId = 1,
            EvaluatedAtUtc = Now,
            Symbol = ScoringFixtures.Symbol,
            Direction = TradeDirection.Long,
            PlannedRiskPercent = plannedRisk,
            DailyPlan = ScoringFixtures.Plan(maxTrades: maxTrades),
            Settings = settings,
            RiskSettings = riskSettings,
            Stats = stats ?? TraderStatistics.Empty,
        };
    }

    private static TraderStatistics Stats(
        int consecutiveLosses = 0,
        decimal dailyLossPercent = 0m,
        DateTime? lastLoss = null,
        decimal? averageRisk = null,
        int tradesToday = 0,
        int closedTrades = 0,
        params int[] worstHours) =>
        new(consecutiveLosses, dailyLossPercent, lastLoss, averageRisk, tradesToday, closedTrades, worstHours);

    // ── T107 discipline.loss_streak ─────────────────────────────────────

    [Fact]
    public void Mot_lenh_thua_khong_tac_dong_gi()
    {
        var result = new LossStreakGate().Evaluate(Context(Stats(consecutiveLosses: 1)));

        Assert.Equal(GateAction.Allow, result.Action);
        Assert.Equal(1.0m, result.SizeMultiplier);
    }

    [Fact]
    public void Hai_lenh_thua_lien_tiep_nhan_kich_thuoc_0_5()
    {
        var result = new LossStreakGate().Evaluate(Context(Stats(consecutiveLosses: 2)));

        Assert.Equal(GateAction.ReduceSize, result.Action);
        Assert.Equal(0.5m, result.SizeMultiplier);
    }

    [Fact]
    public void Ba_lenh_thua_lien_tiep_dung_ngay()
    {
        var stats = Stats(consecutiveLosses: 3) with { ConsecutiveLossesToday = 3 };
        var result = new LossStreakGate().Evaluate(Context(stats));

        Assert.Equal(GateAction.StopForDay, result.Action);
        Assert.Equal(VetoReason.LossStreakStop, result.VetoReason);
    }

    [Fact]
    public void Chuoi_thua_tu_ngay_truoc_chi_giam_size_khong_khoa_vinh_vien()
    {
        var stats = Stats(consecutiveLosses: 3) with { ConsecutiveLossesToday = 0 };
        var result = new LossStreakGate().Evaluate(Context(stats));

        Assert.Equal(GateAction.ReduceSize, result.Action);
        Assert.Equal(0.5m, result.SizeMultiplier);
    }

    [Fact]
    public void Nguong_chuoi_thua_doc_tu_cau_hinh()
    {
        var result = new LossStreakGate().Evaluate(
            Context(Stats(consecutiveLosses: 2), configure: s => s.LossStreakSizeHalveAt = 5));

        Assert.Equal(GateAction.Allow, result.Action);
    }

    // ── T108 discipline.daily_loss_limit ────────────────────────────────

    [Fact]
    public void Cham_gioi_han_lo_ngay_thi_dung_ngay()
    {
        var result = new DailyLossLimitGate().Evaluate(Context(Stats(dailyLossPercent: 3m)));

        Assert.Equal(GateAction.StopForDay, result.Action);
        Assert.Equal(VetoReason.DailyLossStop, result.VetoReason);
    }

    [Fact]
    public void Ngay_duoi_gioi_han_lo_ngay_thi_cho_qua()
    {
        var result = new DailyLossLimitGate().Evaluate(Context(Stats(dailyLossPercent: 2.99m)));

        Assert.Equal(GateAction.Allow, result.Action);
    }

    [Fact]
    public void Gioi_han_lo_ngay_doc_tu_RiskSetting()
    {
        var result = new DailyLossLimitGate().Evaluate(
            Context(Stats(dailyLossPercent: 2m), risk: r => r.MaxDailyLossPercent = 1m));

        Assert.Equal(GateAction.StopForDay, result.Action);
    }

    // ── T109 discipline.revenge_window ──────────────────────────────────

    [Fact]
    public void Muoi_phut_sau_lenh_thua_thi_bi_chan()
    {
        var result = new RevengeWindowGate().Evaluate(Context(Stats(lastLoss: Now.AddMinutes(-10))));

        Assert.Equal(GateAction.BlockTrade, result.Action);
        Assert.Equal(VetoReason.RevengeWindow, result.VetoReason);
    }

    [Fact]
    public void Hai_muoi_phut_sau_lenh_thua_thi_khong_chan()
    {
        var result = new RevengeWindowGate().Evaluate(Context(Stats(lastLoss: Now.AddMinutes(-20))));

        Assert.Equal(GateAction.Allow, result.Action);
    }

    [Fact]
    public void Dung_moc_15_phut_thi_khong_con_chan()
    {
        var result = new RevengeWindowGate().Evaluate(Context(Stats(lastLoss: Now.AddMinutes(-15))));

        Assert.Equal(GateAction.Allow, result.Action);
    }

    [Fact]
    public void Chua_co_lenh_thua_nao_thi_cho_qua()
    {
        Assert.Equal(GateAction.Allow, new RevengeWindowGate().Evaluate(Context()).Action);
    }

    [Fact]
    public void Cua_so_tra_thu_doc_nguong_CHAN_chu_khong_doc_nguong_canh_bao()
    {
        // EngineSetting.RevengeBlockMinutes = 15 (chặn), RiskSetting.RevengeTradeWindowMinutes
        // = 30 (cảnh báo). Ở 20 phút, đọc nhầm sang ngưỡng cảnh báo sẽ chặn oan.
        var context = Context(Stats(lastLoss: Now.AddMinutes(-20)), risk: r => r.RevengeTradeWindowMinutes = 30);

        Assert.Equal(GateAction.Allow, new RevengeWindowGate().Evaluate(context).Action);
    }

    // ── T110 discipline.oversized ───────────────────────────────────────

    [Fact]
    public void Vuot_1_5_lan_trung_binh_thi_tu_co_size_ve_tran()
    {
        var result = new OversizedGate().Evaluate(Context(Stats(averageRisk: 1m), plannedRisk: 1.6m));

        Assert.Equal(GateAction.ReduceSize, result.Action);
        Assert.Equal(0.9375m, result.SizeMultiplier);
        Assert.Null(result.VetoReason);
    }

    [Fact]
    public void Dung_1_5_lan_trung_binh_thi_KHONG_chan()
    {
        // So sánh phải là LỚN HƠN CHẶT. Một lệnh to đúng bằng giới hạn là lệnh trong kế hoạch;
        // chặn nó biến giới hạn thành "không được chạm tới".
        var result = new OversizedGate().Evaluate(Context(Stats(averageRisk: 1m), plannedRisk: 1.5m));

        Assert.Equal(GateAction.Allow, result.Action);
    }

    [Fact]
    public void Chua_co_lich_su_kich_thuoc_thi_cho_qua()
    {
        var result = new OversizedGate().Evaluate(Context(Stats(averageRisk: null), plannedRisk: 99m));

        Assert.Equal(GateAction.Allow, result.Action);
    }

    // ── T111 discipline.max_trades ──────────────────────────────────────

    [Fact]
    public void Du_han_muc_lenh_ngay_thi_bi_chan()
    {
        var result = new MaxTradesGate().Evaluate(Context(Stats(tradesToday: 5), maxTrades: 5));

        Assert.Equal(GateAction.BlockTrade, result.Action);
        Assert.Equal(VetoReason.MaxTradesReached, result.VetoReason);
    }

    [Fact]
    public void Thieu_mot_lenh_thi_van_cho_qua()
    {
        var result = new MaxTradesGate().Evaluate(Context(Stats(tradesToday: 4), maxTrades: 5));

        Assert.Equal(GateAction.Allow, result.Action);
    }

    [Fact]
    public void Han_muc_lay_tu_ke_hoach_ngay_chu_khong_tu_RiskSetting()
    {
        // Ngày cực đoan hạ hạn mức xuống 2; RiskSetting.MaxTradesPerDay vẫn là 5.
        var result = new MaxTradesGate().Evaluate(Context(Stats(tradesToday: 2), maxTrades: 2));

        Assert.Equal(GateAction.BlockTrade, result.Action);
    }

    // ── T112 discipline.worst_hours ─────────────────────────────────────

    [Fact]
    public void Du_50_lenh_va_gio_nam_trong_nhom_te_nhat_thi_tru_10_diem()
    {
        var result = new WorstHoursGate().Evaluate(
            Context(Stats(closedTrades: 50, worstHours: new[] { 14, 3 })));

        Assert.Equal(GateAction.ReduceSize, result.Action);
        Assert.Equal(0.5m, result.SizeMultiplier);
        Assert.Equal(-10, result.ScorePenalty);
    }

    [Fact]
    public void Duoi_50_lenh_thi_cho_qua_voi_phat_0_va_KHONG_thuong_diem()
    {
        // "Chưa đủ dữ liệu để biết giờ này xấu" không giống "giờ này tốt". Thưởng điểm ở đây
        // sẽ khiến tài khoản mới vào lệnh tự tin hơn tài khoản đã có lịch sử.
        var result = new WorstHoursGate().Evaluate(
            Context(Stats(closedTrades: 49, worstHours: new[] { 14, 3 })));

        Assert.Equal(GateAction.Allow, result.Action);
        Assert.Equal(0, result.ScorePenalty);
    }

    [Fact]
    public void Gio_khong_nam_trong_nhom_te_nhat_thi_khong_bi_tru()
    {
        var result = new WorstHoursGate().Evaluate(
            Context(Stats(closedTrades: 100, worstHours: new[] { 3, 21 })));

        Assert.Equal(0, result.ScorePenalty);
    }

    [Fact]
    public void Muc_phat_gio_te_doc_tu_cau_hinh()
    {
        var result = new WorstHoursGate().Evaluate(
            Context(Stats(closedTrades: 50, worstHours: new[] { 14 }), configure: s => s.WorstHoursPenalty = 25));

        Assert.Equal(-25, result.ScorePenalty);
    }

    // ── Lý do phải nêu số liệu (Nguyên tắc I) ───────────────────────────

    [Fact]
    public void Moi_rao_deu_neu_so_lieu_thuc_te_trong_ly_do()
    {
        // "Không đạt" là lý do vô dụng. Mỗi lý do phải chứa ít nhất một con số để trader
        // đối chiếu được với ngưỡng của chính mình.
        var context = Context(Stats(
            consecutiveLosses: 2, dailyLossPercent: 1m, lastLoss: Now.AddMinutes(-5),
            averageRisk: 1m, tradesToday: 3, closedTrades: 60, worstHours: new[] { 14 }));

        foreach (var gate in DisciplineFixtures.AllGates())
        {
            var reason = gate.Evaluate(context).Reason;

            Assert.False(string.IsNullOrWhiteSpace(reason), $"{gate.Key} không nêu lý do.");
            Assert.True(reason.Any(char.IsDigit), $"{gate.Key} nêu lý do không có số liệu nào: {reason}");
        }
    }
}
