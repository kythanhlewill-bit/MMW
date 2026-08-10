using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MMW.Application.Backtest;
using MMW.Domain.DbContext;
using MMW.Domain.Entities;
using MMW.RuleEngine.Tests.Scoring;
using MMW.RuleEngine.Tests.TimeGuard;
using Xunit;

namespace MMW.RuleEngine.Tests.Backtest;

/// <summary>
/// T126 — kho nến: nạp lại không sinh trùng, và dò đúng khoảng thiếu.
/// </summary>
/// <remarks>
/// <c>FindGapsAsync</c> là thứ đứng giữa "kết quả kiểm thử đáng tin" và "kết quả trông hợp lệ
/// nhưng sai". Một khoảng thiếu 20 cây nến giữa khoảng chạy sẽ không gây lỗi nào — nó chỉ làm
/// engine bỏ qua vài giờ giao dịch, và con số cuối cùng vẫn ra một tỷ lệ thắng đẹp đẽ.
/// </remarks>
public class KlineArchiveTests
{
    private static readonly DateTime Start = new(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc);
    private const string Symbol = "BTCUSDT";
    private const string Interval = "15m";

    private static async Task<TimeGuardHarness> HarnessAsync()
    {
        var harness = await TimeGuardHarness.CreateAsync();
        harness.MarketData.Candles[Symbol] = ScoringFixtures.ZigZag(96, interval: TimeSpan.FromMinutes(15));
        return harness;
    }

    private static async Task SeedArchiveAsync(TimeGuardHarness harness, IEnumerable<int> slots)
    {
        using var scope = harness.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();

        foreach (var i in slots)
        {
            var open = Start.AddMinutes(15 * i);
            db.KlineArchives.Add(new KlineArchive
            {
                Symbol = Symbol,
                Interval = Interval,
                OpenTimeUtc = open,
                CloseTimeUtc = open.AddMinutes(15).AddTicks(-1),
                Open = 100m, High = 101m, Low = 99m, Close = 100m, Volume = 10m,
            });
        }
        await db.SaveChangesAsync();
    }

    private static async Task<T> UseAsync<T>(TimeGuardHarness harness, Func<IKlineArchiveService, Task<T>> work)
    {
        using var scope = harness.NewScope();
        return await work(scope.ServiceProvider.GetRequiredService<IKlineArchiveService>());
    }

    // ── Dò khoảng thiếu ─────────────────────────────────────────────────

    [Fact]
    public async Task Du_nen_lien_mach_thi_khong_co_khoang_thieu()
    {
        using var harness = await HarnessAsync();
        await SeedArchiveAsync(harness, Enumerable.Range(0, 20));

        var gaps = await UseAsync(harness, s => s.FindGapsAsync(Symbol, Interval, Start, Start.AddMinutes(15 * 20)));

        Assert.Empty(gaps);
    }

    [Fact]
    public async Task Thieu_nen_o_giua_thi_bao_dung_khoang()
    {
        // Nạp 0–4 và 8–19, thiếu 5–7.
        using var harness = await HarnessAsync();
        await SeedArchiveAsync(harness, Enumerable.Range(0, 5).Concat(Enumerable.Range(8, 12)));

        var gaps = await UseAsync(harness, s => s.FindGapsAsync(Symbol, Interval, Start, Start.AddMinutes(15 * 20)));

        var gap = Assert.Single(gaps);
        Assert.Equal(Start.AddMinutes(15 * 5), gap.From);
        Assert.Equal(Start.AddMinutes(15 * 8), gap.To);
    }

    [Fact]
    public async Task Nhieu_khoang_thieu_roi_rac_deu_duoc_bao()
    {
        using var harness = await HarnessAsync();
        await SeedArchiveAsync(harness, new[] { 0, 1, 4, 5, 8, 9 });

        var gaps = await UseAsync(harness, s => s.FindGapsAsync(Symbol, Interval, Start, Start.AddMinutes(15 * 10)));

        Assert.Equal(2, gaps.Count);
    }

    [Fact]
    public async Task Kho_rong_thi_ca_khoang_la_mot_lo_hong()
    {
        using var harness = await HarnessAsync();

        var gaps = await UseAsync(harness, s => s.FindGapsAsync(Symbol, Interval, Start, Start.AddMinutes(15 * 10)));

        var gap = Assert.Single(gaps);
        Assert.Equal(Start, gap.From);
    }

    [Fact]
    public async Task Thieu_o_cuoi_khoang_cung_duoc_bao()
    {
        using var harness = await HarnessAsync();
        await SeedArchiveAsync(harness, Enumerable.Range(0, 5));

        var gaps = await UseAsync(harness, s => s.FindGapsAsync(Symbol, Interval, Start, Start.AddMinutes(15 * 10)));

        var gap = Assert.Single(gaps);
        Assert.Equal(Start.AddMinutes(15 * 5), gap.From);
        Assert.Equal(Start.AddMinutes(15 * 10), gap.To);
    }

    [Fact]
    public async Task Khoang_hoi_rong_thi_khong_co_lo_hong_nao()
    {
        using var harness = await HarnessAsync();

        Assert.Empty(await UseAsync(harness, s => s.FindGapsAsync(Symbol, Interval, Start, Start)));
    }

    [Fact]
    public async Task Khung_thoi_gian_la_thi_NEM_chu_khong_doan_bua()
    {
        // Đoán bừa độ dài cây nến sẽ làm phép dò khoảng thiếu sai trong im lặng.
        using var harness = await HarnessAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => UseAsync(harness, s => s.FindGapsAsync(Symbol, "7m", Start, Start.AddHours(1))));
    }

    // ── Đọc khoảng ──────────────────────────────────────────────────────

    [Fact]
    public async Task Doc_dung_khoang_va_theo_thu_tu_thoi_gian()
    {
        using var harness = await HarnessAsync();
        await SeedArchiveAsync(harness, Enumerable.Range(0, 20));

        var candles = await UseAsync(harness,
            s => s.GetRangeAsync(Symbol, Interval, Start.AddMinutes(15 * 5), Start.AddMinutes(15 * 10)));

        Assert.Equal(5, candles.Count);
        Assert.Equal(Start.AddMinutes(15 * 5), candles[0].OpenTime);
        Assert.True(candles.Zip(candles.Skip(1)).All(p => p.First.OpenTime < p.Second.OpenTime));
    }

    [Fact]
    public async Task Doc_khong_lan_sang_ma_khac()
    {
        using var harness = await HarnessAsync();
        await SeedArchiveAsync(harness, Enumerable.Range(0, 20));

        var candles = await UseAsync(harness,
            s => s.GetRangeAsync("ETHUSDT", Interval, Start, Start.AddMinutes(15 * 20)));

        Assert.Empty(candles);
    }

    // ── Nạp bổ sung bất biến ────────────────────────────────────────────

    [Fact]
    public async Task Nap_lai_cung_khoang_khong_sinh_ban_ghi_trung()
    {
        // FR-005. Nạp lại chồng lấn là chuyện thường xuyên — người ta chạy lại lệnh nạp sau
        // khi bị ngắt giữa chừng — nên chống trùng phải là mặc định.
        using var harness = await HarnessAsync();
        var to = Start.AddDays(1);

        await UseAsync(harness, s => s.BackfillAsync(Symbol, Interval, ScoringFixtures.Now.AddDays(-2), to));
        var countAfterFirst = await CountAsync(harness);

        await UseAsync(harness, s => s.BackfillAsync(Symbol, Interval, ScoringFixtures.Now.AddDays(-2), to));
        var countAfterSecond = await CountAsync(harness);

        Assert.Equal(countAfterFirst, countAfterSecond);
    }

    [Fact]
    public async Task Nap_nen_lich_su_tien_con_tro_qua_nhieu_trang()
    {
        using var harness = await HarnessAsync();
        var source = Enumerable.Range(0, 1005)
            .Select(i =>
            {
                var open = Start.AddMinutes(15 * i);
                return new Application.MarketData.Models.Candle(
                    open, 100m, 101m, 99m, 100m, 10m, open.AddMinutes(15).AddTicks(-1));
            })
            .ToList();
        harness.MarketData.Candles[Symbol] = source;
        var to = Start.AddMinutes(15 * source.Count);

        var saved = await UseAsync(harness, s => s.BackfillAsync(Symbol, Interval, Start, to));

        Assert.Equal(source.Count, saved);
        Assert.Equal(source.Count, await CountAsync(harness));
        Assert.Empty(await UseAsync(harness, s => s.FindGapsAsync(Symbol, Interval, Start, to)));
    }

    [Fact]
    public async Task Nap_khoang_rong_hoac_nguoc_thi_khong_lam_gi()
    {
        using var harness = await HarnessAsync();

        Assert.Equal(0, await UseAsync(harness, s => s.BackfillAsync(Symbol, Interval, Start, Start)));
        Assert.Equal(0, await UseAsync(harness, s => s.BackfillAsync(Symbol, Interval, Start, Start.AddDays(-1))));
    }

    // ── Kho phí vốn ─────────────────────────────────────────────────────

    [Fact]
    public async Task Doc_phi_von_lay_moc_DA_THANH_TOAN_gan_nhat()
    {
        using var harness = await HarnessAsync();

        using (var scope = harness.NewScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();
            db.FundingRateArchives.AddRange(
                new FundingRateArchive { Symbol = Symbol, FundingTimeUtc = Start, FundingRate = 0.0001m },
                new FundingRateArchive { Symbol = Symbol, FundingTimeUtc = Start.AddHours(8), FundingRate = 0.0002m },
                new FundingRateArchive { Symbol = Symbol, FundingTimeUtc = Start.AddHours(16), FundingRate = 0.0003m });
            await db.SaveChangesAsync();
        }

        var at12 = await UseAsync(harness, s => s.GetFundingAtAsync(Symbol, Start.AddHours(12))!);

        Assert.NotNull(at12);
        Assert.Equal(0.0002m, at12!.FundingRate);
    }

    [Fact]
    public async Task Chua_co_moc_phi_von_nao_thi_tra_null()
    {
        using var harness = await HarnessAsync();

        Assert.Null(await UseAsync(harness, s => s.GetFundingAtAsync(Symbol, Start)!));
    }

    [Fact]
    public async Task Nap_phi_von_lich_su_phan_trang_500_va_bat_bien()
    {
        using var harness = await HarnessAsync();
        var source = Enumerable.Range(0, 1001)
            .Select(i => new Application.MarketData.Models.FundingRatePoint(
                Start.AddHours(8 * i), 0.0001m + i / 10_000_000m, 100m + i))
            .ToList();
        harness.MarketData.FundingHistory[Symbol] = source;
        var to = source[^1].FundingTimeUtc.AddHours(8);

        var first = await UseAsync(harness, s => s.BackfillFundingAsync(Symbol, Start, to));
        var second = await UseAsync(harness, s => s.BackfillFundingAsync(Symbol, Start, to));

        Assert.Equal(source.Count, first);
        Assert.Equal(0, second);
        Assert.Equal(source.Count, await FundingCountAsync(harness));
    }

    private static async Task<int> CountAsync(TimeGuardHarness harness)
    {
        using var scope = harness.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();
        return await db.KlineArchives.CountAsync();
    }

    private static async Task<int> FundingCountAsync(TimeGuardHarness harness)
    {
        using var scope = harness.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();
        return await db.FundingRateArchives.CountAsync();
    }
}
