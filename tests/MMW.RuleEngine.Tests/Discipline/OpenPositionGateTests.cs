using MMW.Application.Trading.Discipline;
using MMW.Application.Trading.Discipline.Gates;
using MMW.Application.Trading.Scoring;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.RuleEngine.Tests.Scoring;
using Xunit;

namespace MMW.RuleEngine.Tests.Discipline;

/// <summary>
/// Rủi ro danh mục: cùng một ý tưởng không được vào nhiều lần, và các mã đi cùng pha phải cộng
/// dồn rủi ro với nhau.
/// </summary>
public class OpenPositionGateTests
{
    private static readonly DateTime Now = new(2026, 8, 5, 14, 0, 0, DateTimeKind.Utc);

    private static DisciplineContext Context(
        string symbol = "ETHUSDT",
        TradeDirection direction = TradeDirection.Long,
        decimal projectedSizeR = 1m,
        decimal? leaderCorrelation = 0.9m,
        bool isLeader = false,
        Action<EngineSetting>? configure = null,
        params OpenPositionSnapshot[] open)
    {
        var settings = EngineSettingDefaults.Create(1);
        configure?.Invoke(settings);

        return new DisciplineContext
        {
            TradingAccountId = 1,
            EvaluatedAtUtc = Now,
            Symbol = symbol,
            Direction = direction,
            PlannedRiskPercent = 1m,
            ProjectedSizeR = projectedSizeR,
            LeaderCorrelation = leaderCorrelation,
            IsLeaderSymbol = isLeader,
            DailyPlan = ScoringFixtures.Plan(),
            Settings = settings,
            RiskSettings = new RiskSetting { TradingAccountId = 1 },
            Stats = TraderStatistics.Empty with { OpenPositions = open },
        };
    }

    // ── discipline.open_position ────────────────────────────────────────

    /// <summary>
    /// Kịch bản thật đang bị bỏ lọt: một setup tốt chấm đạt nhiều nến liền.
    /// </summary>
    /// <remarks>
    /// Điều kiện tạo ra một phiếu đạt ngưỡng không biến mất sau một nến — BOS đã retest vẫn
    /// retest, EMA vẫn xếp đúng, regime ngày không đổi. Không có rào này thì cùng một ý tưởng
    /// được vào lại ở mỗi nến 15m cho tới khi chạm hạn mức lệnh/ngày.
    /// </remarks>
    [Fact]
    public void Da_co_vi_the_tren_cung_ma_thi_chan_setup_moi()
    {
        var result = new OpenPositionGate().Evaluate(Context(
            symbol: "ETHUSDT",
            open: new OpenPositionSnapshot("ETHUSDT", TradeDirection.Long, 1m)));

        Assert.Equal(GateAction.BlockTrade, result.Action);
        Assert.Equal(VetoReason.PositionAlreadyOpen, result.VetoReason);
    }

    /// <summary>
    /// Chặn cả khi chiều NGƯỢC lại — đó là đảo vị thế, không phải một setup độc lập.
    /// </summary>
    [Fact]
    public void Vi_the_nguoc_chieu_tren_cung_ma_cung_bi_chan()
    {
        var result = new OpenPositionGate().Evaluate(Context(
            symbol: "ETHUSDT",
            direction: TradeDirection.Short,
            open: new OpenPositionSnapshot("ETHUSDT", TradeDirection.Long, 1m)));

        Assert.Equal(GateAction.BlockTrade, result.Action);
    }

    [Fact]
    public void Vi_the_tren_ma_khac_khong_chan_setup_nay()
    {
        var result = new OpenPositionGate().Evaluate(Context(
            symbol: "ETHUSDT",
            open: new OpenPositionSnapshot("BTCUSDT", TradeDirection.Long, 1m)));

        Assert.Equal(GateAction.Allow, result.Action);
    }

    [Fact]
    public void Cham_tran_vi_the_dong_thoi_thi_chan()
    {
        var result = new OpenPositionGate().Evaluate(Context(
            symbol: "SOLUSDT",
            configure: s => s.MaxConcurrentPositions = 2,
            open: new[]
            {
                new OpenPositionSnapshot("BTCUSDT", TradeDirection.Long, 1m),
                new OpenPositionSnapshot("ETHUSDT", TradeDirection.Long, 1m),
            }));

        Assert.Equal(GateAction.BlockTrade, result.Action);
        Assert.Equal(VetoReason.ConcurrentPositionLimit, result.VetoReason);
    }

    [Fact]
    public void Khong_co_vi_the_nao_thi_cho_qua()
    {
        Assert.Equal(GateAction.Allow, new OpenPositionGate().Evaluate(Context()).Action);
    }

    // ── discipline.correlated_exposure ──────────────────────────────────

    /// <summary>
    /// Hai lệnh mua trên hai mã tương quan 0,9 không phải hai lệnh — nó là một lệnh 2R.
    /// </summary>
    [Fact]
    public void Rui_ro_cung_chieu_tren_ma_dong_pha_bi_cong_don()
    {
        var result = new CorrelatedExposureGate().Evaluate(Context(
            symbol: "ETHUSDT",
            direction: TradeDirection.Long,
            projectedSizeR: 1m,
            leaderCorrelation: 0.9m,
            configure: s => s.MaxCorrelatedR = 1.0m,
            open: new OpenPositionSnapshot("BTCUSDT", TradeDirection.Long, 0.6m)));

        // Đã dùng 0,6R; trần 1,0R ⟹ còn chỗ cho 0,4R trên lệnh 1,0R ⟹ hệ số 0,4.
        Assert.Equal(GateAction.ReduceSize, result.Action);
        Assert.Equal(0.4m, result.SizeMultiplier);
    }

    /// <summary>Vị thế NGƯỢC chiều không cộng dồn — nó là phòng hộ, không phải chồng rủi ro.</summary>
    [Fact]
    public void Vi_the_nguoc_chieu_khong_cong_don_rui_ro()
    {
        var result = new CorrelatedExposureGate().Evaluate(Context(
            direction: TradeDirection.Long,
            leaderCorrelation: 0.9m,
            open: new OpenPositionSnapshot("BTCUSDT", TradeDirection.Short, 1m)));

        Assert.Equal(GateAction.Allow, result.Action);
    }

    /// <summary>
    /// Mã đã tách khỏi thị trường thì không cộng dồn — đó là phân tán rủi ro thật.
    /// </summary>
    [Fact]
    public void Ma_tach_khoi_thi_truong_khong_bi_cong_don()
    {
        var result = new CorrelatedExposureGate().Evaluate(Context(
            direction: TradeDirection.Long,
            leaderCorrelation: 0.2m,
            open: new OpenPositionSnapshot("BTCUSDT", TradeDirection.Long, 1m)));

        Assert.Equal(GateAction.Allow, result.Action);
    }

    /// <summary>Chính mã dẫn dắt luôn được coi là đi cùng pha, không cần đo.</summary>
    [Fact]
    public void Ma_dan_dat_luon_duoc_coi_la_dong_pha()
    {
        var result = new CorrelatedExposureGate().Evaluate(Context(
            symbol: "BTCUSDT",
            isLeader: true,
            leaderCorrelation: null,
            projectedSizeR: 1m,
            configure: s => s.MaxCorrelatedR = 1.0m,
            open: new OpenPositionSnapshot("ETHUSDT", TradeDirection.Long, 1m)));

        // Trần đã dùng hết ⟹ không còn chỗ.
        Assert.Equal(GateAction.ReduceSize, result.Action);
        Assert.Equal(0m, result.SizeMultiplier);
    }

    /// <summary>Tương quan ÂM mạnh cũng là rủi ro cùng nguồn, chỉ khác dấu.</summary>
    [Fact]
    public void Tuong_quan_am_manh_van_duoc_coi_la_dong_pha()
    {
        var result = new CorrelatedExposureGate().Evaluate(Context(
            direction: TradeDirection.Long,
            leaderCorrelation: -0.9m,
            projectedSizeR: 1m,
            configure: s => s.MaxCorrelatedR = 1.0m,
            open: new OpenPositionSnapshot("BTCUSDT", TradeDirection.Long, 1m)));

        Assert.Equal(GateAction.ReduceSize, result.Action);
    }

    [Fact]
    public void Duoi_tran_thi_cho_qua_nguyen_size()
    {
        var result = new CorrelatedExposureGate().Evaluate(Context(
            direction: TradeDirection.Long,
            leaderCorrelation: 0.9m,
            projectedSizeR: 0.3m,
            configure: s => s.MaxCorrelatedR = 1.0m,
            open: new OpenPositionSnapshot("BTCUSDT", TradeDirection.Long, 0.5m)));

        Assert.Equal(GateAction.Allow, result.Action);
        Assert.Equal(1.0m, result.SizeMultiplier);
    }
}
