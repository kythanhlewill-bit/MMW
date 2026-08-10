using Microsoft.Extensions.DependencyInjection;
using MMW.Application.Trading.TimeGuard;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.TimeGuard;

/// <summary>
/// T052 / FR-011 — cửa sổ chặn do AI đề xuất bị cắt về <c>EngineSetting.AiBlackoutMaxMinutes</c>.
/// </summary>
/// <remarks>
/// Đây là chỗ ranh giới "AI chỉ được veto hoặc giảm" có thể bị lách mà không ai để ý: AI không
/// tạo lệnh, không chọn hướng, không tăng size — nó chỉ đề xuất một cửa sổ chặn dài 20 tiếng, và
/// thế là nó đã tắt hệ thống cả ngày. Cưỡng chế bằng SỐ HỌC ở phía nhận, không bằng lời dặn
/// trong prompt: prompt có thể bị mô hình phớt lờ, phép cắt thì không.
/// </remarks>
public class AiWindowCapTests
{
    private static readonly DateTime ShockAt = new(2026, 8, 5, 10, 0, 0, DateTimeKind.Utc);

    private static DateTime At(int hour, int minute = 0) =>
        new(2026, 8, 5, hour, minute, 0, DateTimeKind.Utc);

    private static ScheduledEvent AiShock(int durationMinutes) => new()
    {
        Kind = ScheduledEventKind.AiDetectedShock,
        Title = "Tin sốc",
        OccursAtUtc = ShockAt,
        DurationMinutes = durationMinutes,
        Impact = MacroEventImpact.High,
        Origin = ScheduledEventOrigin.AiDetected,
        SourceKey = "ai:shock:test",
    };

    private static async Task<IReadOnlyList<BlackoutWindow>> WindowsAsync(TimeGuardHarness harness)
    {
        using var scope = harness.NewScope();
        var guard = scope.ServiceProvider.GetRequiredService<ITimeGuardService>();
        return await guard.GetWindowsAsync(harness.AccountId, At(9), At(15, 30));
    }

    [Fact]
    public async Task Cua_so_AI_dai_20_tieng_bi_cat_ve_tran_cau_hinh()
    {
        // AI đề xuất 1200 phút; cộng thêm 60 phút "sau sự kiện" của luật là 1260 phút.
        // Trần mặc định là 120 phút.
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddEventsAsync(AiShock(durationMinutes: 1200));

        var window = Assert.Single(await WindowsAsync(harness));

        Assert.Equal(ShockAt, window.FromUtc);
        Assert.Equal(ShockAt.AddMinutes(120), window.ToUtc);
    }

    [Fact]
    public async Task Tran_lay_tu_cau_hinh_chu_khong_viet_cung()
    {
        using var harness = await TimeGuardHarness.CreateAsync(s => s.AiBlackoutMaxMinutes = 30);
        await harness.AddEventsAsync(AiShock(durationMinutes: 1200));

        var window = Assert.Single(await WindowsAsync(harness));

        Assert.Equal(ShockAt.AddMinutes(30), window.ToUtc);
    }

    [Fact]
    public async Task Cua_so_AI_ngan_hon_tran_khong_bi_keo_dai_ra()
    {
        // Phép cắt là trần, không phải giá trị cố định. Kéo dài lên cho "đủ trần" chính là
        // để AI tăng mức chặn — chiều ngược lại của cái đang bị cấm.
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddEventsAsync(AiShock(durationMinutes: 10));

        var window = Assert.Single(await WindowsAsync(harness));

        Assert.Equal(ShockAt.AddMinutes(70), window.ToUtc);   // 10 phút sự kiện + 60 phút sau
    }

    [Fact]
    public async Task Su_kien_nap_tay_dai_hon_tran_KHONG_bi_cat()
    {
        // Trần chỉ áp cho nguồn AI. Một buổi họp báo FOMC kéo 5 tiếng do người nạp vào là sự
        // thật đã kiểm chứng; cắt nó về 2 tiếng là tự bỏ ba tiếng bảo vệ.
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddEventsAsync(new ScheduledEvent
        {
            Kind = ScheduledEventKind.FomcPressConference,
            Title = "Họp báo FOMC",
            OccursAtUtc = ShockAt,
            DurationMinutes = 300,
            Impact = MacroEventImpact.Critical,
            Origin = ScheduledEventOrigin.Seeded,
            SourceKey = "fed:presser:test",
        });

        var window = Assert.Single(await WindowsAsync(harness));

        Assert.Equal(ShockAt.AddMinutes(300), window.ToUtc);
    }

    [Theory]
    [InlineData(10, 0, true)]     // đầu cửa sổ đã cắt
    [InlineData(11, 59, true)]    // sát cuối
    [InlineData(12, 0, false)]    // đúng biên trên: đã ra ngoài
    [InlineData(13, 0, false)]    // nằm trong cửa sổ 1260 phút gốc — phải KHÔNG bị chặn
    public async Task Chi_chan_trong_pham_vi_da_cat(int hour, int minute, bool expectBlocked)
    {
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddEventsAsync(AiShock(durationMinutes: 1200));

        using var scope = harness.NewScope();
        var guard = scope.ServiceProvider.GetRequiredService<ITimeGuardService>();

        var decision = await guard.CheckAsync(harness.AccountId, "BTCUSDT", At(hour, minute));

        Assert.Equal(expectBlocked, decision.IsBlocked);
    }

    [Fact]
    public async Task Cat_truoc_roi_moi_hop_nhat_chu_khong_nguoc_lai()
    {
        // Cửa sổ AI gốc 10:00→07:00 hôm sau trùm qua mốc phí vốn 16:00. Nếu hợp nhất trước
        // rồi mới cắt, phần bảo vệ 16:00 sẽ bị nuốt vào một khối rồi bị cắt mất — mốc phí vốn
        // biến mất dù nó chẳng liên quan gì đến AI.
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddEventsAsync(AiShock(durationMinutes: 1200));

        using var scope = harness.NewScope();
        var guard = scope.ServiceProvider.GetRequiredService<ITimeGuardService>();

        var decision = await guard.CheckAsync(harness.AccountId, "BTCUSDT", At(16, 0));

        Assert.True(decision.IsBlocked);
        Assert.Equal(ScheduledEventKind.FundingSettlement, decision.Window!.Kind);
    }
}
