using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using MMW.Application.Trading.DailyPlanning;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using MMW.RuleEngine.Tests.TimeGuard;
using Xunit;

namespace MMW.RuleEngine.Tests.DailyPlanning;

/// <summary>
/// T070 / FR-023, FR-021 — chưa có kế hoạch hợp lệ ⟹ chặn mọi lệnh mới, và KHÔNG tồn tại
/// đường dẫn nào trả về kế hoạch mặc định cho phép giao dịch.
/// </summary>
/// <remarks>
/// Cách cưỡng chế mạnh nhất cho "cấm dùng kế hoạch mặc định" không phải là một câu lệnh
/// <c>if</c>, mà là không cung cấp đường dẫn nào tạo ra nó. Test cuối tệp gác đúng điều đó
/// bằng phản chiếu: một ngày nào đó có người thêm <c>GetOrDefaultAsync</c> cho tiện, và mọi
/// test khác vẫn xanh.
/// </remarks>
public class NoPlanBlocksTests
{
    [Fact]
    public async Task Chua_co_ke_hoach_thi_GetCurrentAsync_tra_null()
    {
        using var harness = await TimeGuardHarness.CreateAsync();

        using var scope = harness.NewScope();
        var service = scope.ServiceProvider.GetRequiredService<IDailyPlanService>();

        Assert.Null(await service.GetCurrentAsync(harness.AccountId));
    }

    [Theory]
    [InlineData(TradeDirection.Long)]
    [InlineData(TradeDirection.Short)]
    public void Khong_co_ke_hoach_thi_moi_chieu_deu_bi_chan(TradeDirection direction)
    {
        Assert.Equal(VetoReason.NoDailyPlan, DailyPlanGate.Check(null, direction));
    }

    [Theory]
    [InlineData(AllowedDirections.LongOnly, TradeDirection.Long, null)]
    [InlineData(AllowedDirections.LongOnly, TradeDirection.Short, VetoReason.DirectionNotAllowed)]
    [InlineData(AllowedDirections.ShortOnly, TradeDirection.Short, null)]
    [InlineData(AllowedDirections.ShortOnly, TradeDirection.Long, VetoReason.DirectionNotAllowed)]
    [InlineData(AllowedDirections.Both, TradeDirection.Long, null)]
    [InlineData(AllowedDirections.Both, TradeDirection.Short, null)]
    [InlineData(AllowedDirections.None, TradeDirection.Long, VetoReason.DirectionNotAllowed)]
    [InlineData(AllowedDirections.None, TradeDirection.Short, VetoReason.DirectionNotAllowed)]
    public void Chieu_khong_duoc_phep_thi_bi_tu_choi(
        AllowedDirections allowed, TradeDirection direction, VetoReason? expected)
    {
        // FR-021: bất kể điểm số. Cổng này không nhận điểm nên không có cách nào lách.
        var plan = new DailyPlan { AllowedDirections = allowed, MaxTradesToday = 5 };

        Assert.Equal(expected, DailyPlanGate.Check(plan, direction));
    }

    [Fact]
    public void Ke_hoach_khong_cho_lenh_nao_thi_chan_ca_hai_chieu()
    {
        var plan = new DailyPlan { AllowedDirections = AllowedDirections.Both, MaxTradesToday = 0 };

        Assert.Equal(VetoReason.MaxTradesReached, DailyPlanGate.Check(plan, TradeDirection.Long));
    }

    [Fact]
    public async Task Ke_hoach_cua_ngay_khac_khong_duoc_dung_thay_cho_hom_nay()
    {
        // FR-024 neo ngày giao dịch tại 00:00 UTC. Lấy nhầm kế hoạch hôm qua là cách âm thầm
        // nhất để một ngày nguy hiểm thừa hưởng hệ số của một ngày lành.
        using var harness = await TimeGuardHarness.CreateAsync();
        harness.MarketData.Candles["BTCUSDT"] = DailyPlanFixtures.FlatClose(Enumerable.Repeat(10m, 104));

        using var scope = harness.NewScope();
        var service = scope.ServiceProvider.GetRequiredService<IDailyPlanService>();

        harness.Clock.UtcNow = new DateTime(2026, 8, 5, 12, 0, 0, DateTimeKind.Utc);
        await service.GenerateAsync(harness.AccountId, new DateOnly(2026, 8, 5));

        Assert.NotNull(await service.GetCurrentAsync(harness.AccountId));

        // 00:00 UTC hôm sau là ranh giới ngày giao dịch — kế hoạch hôm qua hết hiệu lực ngay.
        harness.Clock.UtcNow = new DateTime(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc);
        Assert.Null(await service.GetCurrentAsync(harness.AccountId));
    }

    // ── Bộ gác: không có đường dẫn nào tạo kế hoạch mặc định ────────────

    [Fact]
    public void IDailyPlanService_chi_co_dung_hai_phuong_thuc_cua_contract()
    {
        var names = typeof(IDailyPlanService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { nameof(IDailyPlanService.GenerateAsync), nameof(IDailyPlanService.GetCurrentAsync) }, names);
    }

    [Fact]
    public void Khong_thanh_vien_cong_khai_nao_nghe_giong_ke_hoach_mac_dinh()
    {
        var suspicious = new[] { "Default", "Fallback", "Placeholder", "Empty", "Permissive" };

        var offenders = typeof(IDailyPlanService).Assembly.GetTypes()
            .Where(t => t.Namespace == "MMW.Application.Trading.DailyPlanning")
            .SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(m => (Type: t, Member: m)))
            .Where(x => x.Member switch
            {
                MethodInfo m => m.ReturnType == typeof(DailyPlan) || m.ReturnType == typeof(Task<DailyPlan>),
                PropertyInfo p => p.PropertyType == typeof(DailyPlan),
                FieldInfo f => f.FieldType == typeof(DailyPlan),
                _ => false,
            })
            .Where(x => suspicious.Any(s => x.Member.Name.Contains(s, StringComparison.OrdinalIgnoreCase)))
            .Select(x => $"{x.Type.Name}.{x.Member.Name}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "FR-023 cấm kế hoạch mặc định cho phép giao dịch. Thành viên đáng ngờ: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Cong_kiem_tra_khong_nhan_diem_so_nen_khong_the_lach_bang_diem_cao()
    {
        // FR-021 nói "bất kể điểm số". Cách chắc chắn nhất là chữ ký không có chỗ nhét điểm vào.
        var parameters = typeof(DailyPlanGate)
            .GetMethod(nameof(DailyPlanGate.Check))!
            .GetParameters()
            .Select(p => p.ParameterType)
            .ToArray();

        Assert.Equal(new[] { typeof(DailyPlan), typeof(TradeDirection) }, parameters);
    }
}
