using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MMW.Application.Interfaces;
using MMW.Application.Trading.TimeGuard;
using MMW.Domain.DbContext;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.TimeGuard;

/// <summary>
/// T050 / SC-009 / FR-014 — lịch nạp tay rỗng KHÔNG được làm lớp bảo vệ biến mất trong im lặng.
/// </summary>
/// <remarks>
/// Đây là kịch bản thất bại có xác suất cao nhất của cả tầng này, và nó không đến từ lỗi mã:
/// nó đến từ việc tháng 1 sang năm không ai nhớ nạp lịch CPI/FOMC mới. Khi đó bảng
/// <c>ScheduledEvents</c> vẫn còn nguyên dữ liệu cũ, mọi truy vấn vẫn chạy, không có ngoại lệ
/// nào — hệ thống chỉ đơn giản là hết chặn.
///
/// Hai lớp phòng thủ được kiểm ở đây: (a) cửa sổ sinh bằng công thức vẫn hoạt động đủ 100%
/// vì chúng không cần dữ liệu nạp tay, và (b) hệ thống phải KÊU LÊN rằng phần nạp tay đã hết.
/// </remarks>
public class EmptyCalendarTests
{
    private static DateTime Utc(int y, int m, int d, int h = 0, int min = 0) =>
        new(y, m, d, h, min, 0, DateTimeKind.Utc);

    private static async Task<BlackoutDecision> CheckAsync(TimeGuardHarness harness, DateTime utcNow)
    {
        using var scope = harness.NewScope();
        var guard = scope.ServiceProvider.GetRequiredService<ITimeGuardService>();
        return await guard.CheckAsync(harness.AccountId, "BTCUSDT", utcNow);
    }

    private static async Task AddCompleteCalendarAsync(TimeGuardHarness harness, DateTime occursAtUtc)
    {
        var kinds = new[]
        {
            ScheduledEventKind.Cpi,
            ScheduledEventKind.Ppi,
            ScheduledEventKind.Pce,
            ScheduledEventKind.Nfp,
            ScheduledEventKind.FomcStatement,
            ScheduledEventKind.FomcPressConference,
        };

        await harness.AddEventsAsync(kinds.Select(kind => new ScheduledEvent
        {
            Kind = kind,
            Title = kind.ToString(),
            OccursAtUtc = occursAtUtc,
            Impact = MacroEventImpact.Critical,
            Origin = ScheduledEventOrigin.Seeded,
            SourceKey = $"test:freshness:{kind}:{occursAtUtc:O}",
        }).ToArray());
    }

    // ── (a) Cửa sổ sinh bằng công thức vẫn cưỡng chế đủ ─────────────────

    [Theory]
    // Thanh toán phí vốn — thứ Tư 2026-08-05, cửa sổ [07:55, 08:05)
    [InlineData(2026, 8, 5, 7, 55, true)]
    [InlineData(2026, 8, 5, 8, 0, true)]
    [InlineData(2026, 8, 5, 8, 4, true)]
    [InlineData(2026, 8, 5, 8, 5, false)]
    [InlineData(2026, 8, 5, 7, 54, false)]
    // Cả ba mốc phí vốn trong ngày đều có hiệu lực
    [InlineData(2026, 8, 5, 0, 0, true)]
    [InlineData(2026, 8, 5, 16, 0, true)]
    [InlineData(2026, 8, 5, 12, 0, false)]
    // Khoảng trống cuối tuần — Chủ nhật 2026-08-02, cửa sổ [21:00, 23:00)
    [InlineData(2026, 8, 2, 21, 0, true)]
    [InlineData(2026, 8, 2, 22, 59, true)]
    [InlineData(2026, 8, 2, 23, 0, false)]
    [InlineData(2026, 8, 2, 20, 59, false)]
    public async Task Lich_rong_van_chan_dung_theo_cua_so_sinh_bang_cong_thuc(
        int y, int m, int d, int hour, int minute, bool expectBlocked)
    {
        using var harness = await TimeGuardHarness.CreateAsync();   // không nạp sự kiện nào

        var decision = await CheckAsync(harness, Utc(y, m, d, hour, minute));

        Assert.Equal(expectBlocked, decision.IsBlocked);
    }

    [Fact]
    public async Task Lich_rong_van_chan_dao_han_quyen_chon_thu_Sau()
    {
        // 2026-08-07 là thứ Sáu. Đáo hạn [07:30, 08:30) trùm lên cả phí vốn [07:55, 08:05),
        // nên cửa sổ hợp nhất phải là [07:30, 08:30).
        using var harness = await TimeGuardHarness.CreateAsync();

        var decision = await CheckAsync(harness, Utc(2026, 8, 7, 7, 30));

        Assert.True(decision.IsBlocked);
        Assert.Equal(Utc(2026, 8, 7, 7, 30), decision.Window!.FromUtc);
        Assert.Equal(Utc(2026, 8, 7, 8, 30), decision.Window.ToUtc);
    }

    [Fact]
    public async Task Lich_rong_van_sinh_du_ba_loai_cua_so_trong_mot_tuan()
    {
        // Đếm theo LOẠI chứ không theo số lượng: mất hẳn một loại là mất một lớp bảo vệ,
        // và một phép đếm tổng vẫn có thể lớn hơn 0 trong khi một loại đã biến mất.
        using var harness = await TimeGuardHarness.CreateAsync();

        using var scope = harness.NewScope();
        var guard = scope.ServiceProvider.GetRequiredService<ITimeGuardService>();

        var windows = await guard.GetWindowsAsync(
            harness.AccountId, Utc(2026, 8, 2), Utc(2026, 8, 9));

        Assert.NotEmpty(windows);
        Assert.Contains(windows, w => w.Kind == ScheduledEventKind.FundingSettlement);
        Assert.Contains(windows, w => w.Kind == ScheduledEventKind.OptionsExpiry);
        Assert.Contains(windows, w => w.Kind == ScheduledEventKind.WeekendGap);
    }

    // ── (b) Hệ thống phải kêu lên khi phần nạp tay đã hết ───────────────

    [Fact]
    public async Task Lich_nap_tay_rong_bi_bao_qua_han()
    {
        using var harness = await TimeGuardHarness.CreateAsync();

        using var scope = harness.NewScope();
        var guard = scope.ServiceProvider.GetRequiredService<ITimeGuardService>();

        var freshness = await guard.GetCalendarFreshnessAsync(Utc(2026, 8, 5, 12));

        Assert.True(freshness.IsStale);
        Assert.Null(freshness.LastSeededEventUtc);
        Assert.False(string.IsNullOrWhiteSpace(freshness.WarningVi));
    }

    [Fact]
    public async Task Lich_chi_con_su_kien_qua_khu_cung_bi_bao_qua_han()
    {
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddEventsAsync(new ScheduledEvent
        {
            Kind = ScheduledEventKind.Cpi,
            Title = "CPI tháng 7",
            OccursAtUtc = Utc(2026, 7, 14, 12, 30),
            Impact = MacroEventImpact.Critical,
            Origin = ScheduledEventOrigin.Seeded,
            SourceKey = "bls:cpi:2026-07",
        });

        using var scope = harness.NewScope();
        var guard = scope.ServiceProvider.GetRequiredService<ITimeGuardService>();

        var freshness = await guard.GetCalendarFreshnessAsync(Utc(2026, 8, 5, 12));

        Assert.True(freshness.IsStale);
        Assert.Null(freshness.LastSeededEventUtc);
        Assert.Contains(freshness.Kinds, k =>
            k.Kind == ScheduledEventKind.Cpi
            && k.IsStale
            && k.LastSeededEventUtc == Utc(2026, 7, 14, 12, 30));
    }

    [Fact]
    public async Task Lich_con_su_kien_tuong_lai_thi_khong_bao_qua_han()
    {
        using var harness = await TimeGuardHarness.CreateAsync();
        await AddCompleteCalendarAsync(harness, Utc(2026, 9, 10, 12, 30));

        using var scope = harness.NewScope();
        var guard = scope.ServiceProvider.GetRequiredService<ITimeGuardService>();

        var freshness = await guard.GetCalendarFreshnessAsync(Utc(2026, 8, 5, 12));

        Assert.False(freshness.IsStale);
        Assert.Equal(Utc(2026, 9, 10, 12, 30), freshness.LastSeededEventUtc);
    }

    [Fact]
    public async Task Nfp_tuong_lai_khong_duoc_che_lich_Cpi_Ppi_Pce_Fomc_bi_thieu()
    {
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddEventsAsync(new ScheduledEvent
        {
            Kind = ScheduledEventKind.Nfp,
            Title = "NFP năm sau",
            OccursAtUtc = Utc(2027, 12, 3, 13, 30),
            Impact = MacroEventImpact.Critical,
            Origin = ScheduledEventOrigin.Seeded,
            SourceKey = "bls:nfp:2027-12",
        });

        using var scope = harness.NewScope();
        var guard = scope.ServiceProvider.GetRequiredService<ITimeGuardService>();

        var freshness = await guard.GetCalendarFreshnessAsync(Utc(2026, 8, 5, 12));

        Assert.True(freshness.IsStale);
        Assert.Contains(freshness.Kinds, k => k.Kind == ScheduledEventKind.Nfp && !k.IsStale);
        Assert.Contains(freshness.Kinds, k => k.Kind == ScheduledEventKind.Cpi && k.IsStale);
        Assert.Contains("CPI", freshness.WarningVi);
    }

    [Fact]
    public async Task Su_kien_sinh_bang_cong_thuc_KHONG_lam_lich_nap_tay_het_qua_han()
    {
        // Mốc phí vốn có sẵn đến vô tận. Nếu phép đo "lịch còn mới không" tính cả chúng
        // thì nó vĩnh viễn báo xanh, và cảnh báo quên nạp lịch không bao giờ nổ.
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddEventsAsync(new ScheduledEvent
        {
            Kind = ScheduledEventKind.FundingSettlement,
            Title = "Phí vốn",
            OccursAtUtc = Utc(2027, 1, 1, 8),
            Impact = MacroEventImpact.Low,
            Origin = ScheduledEventOrigin.Derived,
            SourceKey = "derived:funding:2027-01-01T08",
        });

        using var scope = harness.NewScope();
        var guard = scope.ServiceProvider.GetRequiredService<ITimeGuardService>();

        Assert.True((await guard.GetCalendarFreshnessAsync(Utc(2026, 8, 5, 12))).IsStale);
    }

    [Fact]
    public async Task Canh_bao_lich_qua_han_duoc_phat_thanh_thong_bao()
    {
        using var harness = await TimeGuardHarness.CreateAsync();

        using (var scope = harness.NewScope())
        {
            var monitor = scope.ServiceProvider.GetRequiredService<ICalendarFreshnessMonitor>();
            await monitor.RunAsync(Utc(2026, 8, 5, 12));
        }

        using var verify = harness.NewScope();
        var db = verify.ServiceProvider.GetRequiredService<MmwDbContext>();

        Assert.True(await db.Notifications.AnyAsync(n => n.Type == NotificationType.SystemHealth));
    }

    [Fact]
    public async Task Lich_con_han_thi_khong_phat_thong_bao_thua()
    {
        using var harness = await TimeGuardHarness.CreateAsync();
        await AddCompleteCalendarAsync(harness, Utc(2026, 9, 4, 12, 30));

        using (var scope = harness.NewScope())
        {
            var monitor = scope.ServiceProvider.GetRequiredService<ICalendarFreshnessMonitor>();
            await monitor.RunAsync(Utc(2026, 8, 5, 12));
        }

        using var verify = harness.NewScope();
        var db = verify.ServiceProvider.GetRequiredService<MmwDbContext>();

        Assert.False(await db.Notifications.AnyAsync());
    }
}
