using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MMW.Application.MarketData.Models;
using MMW.Application.Trading.DailyPlanning;
using MMW.Domain.DbContext;
using MMW.RuleEngine.Tests.TimeGuard;
using Xunit;

namespace MMW.RuleEngine.Tests.DailyPlanning;

/// <summary>
/// T069 / bất biến 4 — <c>GenerateAsync</c> gọi hai lần trong cùng ngày trả về CÙNG bản ghi
/// và không ghi đè.
/// </summary>
/// <remarks>
/// Kế hoạch đổi giữa ngày thì mọi phiếu chấm điểm trước đó mất ngữ cảnh, và bản ghi kiểm toán
/// trở thành vô nghĩa: không còn cách nào trả lời "lúc 10 giờ hệ thống nghĩ gì". Job chạy lại
/// sau một lần khởi động lại giữa ngày là chuyện bình thường, nên tính bất biến này không phải
/// trường hợp hiếm.
/// </remarks>
public class DailyPlanIdempotencyTests
{
    private static readonly DateOnly PlanDate = new(2026, 8, 5);

    private static IReadOnlyList<Candle> Series(decimal range) =>
        DailyPlanFixtures.FlatClose(Enumerable.Repeat(range, 104));

    private static async Task<TimeGuardHarness> HarnessAsync(decimal range = 10m)
    {
        var harness = await TimeGuardHarness.CreateAsync();
        harness.MarketData.Candles["BTCUSDT"] = Series(range);
        harness.MarketData.FearGreed = 55;
        return harness;
    }

    private static async Task<Domain.Entities.DailyPlan> GenerateAsync(TimeGuardHarness harness)
    {
        using var scope = harness.NewScope();
        var service = scope.ServiceProvider.GetRequiredService<IDailyPlanService>();
        return await service.GenerateAsync(harness.AccountId, PlanDate);
    }

    [Fact]
    public async Task Goi_hai_lan_cung_ngay_tra_ve_cung_ban_ghi()
    {
        using var harness = await HarnessAsync();

        var first = await GenerateAsync(harness);
        var second = await GenerateAsync(harness);

        Assert.NotEqual(0, first.Id);
        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task Goi_lai_khong_sinh_them_dong_trong_co_so_du_lieu()
    {
        using var harness = await HarnessAsync();

        await GenerateAsync(harness);
        await GenerateAsync(harness);
        await GenerateAsync(harness);

        using var scope = harness.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();

        Assert.Equal(1, await db.DailyPlans.CountAsync(p => p.PlanDateUtc == PlanDate));
    }

    [Fact]
    public async Task Du_lieu_thi_truong_doi_giua_ngay_cung_KHONG_ghi_de_ban_da_co()
    {
        // Đây mới là phép thử thật của tính bất biến: gọi lại với dữ liệu khác hẳn.
        // Trả về cùng Id nhưng âm thầm cập nhật các trường thì vẫn là ghi đè.
        using var harness = await HarnessAsync(range: 10m);

        var first = await GenerateAsync(harness);
        var originalGeneratedAt = first.GeneratedAtUtc;
        var originalVolatility = first.VolatilityRegime;

        harness.MarketData.Candles["BTCUSDT"] =
            DailyPlanFixtures.FlatClose(Enumerable.Repeat(10m, 79).Concat(Enumerable.Repeat(500m, 25)));
        harness.MarketData.FearGreed = 5;

        await GenerateAsync(harness);

        using var scope = harness.NewScope();
        var db = scope.ServiceProvider.GetRequiredService<MmwDbContext>();
        var stored = await db.DailyPlans.SingleAsync(p => p.PlanDateUtc == PlanDate);

        Assert.Equal(originalGeneratedAt, stored.GeneratedAtUtc);
        Assert.Equal(originalVolatility, stored.VolatilityRegime);
        Assert.Equal(55, stored.FearGreedIndex);
    }

    [Fact]
    public async Task Ngay_khac_thi_sinh_ban_ghi_khac()
    {
        using var harness = await HarnessAsync();

        var day1 = await GenerateAsync(harness);

        using var scope = harness.NewScope();
        var service = scope.ServiceProvider.GetRequiredService<IDailyPlanService>();
        var day2 = await service.GenerateAsync(harness.AccountId, PlanDate.AddDays(1));

        Assert.NotEqual(day1.Id, day2.Id);
    }

    [Fact]
    public async Task Ke_hoach_ghi_lai_dau_vao_da_dung_de_truy_vet_duoc()
    {
        // FR-017 đòi lưu đầu vào. Không lưu thì ba tháng sau không ai giải thích nổi vì sao
        // ngày đó hệ số là 0.4.
        using var harness = await HarnessAsync();
        harness.MarketData.FundingRate = 0.0002m;
        harness.MarketData.FearGreed = 42;

        var plan = await GenerateAsync(harness);

        Assert.Equal(42, plan.FearGreedIndex);
        Assert.Equal(0.0002m, plan.FundingRate);
        Assert.False(string.IsNullOrWhiteSpace(plan.BtcStructure));
        Assert.Equal(PlanDate, plan.PlanDateUtc);
    }

    [Fact]
    public async Task Thieu_nguon_du_lieu_thi_ghi_ro_thanh_phan_thieu_va_danh_dau_chua_day_du()
    {
        using var harness = await TimeGuardHarness.CreateAsync();   // không nạp nến, không tâm lý

        var plan = await GenerateAsync(harness);

        Assert.False(plan.IsComplete);
        Assert.False(string.IsNullOrWhiteSpace(plan.MissingInputs));
        Assert.True(plan.RiskMultiplier <= 0.5m);
    }

    [Fact]
    public async Task Nguon_du_lieu_nem_ngoai_le_khong_lam_chet_viec_sinh_ke_hoach()
    {
        // Nguồn giá ném khi chưa đặt dữ liệu; kế hoạch vẫn phải ra đời, chỉ là thận trọng hơn.
        using var harness = await TimeGuardHarness.CreateAsync();
        harness.MarketData.ThrowOnCandles = true;

        var plan = await GenerateAsync(harness);

        Assert.NotNull(plan);
        Assert.False(plan.IsComplete);
    }
}
