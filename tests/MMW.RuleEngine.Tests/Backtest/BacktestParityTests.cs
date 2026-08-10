using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using MMW.Application.Backtest;
using MMW.Application.Backtest.Models;
using MMW.Application.Services;
using MMW.Application.Trading.DailyPlanning;
using MMW.Domain.DbContext;
using MMW.Domain.Entities;
using MMW.RuleEngine.Tests.Constitution;
using Xunit;

namespace MMW.RuleEngine.Tests.Backtest;

/// <summary>
/// T128 / SC-003 — chuỗi phiếu từ <see cref="BacktestEngine"/> trùng khớp mọi trường với chuỗi
/// từ <see cref="ISignalEvalService"/> chạy chế độ mô phỏng trên cùng dữ liệu.
/// </summary>
/// <remarks>
/// Test đắt nhất và có giá trị cao nhất của cả feature. Nó đỏ đúng vào lúc ai đó vô tình thêm
/// một nhánh mã riêng cho kiểm thử — thời điểm mà mọi con số kiểm thử bắt đầu nói dối.
///
/// Hai phía được lái bằng HAI vòng lặp khác nhau: một do <c>BacktestEngine</c> điều khiển, một
/// do chính test đẩy đồng hồ và gọi service. Nếu chỉ so kết quả của cùng một vòng lặp với chính
/// nó thì test luôn xanh và chẳng chứng minh điều gì.
/// </remarks>
public class BacktestParityTests
{
    private static readonly DateTime Start = new(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc);
    private const string Symbol = "BTCUSDT";
    private const int CandleCount = 400;   // hơn bốn ngày nến 15m

    /// <param name="Lines">
    /// Các dòng tiêu chí, nối thành MỘT chuỗi.
    /// </param>
    /// <remarks>
    /// Phải là chuỗi chứ không phải danh sách: record so sánh <c>IReadOnlyList</c> theo THAM
    /// CHIẾU, nên hai lần chạy sẽ luôn "khác nhau" dù mọi con số trùng khớp — và thông báo lỗi
    /// sẽ chỉ in ra tên kiểu danh sách, hoàn toàn không đọc được.
    /// </remarks>
    private sealed record Snapshot(
        string Symbol,
        DateTime CandleCloseTimeUtc,
        int TotalScore,
        int TechnicalScore,
        int MarketScore,
        int LiquidityScore,
        int DisciplinePenalty,
        string Outcome,
        string? VetoReason,
        string StrategyVersion,
        string SetupType,
        string TriggerState,
        decimal? ExpectedCostR,
        decimal? NetRiskReward,
        decimal FinalSizeR,
        string Lines);

    private static Snapshot Capture(EntryScorecard c) => new(
        c.Symbol, c.CandleCloseTimeUtc, c.TotalScore, c.TechnicalScore, c.MarketScore,
        c.LiquidityScore, c.DisciplinePenalty, c.Outcome.ToString(), c.VetoReason?.ToString(),
        c.StrategyVersion.ToString(), c.SetupType.ToString(), c.TriggerState.ToString(),
        c.ExpectedCostR, c.NetRiskReward, c.FinalSizeR,
        string.Join(" | ", c.Lines
            .Where(l => !BacktestLimitations.ParityExclusions.Contains(l.CriterionKey))
            .OrderBy(l => l.CriterionKey, StringComparer.Ordinal)
            .Select(l => $"{l.CriterionKey}={l.AwardedPoints}:{l.StateCode}")));

    private static async Task<BacktestReport> RunReportAsync(bool collectTelemetry)
    {
        var candles = BacktestHarness.Series(Start, CandleCount);
        using var harness = await BacktestHarness.CreateAsync(Start, candles);
        using var scope = harness.NewScope();
        var engine = scope.ServiceProvider.GetRequiredService<IBacktestEngine>();
        return await engine.RunAsync(new BacktestRequest(
            "telemetry-parity", Start, candles[^1].CloseTime, new[] { Symbol }, harness.AccountId,
            CollectTelemetry: collectTelemetry));
    }

    private static async Task<List<Snapshot>> ReadAsync(BacktestHarness harness)
    {
        using var scope = harness.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();

        var cards = await db.EntryScorecards
            .AsNoTracking()
            .Include(c => c.Lines)
            .OrderBy(c => c.CandleCloseTimeUtc)
            .ThenBy(c => c.Symbol)
            .ToListAsync();

        return cards.Select(Capture).ToList();
    }

    /// <summary>Phía A: để <c>BacktestEngine</c> tự lái vòng lặp.</summary>
    private static async Task<List<Snapshot>> RunEngineAsync()
    {
        var candles = BacktestHarness.Series(Start, CandleCount);
        using var harness = await BacktestHarness.CreateAsync(Start, candles);

        using (var scope = harness.NewScope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<IBacktestEngine>();
            await engine.RunAsync(new BacktestRequest(
                "parity", Start, candles[^1].CloseTime, new[] { Symbol }, harness.AccountId,
                PersistScorecards: true));
        }

        return await ReadAsync(harness);
    }

    /// <summary>Phía B: test tự đẩy đồng hồ và gọi thẳng service, không qua engine.</summary>
    private static async Task<List<Snapshot>> RunManualAsync()
    {
        var candles = BacktestHarness.Series(Start, CandleCount);
        using var harness = await BacktestHarness.CreateAsync(Start, candles);

        var step = TimeSpan.FromMinutes(15);
        var lastPlanDate = default(DateOnly?);

        for (var closeAt = Start + step; closeAt <= candles[^1].CloseTime; closeAt += step)
        {
            harness.Clock.Advance(closeAt + TimeSpan.FromMinutes(1));

            using var scope = harness.NewScope();
            var plans = scope.ServiceProvider.GetRequiredService<IDailyPlanService>();
            var eval = scope.ServiceProvider.GetRequiredService<ISignalEvalService>();

            var today = DateOnly.FromDateTime(harness.Clock.UtcNow);
            if (lastPlanDate != today)
            {
                await plans.GenerateAsync(harness.AccountId, today);
                lastPlanDate = today;
            }

            await eval.EvaluateAsync(harness.AccountId, Symbol, harness.Clock.UtcNow);
        }

        return await ReadAsync(harness);
    }

    [Fact]
    public async Task Hai_duong_chay_cho_cung_chuoi_phieu_cham_diem()
    {
        var fromEngine = await RunEngineAsync();
        var fromService = await RunManualAsync();

        Assert.NotEmpty(fromEngine);
        Assert.Equal(fromService.Count, fromEngine.Count);
        Assert.Equal(fromService, fromEngine);
    }

    [Fact]
    public async Task Chay_lai_engine_hai_lan_cho_ket_qua_giong_het()
    {
        Assert.Equal(await RunEngineAsync(), await RunEngineAsync());
    }

    [Fact]
    public async Task Bat_telemetry_khong_lam_thay_doi_bat_ky_ket_qua_giao_dich_nao()
    {
        var disabled = await RunReportAsync(collectTelemetry: false);
        var enabled = await RunReportAsync(collectTelemetry: true);

        Assert.Null(disabled.Telemetry);
        Assert.NotNull(enabled.Telemetry);
        Assert.True(enabled.Telemetry!.DecisionCount > 0);
        Assert.NotNull(enabled.Telemetry.EntryFills);

        Assert.Equal(disabled.StrategyVersion, enabled.StrategyVersion);
        Assert.Equal(disabled.TradeCount, enabled.TradeCount);
        Assert.Equal(disabled.WinRate, enabled.WinRate);
        Assert.Equal(disabled.ExpectancyR, enabled.ExpectancyR);
        Assert.Equal(disabled.MaxDrawdownPercent, enabled.MaxDrawdownPercent);
        Assert.Equal(disabled.TotalFeeR, enabled.TotalFeeR);
        Assert.Equal(disabled.TotalFundingR, enabled.TotalFundingR);
        Assert.Equal(disabled.TotalSlippageR, enabled.TotalSlippageR);
        Assert.Equal(JsonSerializer.Serialize(disabled.ByMode), JsonSerializer.Serialize(enabled.ByMode));
        Assert.Equal(JsonSerializer.Serialize(disabled.ByExitReason), JsonSerializer.Serialize(enabled.ByExitReason));
    }

    [Fact]
    public async Task Telemetry_fingerprint_on_dinh_khi_cung_du_lieu_va_cau_hinh()
    {
        var first = (await RunReportAsync(collectTelemetry: true)).Telemetry!;
        var second = (await RunReportAsync(collectTelemetry: true)).Telemetry!;

        Assert.Equal(first.DecisionFingerprint, second.DecisionFingerprint);
        Assert.Equal(first.TradeFingerprint, second.TradeFingerprint);
    }

    [Fact]
    public async Task Backtest_mac_dinh_khong_ghi_hang_nghin_phieu_vao_bang_production()
    {
        var candles = BacktestHarness.Series(Start, 80);
        using var harness = await BacktestHarness.CreateAsync(Start, candles);

        using (var scope = harness.NewScope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<IBacktestEngine>();
            await engine.RunAsync(new BacktestRequest(
                "transient", Start, candles[^1].CloseTime, new[] { Symbol }, harness.AccountId));
        }

        using var verify = harness.NewScope();
        var db = verify.ServiceProvider.GetRequiredService<MmwDbContext>();
        Assert.Equal(0, await db.EntryScorecards.CountAsync());
        Assert.Equal(0, await db.DailyPlans.CountAsync());
        Assert.Equal("Completed", await db.BacktestRuns.Select(x => x.Status).SingleAsync());
    }

    // ── Danh sách loại trừ ──────────────────────────────────────────────

    [Fact]
    public void Danh_sach_loai_tru_chi_co_DUNG_MOT_phan_tu()
    {
        // Loại trừ bằng cách nới lỏng phép so sánh là SAI — nó che luôn những lệch thật sự do
        // lỗi mã. Danh sách phải tường minh theo khoá, và phải ngắn.
        Assert.Single(BacktestLimitations.ParityExclusions);
        Assert.Contains("market.funding_crowding", BacktestLimitations.ParityExclusions);
    }

    [Fact]
    public void Phan_tu_bi_loai_tru_dung_la_tieu_chi_co_that()
    {
        // Loại trừ một khoá không tồn tại sẽ là loại trừ vô nghĩa, và danh sách sẽ âm thầm
        // mất tác dụng nếu tiêu chí bị đổi tên.
        var keys = Scoring.ScoringFixtures.AllCriteria().Select(c => c.Key).ToHashSet(StringComparer.Ordinal);

        Assert.All(BacktestLimitations.ParityExclusions, k =>
            Assert.True(keys.Contains(k), $"Khoá loại trừ '{k}' không khớp tiêu chí nào đang đăng ký."));
    }

    // ── Bộ gác FR-053: không nhánh mã riêng cho kiểm thử ────────────────

    [Fact]
    public void Tang_quyet_dinh_khong_biet_gi_ve_kiem_thu_lich_su()
    {
        // FR-053. Cách cưỡng chế mạnh nhất không phải là review mà là làm cho tầng quyết định
        // KHÔNG THỂ hỏi "có phải đang backtest không".
        var offenders = typeof(BacktestEngine).Assembly.GetTypes()
            .Where(t => t.Namespace is not null && t.Namespace.StartsWith("MMW.Application.Trading", StringComparison.Ordinal))
            .SelectMany(t => t.GetMembers().Select(m => (Type: t, Member: m)))
            .Where(x => x.Member.Name.Contains("Backtest", StringComparison.OrdinalIgnoreCase)
                        || x.Member.Name.Contains("IsSimulat", StringComparison.OrdinalIgnoreCase))
            .Select(x => $"{x.Type.Name}.{x.Member.Name}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "Tầng quyết định không được có nhánh riêng cho kiểm thử lịch sử (FR-053): "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void Tang_quyet_dinh_khong_tham_chieu_lop_cua_engine_kiem_thu()
    {
        // Chiều phụ thuộc phải một chiều: Backtest biết Trading, Trading KHÔNG biết Backtest.
        var calls = IlScanner.ScanCalls(
            typeof(BacktestEngine).Assembly,
            ns => ns.StartsWith("MMW.Application.Trading", StringComparison.Ordinal));

        var offenders = calls
            .Where(c => c.TargetType.StartsWith("MMW.Application.Backtest", StringComparison.Ordinal))
            .Select(c => c.ToString())
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0,
            "Tầng quyết định gọi sang engine kiểm thử — chiều phụ thuộc bị đảo: "
            + string.Join(", ", offenders));
    }
}
