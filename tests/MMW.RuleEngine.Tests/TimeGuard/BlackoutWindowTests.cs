using Microsoft.Extensions.DependencyInjection;
using MMW.Application.Trading.TimeGuard;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.TimeGuard;

/// <summary>
/// T048 / SC-006 — mỗi loại sự kiện được kiểm hai lần: một lần chứng minh nó CHẶN THẬT
/// bên trong biên, một lần chứng minh nó KHÔNG CHẶN NHẦM ngay ngoài biên.
/// </summary>
/// <remarks>
/// Chỉ kiểm "có chặn" là chưa đủ. Một hàm luôn trả về "chặn" sẽ qua được nửa số test, và
/// nó phá hệ thống theo cách khó thấy nhất: không bao giờ vào lệnh, mà zero lệnh lại là
/// kết quả hợp lệ của thiết kế này.
///
/// Mốc thử là 2026-08-05 13:30 UTC (thứ Tư) — cách xa cả ba mốc phí vốn 00/08/16, không
/// phải thứ Sáu đáo hạn, không phải Chủ nhật. Nếu chọn mốc gần các sự kiện sinh bằng công
/// thức thì phép hợp nhất cửa sổ sẽ nới biên và test "ngay ngoài biên" đỏ vì lý do khác.
/// </remarks>
public class BlackoutWindowTests
{
    private static readonly DateTime EventAt = new(2026, 8, 5, 13, 30, 0, DateTimeKind.Utc);

    /// <summary>Loại sự kiện, số phút chặn trước, số phút chặn sau, độ dài sự kiện.</summary>
    public static TheoryData<ScheduledEventKind, int, int, int?> Kinds() => new()
    {
        { ScheduledEventKind.Cpi,                 60, 30, null },
        { ScheduledEventKind.Ppi,                 60, 30, null },
        { ScheduledEventKind.Nfp,                 60, 30, null },
        { ScheduledEventKind.FomcStatement,       90, 30, null },
        { ScheduledEventKind.FomcPressConference,  0,  0, 60   },
        { ScheduledEventKind.Pce,                 30, 15, null },
        { ScheduledEventKind.Gdp,                 30, 15, null },
        { ScheduledEventKind.JoblessClaims,       30, 15, null },
        { ScheduledEventKind.OptionsExpiry,       30, 30, null },
        { ScheduledEventKind.FundingSettlement,    5,  5, null },
        { ScheduledEventKind.WeekendGap,           0,  0, 120  },
        { ScheduledEventKind.AiDetectedShock,      0, 60, null },
    };

    private static async Task<TimeGuardHarness> HarnessWithAsync(ScheduledEventKind kind, int? durationMinutes)
    {
        var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddEventsAsync(new ScheduledEvent
        {
            Kind = kind,
            Title = kind.ToString(),
            OccursAtUtc = EventAt,
            DurationMinutes = durationMinutes,
            Impact = MacroEventImpact.High,
            Origin = kind == ScheduledEventKind.AiDetectedShock
                ? ScheduledEventOrigin.AiDetected
                : ScheduledEventOrigin.Seeded,
            SourceKey = $"test:{kind}",
        });
        return harness;
    }

    private static async Task<BlackoutDecision> CheckAsync(TimeGuardHarness harness, DateTime utcNow)
    {
        using var scope = harness.NewScope();
        var guard = scope.ServiceProvider.GetRequiredService<ITimeGuardService>();
        return await guard.CheckAsync(harness.AccountId, "BTCUSDT", utcNow);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Chan_that_khi_dang_o_trong_cua_so(
        ScheduledEventKind kind, int before, int after, int? duration)
    {
        using var harness = await HarnessWithAsync(kind, duration);

        var windowFrom = EventAt.AddMinutes(-before);
        var windowTo = EventAt.AddMinutes((duration ?? 0) + after);

        foreach (var moment in new[] { windowFrom, EventAt, windowTo.AddMinutes(-1) })
        {
            var decision = await CheckAsync(harness, moment);

            Assert.True(decision.IsBlocked,
                $"{kind}: thời điểm {moment:HH:mm} nằm trong cửa sổ " +
                $"[{windowFrom:HH:mm}, {windowTo:HH:mm}) mà không bị chặn.");
            Assert.NotNull(decision.Window);
            Assert.False(string.IsNullOrWhiteSpace(decision.ReasonVi));
        }
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Khong_chan_nham_khi_vua_ra_khoi_cua_so(
        ScheduledEventKind kind, int before, int after, int? duration)
    {
        using var harness = await HarnessWithAsync(kind, duration);

        var windowFrom = EventAt.AddMinutes(-before);
        var windowTo = EventAt.AddMinutes((duration ?? 0) + after);

        // Nửa mở [from, to): đúng biên trên là ĐÃ RA khỏi cửa sổ.
        foreach (var moment in new[] { windowFrom.AddMinutes(-1), windowTo })
        {
            var decision = await CheckAsync(harness, moment);

            Assert.False(decision.IsBlocked,
                $"{kind}: thời điểm {moment:HH:mm} nằm ngoài cửa sổ " +
                $"[{windowFrom:HH:mm}, {windowTo:HH:mm}) mà vẫn bị chặn.");
            Assert.Null(decision.Window);
        }
    }

    [Fact]
    public async Task Cua_so_tra_ve_mang_dung_bien_va_dung_loai_su_kien()
    {
        using var harness = await HarnessWithAsync(ScheduledEventKind.Cpi, null);

        var decision = await CheckAsync(harness, EventAt);

        Assert.NotNull(decision.Window);
        Assert.Equal(EventAt.AddMinutes(-60), decision.Window!.FromUtc);
        Assert.Equal(EventAt.AddMinutes(30), decision.Window.ToUtc);
        Assert.Equal(ScheduledEventKind.Cpi, decision.Window.Kind);
        Assert.True(decision.Window.RequiresPositionAction);
    }

    [Fact]
    public async Task Ly_do_chan_neu_gio_Viet_Nam_de_trader_doi_chieu_duoc()
    {
        // Trader ở Việt Nam đọc giờ UTC sẽ phải tự cộng 7 — và sẽ có lúc cộng nhầm.
        using var harness = await HarnessWithAsync(ScheduledEventKind.Cpi, null);

        var decision = await CheckAsync(harness, EventAt);

        // Cửa sổ CPI là [12:30, 14:00) UTC = [19:30, 21:00) giờ Việt Nam.
        Assert.Contains("19:30", decision.ReasonVi);
        Assert.Contains("21:00", decision.ReasonVi);
    }

    [Fact]
    public async Task Lich_khong_co_su_kien_nao_gan_do_thi_cho_vao_lenh()
    {
        using var harness = await TimeGuardHarness.CreateAsync();

        var decision = await CheckAsync(harness, EventAt);

        Assert.False(decision.IsBlocked);
    }
}
