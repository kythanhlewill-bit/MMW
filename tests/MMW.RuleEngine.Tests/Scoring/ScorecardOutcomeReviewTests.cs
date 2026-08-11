using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MMW.Application.Backtest;
using MMW.Application.MarketData.Models;
using MMW.Application.Services;
using MMW.Domain.DbContext;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.Scoring;

/// <summary>Kho nến giả — trả đúng chuỗi được nạp, không đụng cơ sở dữ liệu.</summary>
internal sealed class StubArchiveReader : IKlineArchiveReader
{
    private readonly IReadOnlyList<Candle> _candles;
    public StubArchiveReader(IReadOnlyList<Candle> candles) => _candles = candles;

    public Task<IReadOnlyList<Candle>> GetRangeAsync(
        string symbol, string interval, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Candle>>(
            _candles.Where(c => c.OpenTime >= fromUtc && c.OpenTime < toUtc).ToList());

    public Task<IReadOnlyList<(DateTime From, DateTime To)>> FindGapsAsync(
        string symbol, string interval, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<(DateTime, DateTime)>>(Array.Empty<(DateTime, DateTime)>());

    public Task<FundingRateArchive?> GetFundingAtAsync(
        string symbol, DateTime atUtc, CancellationToken ct = default)
        => Task.FromResult<FundingRateArchive?>(null);
}

/// <summary>
/// Kết cục thực tế của phiếu: ba quy ước dễ gãy nhất và một bất biến về tính lặp lại.
/// </summary>
public class ScorecardOutcomeReviewTests
{
    private static readonly DateTime Decision = new(2026, 8, 11, 14, 0, 0, DateTimeKind.Utc);

    private static Candle Bar(DateTime open, decimal high, decimal low) =>
        new(open, Open: 100m, High: high, Low: low, Close: 100m, Volume: 1m,
            CloseTime: open.AddMinutes(15).AddMilliseconds(-1));

    private static MmwDbContext NewDb() =>
        new(new DbContextOptionsBuilder<MmwDbContext>()
            .UseInMemoryDatabase("mmw_outcome_" + Guid.NewGuid()).Options);

    /// <summary>Phiếu Long: vào 100, dừng 98, mục tiêu 104 ⟹ 1R = 2 giá, mục tiêu = 2R.</summary>
    private static async Task<MmwDbContext> SeedAsync()
    {
        var db = NewDb();
        db.TradingAccounts.Add(new TradingAccount { Id = 1, Name = "test", IsActive = true });
        db.EngineSettings.Add(new EngineSetting { Id = 1, TradingAccountId = 1 });
        db.EntryScorecards.Add(new EntryScorecard
        {
            Id = 1,
            TradingAccountId = 1,
            Symbol = "BTCUSDT",
            Interval = "1h",
            EvaluatedAtUtc = Decision,
            CandleCloseTimeUtc = Decision,
            Direction = TradeDirection.Long,
            Outcome = ScorecardOutcome.Vetoed,
            VetoReason = VetoReason.HtfMisaligned,
            SuggestedEntry = 100m,
            SuggestedStopLoss = 98m,
            SuggestedFirstTakeProfit = 104m,
        });
        await db.SaveChangesAsync();
        return db;
    }

    private static ScorecardOutcomeReviewService Service(
        MmwDbContext db, IReadOnlyList<Candle> candles, DateTime now) =>
        new(db, new StubArchiveReader(candles), new TestClock(now),
            NullLogger<ScorecardOutcomeReviewService>.Instance);

    // ── Quy ước 1: cùng một nến chạm cả hai ⟹ STOP ──────────────────────

    [Fact]
    public async Task Cung_mot_nen_cham_ca_stop_lan_muc_tieu_thi_tinh_STOP()
    {
        // OHLC không nói cái nào đến trước. Chọn phía có lợi là tự bơm kết quả lên, và bơm đúng
        // chỗ không ai kiểm lại được. Quy ước này phải khớp SimulatedTradePosition.
        await using var db = await SeedAsync();
        var candles = new[] { Bar(Decision.AddMinutes(15), high: 105m, low: 97m) };

        var created = await Service(db, candles, Decision.AddDays(2)).ResolvePendingAsync(Decision.AddDays(2));

        Assert.Equal(1, created);
        var review = await db.ScorecardOutcomeReviews.SingleAsync();
        Assert.Equal(ScorecardReviewOutcome.Stop, review.Outcome);
        Assert.True(review.NetR < 0m);
    }

    // ── Quy ước 2: không nhìn trộm ──────────────────────────────────────

    [Fact]
    public async Task Nen_mo_TRUOC_thoi_diem_quyet_dinh_bi_bo_qua()
    {
        // Nến trước quyết định chạm mục tiêu; nếu bị tính thì phiếu thành Target một cách gian lận.
        await using var db = await SeedAsync();
        var candles = new[]
        {
            Bar(Decision.AddMinutes(-15), high: 110m, low: 99m),   // phải bị bỏ
            Bar(Decision.AddMinutes(15),  high: 101m, low: 97m),   // chạm stop
        };

        await Service(db, candles, Decision.AddDays(2)).ResolvePendingAsync(Decision.AddDays(2));

        var review = await db.ScorecardOutcomeReviews.SingleAsync();
        Assert.Equal(ScorecardReviewOutcome.Stop, review.Outcome);
        Assert.Equal(Decision.AddMinutes(15), review.FirstBarUtc);
    }

    // ── Quy ước 3: thiếu nến KHÔNG được biến thành kết quả ───────────────

    [Fact]
    public async Task Thieu_nen_thi_KHONG_sinh_ban_ghi()
    {
        // Đây là chỗ dễ nói dối nhất: đóng cưỡng bức khi mới có vài nến sẽ biến "chưa đo được"
        // thành "đã đo và giá đi ngang", rồi con số đó trôi thẳng vào thống kê.
        await using var db = await SeedAsync();
        var candles = new[]
        {
            Bar(Decision.AddMinutes(15), high: 101m, low: 99m),
            Bar(Decision.AddMinutes(30), high: 101m, low: 99m),
        };

        var created = await Service(db, candles, Decision.AddDays(2)).ResolvePendingAsync(Decision.AddDays(2));

        Assert.Equal(0, created);
        Assert.Empty(db.ScorecardOutcomeReviews);
    }

    [Fact]
    public async Task Du_nen_ma_khong_cham_gi_thi_ghi_OpenAtHorizon()
    {
        await using var db = await SeedAsync();
        var candles = Enumerable.Range(1, 96)
            .Select(i => Bar(Decision.AddMinutes(15 * i), high: 101m, low: 99m))
            .ToArray();

        await Service(db, candles, Decision.AddDays(2)).ResolvePendingAsync(Decision.AddDays(2));

        var review = await db.ScorecardOutcomeReviews.SingleAsync();
        Assert.Equal(ScorecardReviewOutcome.OpenAtHorizon, review.Outcome);
    }

    // ── Chạm mục tiêu và kinh tế ────────────────────────────────────────

    [Fact]
    public async Task Cham_muc_tieu_cho_grossR_duong_va_netR_thap_hon_grossR()
    {
        // netR luôn phải nhỏ hơn grossR: phí và trượt giá chỉ đi một chiều.
        await using var db = await SeedAsync();
        var candles = new[] { Bar(Decision.AddMinutes(15), high: 105m, low: 99.5m) };

        await Service(db, candles, Decision.AddDays(2)).ResolvePendingAsync(Decision.AddDays(2));

        var review = await db.ScorecardOutcomeReviews.SingleAsync();
        Assert.Equal(ScorecardReviewOutcome.Target, review.Outcome);
        Assert.True(review.GrossR > 0m);
        Assert.True(review.NetR < review.GrossR);
        Assert.Equal(2m, review.StopDistancePercent);   // |100-98|/100
    }

    // ── Chạy lại không nhân đôi ─────────────────────────────────────────

    [Fact]
    public async Task Chay_lai_KHONG_sinh_ban_ghi_thu_hai()
    {
        // Job chạy mỗi giờ. Không có bất biến này thì mọi thống kê tự nhân lên theo số lần chạy.
        await using var db = await SeedAsync();
        var candles = new[] { Bar(Decision.AddMinutes(15), high: 105m, low: 99.5m) };
        var service = Service(db, candles, Decision.AddDays(2));

        var first = await service.ResolvePendingAsync(Decision.AddDays(2));
        var second = await service.ResolvePendingAsync(Decision.AddDays(2));

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Single(db.ScorecardOutcomeReviews);
    }

    // ── Phiếu tự mâu thuẫn thì bỏ, không ném ────────────────────────────

    [Fact]
    public async Task Phieu_co_entry_sai_phia_stop_bi_bo_qua_chu_khong_lam_gay_job()
    {
        await using var db = await SeedAsync();
        var card = await db.EntryScorecards.SingleAsync();
        card.SuggestedStopLoss = 102m;   // Long mà stop nằm TRÊN entry
        await db.SaveChangesAsync();

        var candles = new[] { Bar(Decision.AddMinutes(15), high: 105m, low: 99.5m) };
        var created = await Service(db, candles, Decision.AddDays(2)).ResolvePendingAsync(Decision.AddDays(2));

        Assert.Equal(0, created);
        Assert.Empty(db.ScorecardOutcomeReviews);
    }
}
