using Microsoft.Extensions.DependencyInjection;
using MMW.Application.Trading.TimeGuard;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.TimeGuard;

/// <summary>
/// T049 / FR-012 — cửa sổ chồng lấn phải hợp nhất thành MỘT khoảng liên tục.
/// </summary>
/// <remarks>
/// Không hợp nhất thì hệ thống vẫn chặn đúng, nhưng mọi thứ đọc cửa sổ đều sai: giao diện
/// hiện hai dòng chồng nhau, và <c>GetUpcomingAsync</c> trả về cửa sổ thứ hai sau khi cửa sổ
/// thứ nhất đã bắt đầu — làm lớp xử lý vị thế tưởng còn thời gian trong khi đã ở trong vùng cấm.
///
/// Khoảng khảo sát 10:00–15:00 UTC được chọn để không chạm mốc phí vốn 08:00 và 16:00, nên
/// mọi cửa sổ đếm được ở đây đều là của sự kiện do test nạp vào.
/// </remarks>
public class WindowMergeTests
{
    private static readonly DateTime Day = new(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc);
    private static DateTime At(int hour, int minute) => Day.AddHours(hour).AddMinutes(minute);

    private static readonly DateTime ProbeFrom = At(10, 0);
    private static readonly DateTime ProbeTo = At(15, 0);

    private static ScheduledEvent Event(ScheduledEventKind kind, DateTime at, MacroEventImpact impact) => new()
    {
        Kind = kind,
        Title = kind.ToString(),
        OccursAtUtc = at,
        Impact = impact,
        Origin = ScheduledEventOrigin.Seeded,
        SourceKey = $"test:{kind}:{at:O}",
    };

    private static async Task<IReadOnlyList<BlackoutWindow>> WindowsAsync(TimeGuardHarness harness)
    {
        using var scope = harness.NewScope();
        var guard = scope.ServiceProvider.GetRequiredService<ITimeGuardService>();
        return await guard.GetWindowsAsync(harness.AccountId, ProbeFrom, ProbeTo);
    }

    [Fact]
    public async Task Hai_cua_so_chong_lan_hop_nhat_thanh_mot_khoang_lien_tuc()
    {
        // CPI 13:30 → [12:30, 14:00);  PCE 14:10 → [13:40, 14:25).  Chồng nhau ở 13:40–14:00.
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddEventsAsync(
            Event(ScheduledEventKind.Cpi, At(13, 30), MacroEventImpact.Critical),
            Event(ScheduledEventKind.Pce, At(14, 10), MacroEventImpact.Medium));

        var windows = await WindowsAsync(harness);

        var merged = Assert.Single(windows);
        Assert.Equal(At(12, 30), merged.FromUtc);
        Assert.Equal(At(14, 25), merged.ToUtc);
    }

    [Fact]
    public async Task Cua_so_hop_nhat_giu_muc_tac_dong_CAO_NHAT()
    {
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddEventsAsync(
            Event(ScheduledEventKind.Cpi, At(13, 30), MacroEventImpact.Critical),
            Event(ScheduledEventKind.Pce, At(14, 10), MacroEventImpact.Medium));

        var merged = Assert.Single(await WindowsAsync(harness));

        Assert.Equal(MacroEventImpact.Critical, merged.Impact);
    }

    [Fact]
    public async Task Cua_so_hop_nhat_buoc_xu_ly_vi_the_neu_BAT_KY_thanh_phan_nao_buoc()
    {
        // GDP không buộc xử lý vị thế, CPI thì có. Hợp nhất mà lấy giá trị của cái đến sau
        // sẽ làm mất một lớp bảo vệ — Nguyên tắc III chỉ cho thêm, không cho bớt.
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddEventsAsync(
            Event(ScheduledEventKind.Gdp, At(13, 0), MacroEventImpact.Medium),    // [12:30, 13:15)
            Event(ScheduledEventKind.Cpi, At(13, 45), MacroEventImpact.High));    // [12:45, 14:15)

        var merged = Assert.Single(await WindowsAsync(harness));

        Assert.True(merged.RequiresPositionAction);
    }

    [Fact]
    public async Task Cua_so_hop_nhat_neu_ten_ca_hai_su_kien()
    {
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddEventsAsync(
            Event(ScheduledEventKind.Cpi, At(13, 30), MacroEventImpact.High),
            Event(ScheduledEventKind.Pce, At(14, 10), MacroEventImpact.Medium));

        var merged = Assert.Single(await WindowsAsync(harness));

        Assert.Contains("Cpi", merged.Title, StringComparison.Ordinal);
        Assert.Contains("Pce", merged.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hai_cua_so_cham_nhau_dung_bien_cung_hop_nhat()
    {
        // CPI 13:30 → [12:30, 14:00);  GDP 14:30 → [14:00, 14:45).
        // Nửa mở nên [12:30,14:00) ∪ [14:00,14:45) = [12:30,14:45): không hề có kẽ hở nào
        // ở 14:00, và để lộ ra hai dòng là mô tả sai sự thật.
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddEventsAsync(
            Event(ScheduledEventKind.Cpi, At(13, 30), MacroEventImpact.High),
            Event(ScheduledEventKind.Gdp, At(14, 30), MacroEventImpact.Medium));

        var merged = Assert.Single(await WindowsAsync(harness));

        Assert.Equal(At(12, 30), merged.FromUtc);
        Assert.Equal(At(14, 45), merged.ToUtc);
    }

    [Fact]
    public async Task Cua_so_nam_gon_trong_cua_so_khac_khong_lam_ngan_khoang_hop_nhat()
    {
        // FOMC statement 13:30 → [12:00, 14:00);  Jobless 13:00 → [12:30, 13:15) nằm gọn bên trong.
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddEventsAsync(
            Event(ScheduledEventKind.FomcStatement, At(13, 30), MacroEventImpact.Critical),
            Event(ScheduledEventKind.JoblessClaims, At(13, 0), MacroEventImpact.Low));   // [12:30, 13:15)

        var merged = Assert.Single(await WindowsAsync(harness));

        Assert.Equal(At(12, 0), merged.FromUtc);
        Assert.Equal(At(14, 0), merged.ToUtc);
    }

    [Fact]
    public async Task Hai_cua_so_cach_nhau_thi_van_la_hai_dong_rieng()
    {
        // CPI 13:30 → [12:30, 14:00);  Jobless 15:00 → [14:30, 15:15).  Hở 14:00–14:30.
        // Hợp nhất quá tay sẽ nuốt mất nửa tiếng vào lệnh hoàn toàn hợp lệ.
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddEventsAsync(
            Event(ScheduledEventKind.Cpi, At(13, 30), MacroEventImpact.High),
            Event(ScheduledEventKind.JoblessClaims, At(15, 0), MacroEventImpact.Low));

        var windows = (await WindowsAsync(harness)).OrderBy(w => w.FromUtc).ToList();

        Assert.Equal(2, windows.Count);
        Assert.Equal(At(14, 0), windows[0].ToUtc);
        Assert.Equal(At(14, 30), windows[1].FromUtc);
    }

    [Fact]
    public async Task Cua_so_vuot_qua_bien_khoang_hoi_van_tra_ve_bien_that()
    {
        // Jobless 15:00 → [14:30, 15:15) tràn qua mốc hỏi 15:00. Cắt cụt về 15:00 sẽ khiến
        // giao diện "48 giờ tới" hiện sai giờ kết thúc, và người đọc tưởng đã hết cấm lúc 15:00.
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddEventsAsync(Event(ScheduledEventKind.JoblessClaims, At(15, 0), MacroEventImpact.Low));

        var window = Assert.Single(await WindowsAsync(harness));

        Assert.Equal(At(14, 30), window.FromUtc);
        Assert.Equal(At(15, 15), window.ToUtc);
    }

    [Fact]
    public async Task Kiem_tra_mot_thoi_diem_tra_ve_dung_cua_so_da_hop_nhat()
    {
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddEventsAsync(
            Event(ScheduledEventKind.Cpi, At(13, 30), MacroEventImpact.High),
            Event(ScheduledEventKind.Pce, At(14, 10), MacroEventImpact.Medium));

        using var scope = harness.NewScope();
        var guard = scope.ServiceProvider.GetRequiredService<ITimeGuardService>();

        // 14:15 nằm ngoài cửa sổ CPI nhưng trong cửa sổ PCE — và trong khoảng hợp nhất.
        var decision = await guard.CheckAsync(harness.AccountId, "BTCUSDT", At(14, 15));

        Assert.True(decision.IsBlocked);
        Assert.Equal(At(12, 30), decision.Window!.FromUtc);
        Assert.Equal(At(14, 25), decision.Window.ToUtc);
    }

    [Fact]
    public async Task Cua_so_ke_tiep_bo_qua_cua_so_dang_dien_ra()
    {
        // Đang trong cửa sổ CPI, câu hỏi "sắp có cửa sổ nào" phải trả về cửa sổ SAU đó,
        // không phải cái đang diễn ra — lớp xử lý vị thế dùng nó để biết còn bao lâu.
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddEventsAsync(
            Event(ScheduledEventKind.Cpi, At(13, 30), MacroEventImpact.High),          // [12:30, 14:00)
            Event(ScheduledEventKind.JoblessClaims, At(15, 0), MacroEventImpact.Low)); // [14:30, 15:15)

        using var scope = harness.NewScope();
        var guard = scope.ServiceProvider.GetRequiredService<ITimeGuardService>();

        var upcoming = await guard.GetUpcomingAsync(harness.AccountId, At(13, 0), withinMinutes: 120);

        Assert.NotNull(upcoming);
        Assert.Equal(At(14, 30), upcoming!.FromUtc);
    }

    [Fact]
    public async Task Khong_co_cua_so_nao_trong_tam_hoi_thi_tra_ve_null()
    {
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddEventsAsync(Event(ScheduledEventKind.Cpi, At(13, 30), MacroEventImpact.High));

        using var scope = harness.NewScope();
        var guard = scope.ServiceProvider.GetRequiredService<ITimeGuardService>();

        // 10:00 + 30 phút = 10:30, còn cách cửa sổ 12:30 khá xa.
        Assert.Null(await guard.GetUpcomingAsync(harness.AccountId, At(10, 0), withinMinutes: 30));
    }
}
