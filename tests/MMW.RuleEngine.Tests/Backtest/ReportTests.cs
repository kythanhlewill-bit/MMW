using Microsoft.Extensions.DependencyInjection;
using MMW.Application.Backtest;
using MMW.Application.Backtest.Models;
using MMW.Application.MarketData.Models;
using Xunit;

namespace MMW.RuleEngine.Tests.Backtest;

/// <summary>
/// T129 — báo cáo phải nêu hạn chế của chính nó, và mô phỏng phải thận trọng.
/// </summary>
/// <remarks>
/// Một báo cáo kiểm thử không nêu hạn chế sẽ được đọc như một lời hứa — và đó chính là cách
/// người ta thuyết phục bản thân bật giao dịch thật quá sớm.
/// </remarks>
public class ReportTests
{
    private static readonly DateTime Start = new(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc);

    // ── Limitations ─────────────────────────────────────────────────────

    [Fact]
    public void Danh_sach_han_che_KHONG_duoc_rong()
    {
        Assert.NotEmpty(BacktestLimitations.Build(0.05m, 1m, 3m, missingCandles: 0));
    }

    [Fact]
    public void Han_che_neu_dung_10_tren_100_diem_bi_mat()
    {
        var items = BacktestLimitations.Build(0.05m, 1m, 3m, 0);

        Assert.Contains(items, s => s.Contains("10/100"));
        Assert.Equal(10, BacktestLimitations.UnavailableCriteria.Values.Sum());
    }

    [Fact]
    public void Hai_tieu_chi_khong_dung_lai_duoc_deu_co_ten_ro_rang()
    {
        Assert.Contains("liquidity.open_interest", BacktestLimitations.UnavailableCriteria.Keys);
        Assert.Contains("liquidity.spread_depth", BacktestLimitations.UnavailableCriteria.Keys);

        var items = BacktestLimitations.Build(0.05m, 1m, 3m, 0);
        Assert.Contains(items, s => s.Contains("liquidity.open_interest"));
        Assert.Contains(items, s => s.Contains("liquidity.spread_depth"));
    }

    [Fact]
    public void Han_che_neu_ro_phi_von_dung_ty_le_da_thanh_toan()
    {
        var items = BacktestLimitations.Build(0.05m, 1m, 3m, 0);

        Assert.Contains(items, s => s.Contains("ĐÃ THANH TOÁN") && s.Contains("DỰ PHÓNG"));
    }

    [Fact]
    public void Han_che_neu_ro_gia_dinh_dung_lo_khop_truoc()
    {
        var items = BacktestLimitations.Build(0.05m, 1m, 3m, 0);

        Assert.Contains(items, s => s.Contains("DỪNG LỖ KHỚP TRƯỚC"));
    }

    [Fact]
    public void Han_che_neu_dung_con_so_phi_va_truot_gia_da_dung()
    {
        var items = BacktestLimitations.Build(0.07m, 2.5m, 4.5m, 0);

        Assert.Contains(items, s => s.Contains("0.07") && s.Contains("2.5") && s.Contains("4.5"));
    }

    [Fact]
    public void Thieu_nen_thi_them_mot_dong_han_che_nua()
    {
        var without = BacktestLimitations.Build(0.05m, 1m, 3m, 0);
        var with = BacktestLimitations.Build(0.05m, 1m, 3m, 42);

        Assert.Equal(without.Count + 1, with.Count);
        Assert.Contains(with, s => s.Contains("42"));
    }

    [Fact]
    public void Win_rate_dung_khoang_Wilson_chu_khong_bao_cao_mot_diem()
    {
        var interval = BacktestStatistics.WinRate95(wins: 19, count: 73);

        Assert.InRange(interval.Lower, 17m, 18m);
        Assert.InRange(interval.Upper, 37m, 38m);
    }

    [Fact]
    public void Phan_phoi_RR_cho_biet_moi_nguong_giu_lai_bao_nhieu_mau()
    {
        var distribution = BacktestStatistics.StructuralRr(
            evaluatedCount: 5,
            observed: new[] { 0.8m, 1.0m, 1.2m, 1.6m },
            insufficientRoomVetoCount: 4,
            unplannableStopCount: 1);

        Assert.Equal(1.1m, distribution.Median);
        Assert.Equal(1, distribution.UnplannableStopCount);
        Assert.Equal(2, distribution.Thresholds.Single(x => x.ThresholdR == 1.2m).EligibleCount);
        Assert.Equal(40m, distribution.Thresholds.Single(x => x.ThresholdR == 1.2m).PercentOfEvaluated);
    }

    // ── Vòng đời vị thế mô phỏng ────────────────────────────────────────

    private static async Task<BacktestReport> RunAsync(IReadOnlyList<Candle> candles)
    {
        using var harness = await BacktestHarness.CreateAsync(Start, candles);
        using var scope = harness.NewScope();
        var engine = scope.ServiceProvider.GetRequiredService<IBacktestEngine>();

        return await engine.RunAsync(new BacktestRequest(
            "report", Start, candles[^1].CloseTime, new[] { "BTCUSDT" }, harness.AccountId));
    }

    [Fact]
    public async Task Bao_cao_that_luon_kem_danh_sach_han_che()
    {
        var report = await RunAsync(BacktestHarness.Series(Start, 200));

        Assert.NotEmpty(report.Limitations);
        Assert.NotEqual(0, report.RunId);
    }

    [Fact]
    public async Task Khong_ra_lenh_nao_van_la_bao_cao_hop_le()
    {
        // Zero lệnh là kết quả ĐÚNG, không phải lỗi — và báo cáo vẫn phải nêu hạn chế.
        var report = await RunAsync(BacktestHarness.Series(Start, 100));

        Assert.InRange(report.TradeCount, 0, int.MaxValue);
        Assert.NotEmpty(report.Limitations);
    }

    [Fact]
    public async Task Kho_thieu_nen_thi_TU_CHOI_chay_chu_khong_canh_bao_roi_chay_tiep()
    {
        // Chạy trên dữ liệu khuyết cho ra kết quả trông hợp lệ nhưng sai — kiểu lỗi tệ nhất.
        var candles = BacktestHarness.Series(Start, 200);
        var withHole = candles.Take(50).Concat(candles.Skip(60)).ToList();

        using var harness = await BacktestHarness.CreateAsync(Start, withHole);
        using var scope = harness.NewScope();
        var engine = scope.ServiceProvider.GetRequiredService<IBacktestEngine>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.RunAsync(
            new BacktestRequest("gap", Start, candles[^1].CloseTime, new[] { "BTCUSDT" }, harness.AccountId)));

        Assert.Contains("thiếu", ex.Message);
    }

    [Fact]
    public async Task Lan_chay_duoc_luu_lai_kem_han_che()
    {
        var candles = BacktestHarness.Series(Start, 150);
        using var harness = await BacktestHarness.CreateAsync(Start, candles);

        using (var scope = harness.NewScope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<IBacktestEngine>();
            await engine.RunAsync(new BacktestRequest(
                "saved", Start, candles[^1].CloseTime, new[] { "BTCUSDT" }, harness.AccountId));
        }

        using var verify = harness.NewScope();
        var db = verify.ServiceProvider.GetRequiredService<Domain.DbContext.MmwDbContext>();
        var run = db.BacktestRuns.Single();

        Assert.Equal("Completed", run.Status);
        Assert.False(string.IsNullOrWhiteSpace(run.Limitations));
        Assert.False(string.IsNullOrWhiteSpace(run.EngineSettingSnapshotJson));
        Assert.False(string.IsNullOrWhiteSpace(run.StructuralRrDistributionJson));
        Assert.False(string.IsNullOrWhiteSpace(run.StructuralRrVetoObservationsJson));
        Assert.False(string.IsNullOrWhiteSpace(run.BreakdownByModeJson));
        Assert.False(string.IsNullOrWhiteSpace(run.BreakdownByExitReasonJson));
        Assert.Equal(1, run.ComparableTrialNumber);
    }
}
