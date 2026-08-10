using Microsoft.Extensions.DependencyInjection;
using MMW.Application.Trading.Discipline;
using MMW.Application.Trading.Discipline.Gates;
using MMW.Application.Trading.Scoring;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.RuleEngine.Tests.TimeGuard;
using Xunit;

namespace MMW.RuleEngine.Tests.Discipline;

/// <summary>
/// T114 / FR-024 — bộ đếm ngày reset tại 00:00 UTC, và vị thế đang mở KHÔNG bị ảnh hưởng.
/// </summary>
/// <remarks>
/// Nửa sau là phần dễ làm sai nhất. "Reset ngày" nghe như một hành động, và một cài đặt vô ý
/// có thể đóng hoặc đánh dấu lại các lệnh đang mở lúc nửa đêm. Reset ở đây chỉ là bộ đếm đổi
/// mốc thời gian — vị thế mở qua đêm là chuyện bình thường và phải nguyên vẹn.
/// </remarks>
public class DayResetTests
{
    private static readonly DateTime Yesterday = new(2026, 8, 4, 20, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime BeforeMidnight = new(2026, 8, 4, 23, 59, 0, DateTimeKind.Utc);
    private static readonly DateTime AfterMidnight = new(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc);

    private static Trade Closed(long accountId, DateTime openedAt, TradeOutcome outcome, decimal pnl) => new()
    {
        TradingAccountId = accountId,
        Symbol = "BTCUSDT",
        Direction = TradeDirection.Long,
        Status = TradeStatus.Closed,
        EntryPrice = 100m,
        Quantity = 1m,
        RiskPercent = 1m,
        OpenedAt = openedAt,
        ClosedAt = openedAt.AddHours(1),
        Outcome = outcome,
        RealizedPnl = pnl,
    };

    private static async Task<TraderStatistics> StatsAsync(TimeGuardHarness harness, DateTime at)
    {
        using var scope = harness.NewScope();
        var provider = scope.ServiceProvider.GetRequiredService<ITraderStatisticsProvider>();
        return await provider.GetAsync(harness.AccountId, at);
    }

    [Fact]
    public async Task Bo_dem_so_lenh_trong_ngay_reset_tai_00_00_UTC()
    {
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddClosedTradesAsync(new[]
        {
            Closed(harness.AccountId, Yesterday, TradeOutcome.Win, 10m),
            Closed(harness.AccountId, Yesterday.AddHours(1), TradeOutcome.Win, 10m),
        });

        Assert.Equal(2, (await StatsAsync(harness, BeforeMidnight)).TradesToday);
        Assert.Equal(0, (await StatsAsync(harness, AfterMidnight)).TradesToday);
    }

    [Fact]
    public async Task Phan_tram_lo_ngay_reset_tai_00_00_UTC()
    {
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddClosedTradesAsync(new[]
        {
            Closed(harness.AccountId, Yesterday, TradeOutcome.Loss, -50m),
        });

        Assert.True((await StatsAsync(harness, BeforeMidnight)).DailyLossPercent > 0m);
        Assert.Equal(0m, (await StatsAsync(harness, AfterMidnight)).DailyLossPercent);
    }

    [Fact]
    public async Task Trang_thai_dung_ngay_tu_het_hieu_luc_sau_nua_dem()
    {
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddClosedTradesAsync(new[]
        {
            Closed(harness.AccountId, Yesterday, TradeOutcome.Loss, -100m),
        });

        var runner = new DisciplineGateRunner(DisciplineFixtures.AllGates());

        DisciplineContext Context(DateTime at, TraderStatistics stats) => new()
        {
            TradingAccountId = harness.AccountId,
            EvaluatedAtUtc = at,
            Symbol = Scoring.ScoringFixtures.Symbol,
            Direction = TradeDirection.Long,
            PlannedRiskPercent = 1m,
            DailyPlan = Scoring.ScoringFixtures.Plan(),
            Settings = EngineSettingDefaults.Create(harness.AccountId),
            RiskSettings = new RiskSetting { TradingAccountId = harness.AccountId, MaxDailyLossPercent = 3m },
            Stats = stats,
        };

        var before = runner.Run(Context(BeforeMidnight, await StatsAsync(harness, BeforeMidnight)));
        var after = runner.Run(Context(AfterMidnight, await StatsAsync(harness, AfterMidnight)));

        Assert.Equal(VetoReason.DailyLossStop, before.Aggregate.VetoReason);
        Assert.NotEqual(VetoReason.DailyLossStop, after.Aggregate.VetoReason);
    }

    [Fact]
    public async Task Chuoi_thua_KHONG_reset_theo_ngay()
    {
        // Bộ đếm ngày reset, nhưng chuỗi thua liên tiếp thì không: thua ba lệnh cuối phiên hôm
        // qua rồi mở lại lúc 00:01 vẫn là cùng một trạng thái tâm lý, và đó chính là lúc rào
        // này cần đứng vững nhất.
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddClosedTradesAsync(new[]
        {
            Closed(harness.AccountId, Yesterday, TradeOutcome.Loss, -10m),
            Closed(harness.AccountId, Yesterday.AddHours(1), TradeOutcome.Loss, -10m),
            Closed(harness.AccountId, Yesterday.AddHours(2), TradeOutcome.Loss, -10m),
        });

        var stats = await StatsAsync(harness, AfterMidnight);
        Assert.Equal(3, stats.ConsecutiveLosses);
        Assert.Equal(0, stats.ConsecutiveLossesToday);

        var result = new LossStreakGate().Evaluate(new DisciplineContext
        {
            TradingAccountId = harness.AccountId,
            EvaluatedAtUtc = AfterMidnight,
            Symbol = Scoring.ScoringFixtures.Symbol,
            Direction = TradeDirection.Long,
            PlannedRiskPercent = 1m,
            DailyPlan = Scoring.ScoringFixtures.Plan(),
            Settings = EngineSettingDefaults.Create(harness.AccountId),
            RiskSettings = new RiskSetting { TradingAccountId = harness.AccountId },
            Stats = stats,
        });
        Assert.Equal(GateAction.ReduceSize, result.Action);
    }

    [Fact]
    public async Task Vi_the_dang_mo_khong_bi_dung_toi_khi_sang_ngay_moi()
    {
        using var harness = await TimeGuardHarness.CreateAsync();
        var open = new Trade
        {
            TradingAccountId = harness.AccountId,
            Symbol = "BTCUSDT",
            Direction = TradeDirection.Long,
            Status = TradeStatus.Open,
            EntryPrice = 100m,
            StopLoss = 90m,
            Quantity = 1m,
            OpenedAt = Yesterday,
        };
        await harness.AddClosedTradesAsync(new[] { open });

        await StatsAsync(harness, AfterMidnight);

        using var scope = harness.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<Domain.DbContext.MmwDbContext>();
        var stored = db.Trades.Single();

        Assert.Equal(TradeStatus.Open, stored.Status);
        Assert.Null(stored.ClosedAt);
        Assert.Equal(90m, stored.StopLoss);
    }

    [Fact]
    public async Task Lenh_dang_mo_van_duoc_tinh_vao_so_lenh_trong_ngay()
    {
        // Đã vào lệnh thì đã dùng một suất hạn mức, dù nó chưa đóng. Chỉ đếm lệnh ĐÃ ĐÓNG sẽ
        // cho phép mở vô hạn vị thế cùng lúc.
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddClosedTradesAsync(new[]
        {
            new Trade
            {
                TradingAccountId = harness.AccountId, Symbol = "BTCUSDT",
                Direction = TradeDirection.Long, Status = TradeStatus.Open,
                EntryPrice = 100m, Quantity = 1m, OpenedAt = BeforeMidnight.AddHours(-2),
            },
        });

        Assert.Equal(1, (await StatsAsync(harness, BeforeMidnight)).TradesToday);
    }

    [Fact]
    public async Task Lenh_moi_len_ke_hoach_chua_dung_suat_han_muc()
    {
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddClosedTradesAsync(new[]
        {
            new Trade
            {
                TradingAccountId = harness.AccountId, Symbol = "BTCUSDT",
                Direction = TradeDirection.Long, Status = TradeStatus.Planned,
                EntryPrice = 100m, Quantity = 1m, OpenedAt = BeforeMidnight.AddHours(-2),
            },
        });

        Assert.Equal(0, (await StatsAsync(harness, BeforeMidnight)).TradesToday);
    }

    [Fact]
    public async Task Lai_trong_ngay_cho_phan_tram_lo_bang_0_chu_khong_am()
    {
        // "Lỗ âm" là khái niệm vô nghĩa và sẽ làm phép so sánh ở rào đọc ngược.
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddClosedTradesAsync(new[]
        {
            Closed(harness.AccountId, BeforeMidnight.AddHours(-3), TradeOutcome.Win, 200m),
        });

        Assert.Equal(0m, (await StatsAsync(harness, BeforeMidnight)).DailyLossPercent);
    }

    [Fact]
    public async Task Trung_binh_oversize_khoi_phuc_rui_ro_truoc_ky_luat_de_khong_tu_co()
    {
        using var harness = await TimeGuardHarness.CreateAsync();

        using (var scope = harness.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Domain.DbContext.MmwDbContext>();
            var scorecard = new EntryScorecard
            {
                TradingAccountId = harness.AccountId,
                Symbol = "BTCUSDT",
                Interval = "15m",
                CandleCloseTimeUtc = Yesterday,
                EvaluatedAtUtc = Yesterday,
                Outcome = ScorecardOutcome.Entered,
                DisciplineMultiplier = 0.5m,
            };
            db.EntryScorecards.Add(scorecard);
            await db.SaveChangesAsync();

            var trade = Closed(harness.AccountId, Yesterday, TradeOutcome.Loss, -10m);
            trade.RiskPercent = 0.5m;
            trade.EntryScorecardId = scorecard.Id;
            db.Trades.Add(trade);
            await db.SaveChangesAsync();
        }

        var stats = await StatsAsync(harness, AfterMidnight);

        Assert.Equal(1m, stats.AverageRiskRecent);
    }
}
