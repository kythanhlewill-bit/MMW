using MMW.Application.Trading.Discipline;
using MMW.Application.Trading.Discipline.Gates;
using MMW.Application.Trading.Scoring;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.RuleEngine.Tests.Scoring;
using Xunit;

namespace MMW.RuleEngine.Tests.Discipline;

/// <summary>
/// Hạn mức trong ngày đếm RIÊNG từng nhóm.
/// </summary>
/// <remarks>
/// Ký quỹ của hai bộ luật đã tách được ngay tại sàn (ví USDT / ví USDC ở chế độ ký quỹ đơn tài
/// sản), nhưng các bộ đếm "trong ngày" thì không tách theo. Dùng chung nghĩa là một lệnh swing
/// thua có thể khoá bộ luật trong ngày tới hết ngày UTC — bị dừng bởi kết quả của một bộ luật
/// khác, trên mã khác, với ví ký quỹ khác, mà lý do dừng không nhắc gì tới nhóm kia.
/// </remarks>
public class StyleScopedLimitTests
{
    private static readonly DateTime Now = new(2026, 8, 25, 14, 0, 0, DateTimeKind.Utc);

    private static DisciplineContext Context(
        TraderStatistics stats,
        TradingStrategyVersion version,
        Action<RiskSetting>? risk = null,
        int planMaxTrades = 5)
    {
        var settings = EngineSettingDefaults.Create(1);
        settings.StrategyVersion = version;

        var riskSetting = new RiskSetting { TradingAccountId = 1 };
        risk?.Invoke(riskSetting);

        return new DisciplineContext
        {
            TradingAccountId = 1,
            EvaluatedAtUtc = Now,
            Symbol = "BTCUSDC",
            Direction = TradeDirection.Long,
            PlannedRiskPercent = 1m,
            DailyPlan = ScoringFixtures.Plan(maxTrades: planMaxTrades),
            Settings = settings,
            RiskSettings = riskSetting,
            Stats = stats,
        };
    }

    private static TraderStatistics Stats(
        int intradayTrades = 0,
        int htfTrades = 0,
        decimal intradayLoss = 0m,
        decimal htfLoss = 0m) =>
        TraderStatistics.Empty with
        {
            TradesTodayByStyle = new Dictionary<TradeStyle, int>
            {
                [TradeStyle.Intraday] = intradayTrades,
                [TradeStyle.HtfSwing] = htfTrades,
            },
            DailyLossPercentByStyle = new Dictionary<TradeStyle, decimal>
            {
                [TradeStyle.Intraday] = intradayLoss,
                [TradeStyle.HtfSwing] = htfLoss,
            },
        };

    // ── discipline.daily_loss_limit ─────────────────────────────────────

    /// <summary>Đây là kịch bản chính: nhóm swing thua không được phép dừng nhóm lệnh ngắn.</summary>
    [Fact]
    public void Lo_ngay_cua_nhom_H4_khong_dung_nhom_lenh_ngan()
    {
        var stats = Stats(intradayLoss: 0m, htfLoss: 9m);

        var intraday = new DailyLossLimitGate().Evaluate(
            Context(stats, TradingStrategyVersion.AdaptiveSidewaysV6));

        Assert.Equal(GateAction.Allow, intraday.Action);
    }

    [Fact]
    public void Lo_ngay_cua_nhom_H4_van_dung_chinh_nhom_H4()
    {
        var stats = Stats(intradayLoss: 0m, htfLoss: 9m);

        var htf = new DailyLossLimitGate().Evaluate(
            Context(stats, TradingStrategyVersion.HtfSwingV7));

        Assert.Equal(GateAction.StopForDay, htf.Action);
        Assert.Equal(VetoReason.DailyLossStop, htf.VetoReason);
    }

    /// <summary>Và chiều ngược lại — nhóm lệnh ngắn thua không khoá đường swing.</summary>
    [Fact]
    public void Lo_ngay_cua_nhom_lenh_ngan_khong_dung_nhom_H4()
    {
        var stats = Stats(intradayLoss: 9m, htfLoss: 0m);

        var htf = new DailyLossLimitGate().Evaluate(
            Context(stats, TradingStrategyVersion.HtfSwingV7));

        Assert.Equal(GateAction.Allow, htf.Action);
    }

    /// <summary>Ngưỡng của nhóm H4 đọc từ cột riêng, không dùng lại cột của nhóm lệnh ngắn.</summary>
    [Fact]
    public void Nguong_lo_ngay_cua_nhom_H4_doc_tu_cot_rieng()
    {
        var stats = Stats(htfLoss: 2m);

        var result = new DailyLossLimitGate().Evaluate(Context(
            stats, TradingStrategyVersion.HtfSwingV7,
            risk: r => { r.MaxDailyLossPercent = 10m; r.MaxDailyLossPercentHtf = 1m; }));

        Assert.Equal(GateAction.StopForDay, result.Action);
    }

    // ── discipline.max_trades ───────────────────────────────────────────

    [Fact]
    public void So_lenh_ngan_da_vao_khong_chiem_suat_cua_nhom_H4()
    {
        var stats = Stats(intradayTrades: 5, htfTrades: 0);

        var htf = new MaxTradesGate().Evaluate(
            Context(stats, TradingStrategyVersion.HtfSwingV7, planMaxTrades: 5));

        Assert.Equal(GateAction.Allow, htf.Action);
    }

    /// <summary>
    /// Nhóm H4 KHÔNG đọc hạn mức của kế hoạch ngày: kế hoạch đó dựng từ chế độ ngày của mã dẫn
    /// dắt để phục vụ sổ lệnh trong ngày, ngân sách lệnh của nó không mô tả gì về một bộ luật
    /// đọc chiều từ cấu trúc 4h của từng mã riêng.
    /// </summary>
    [Fact]
    public void Nhom_H4_doc_han_muc_rieng_chu_khong_doc_ke_hoach_ngay()
    {
        var stats = Stats(htfTrades: 2);

        var result = new MaxTradesGate().Evaluate(Context(
            stats, TradingStrategyVersion.HtfSwingV7,
            risk: r => r.MaxTradesPerDayHtf = 2,
            planMaxTrades: 20));

        Assert.Equal(GateAction.BlockTrade, result.Action);
        Assert.Equal(VetoReason.MaxTradesReached, result.VetoReason);
    }

    [Fact]
    public void Nhom_lenh_ngan_van_doc_ke_hoach_ngay_nhu_cu()
    {
        var stats = Stats(intradayTrades: 5, htfTrades: 99);

        var result = new MaxTradesGate().Evaluate(Context(
            stats, TradingStrategyVersion.AdaptiveSidewaysV6, planMaxTrades: 5));

        Assert.Equal(GateAction.BlockTrade, result.Action);
    }

    // ── tương thích ngược ───────────────────────────────────────────────

    /// <summary>
    /// Thống kê dựng bằng con số TOÀN tài khoản (không ai tách) phải đọc ra đúng con số đó cho
    /// nhóm lệnh ngắn. Rơi về 0 ở đây nghĩa là mọi hạn mức im lặng cho qua.
    /// </summary>
    [Fact]
    public void Thong_ke_khong_tach_thi_quy_het_ve_nhom_lenh_ngan()
    {
        var flat = new TraderStatistics(0, 4.5m, null, null, 7, 0, Array.Empty<int>());

        Assert.Equal(7, flat.TradesTodayOf(TradeStyle.Intraday));
        Assert.Equal(4.5m, flat.DailyLossPercentOf(TradeStyle.Intraday));
        Assert.Equal(0, flat.TradesTodayOf(TradeStyle.HtfSwing));
        Assert.Equal(0m, flat.DailyLossPercentOf(TradeStyle.HtfSwing));
    }
}
