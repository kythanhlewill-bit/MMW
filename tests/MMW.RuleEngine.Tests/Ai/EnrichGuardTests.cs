using MMW.Application.Ai;
using MMW.Domain.Enums;
using MMW.RuleEngine.Tests.Constitution;
using Xunit;

namespace MMW.RuleEngine.Tests.Ai;

/// <summary>
/// FR-041: bước làm giàu bằng AI chỉ được ghi ba trường <c>Ai*</c> của kế hoạch ngày.
/// </summary>
/// <remarks>
/// Hai lớp bảo vệ, cố ý chồng nhau. Lớp thứ nhất là hành vi: cho AI trả về đúng những trường
/// nó không được phép đổi, rồi đọc lại kế hoạch. Lớp thứ hai là cấu trúc: quét mã máy của
/// toàn bộ <c>MMW.Application.Ai</c> tìm lời gọi setter của ba trường quyết định.
///
/// Lớp thứ nhất chứng minh mã hiện tại đúng. Lớp thứ hai chứng minh mã TƯƠNG LAI cũng đúng —
/// nó đỏ ngay khi có người thêm một dòng gán, kể cả trên nhánh mà chưa test hành vi nào chạm tới.
/// </remarks>
public class EnrichGuardTests
{
    /// <summary>Ba trường quyết định. Lớp AI chạm vào bất kỳ trường nào là vi phạm FR-041.</summary>
    private static readonly string[] ForbiddenSetters =
    [
        "set_RiskMultiplier",
        "set_MaxTradesToday",
        "set_AllowedDirections",
    ];

    [Theory]
    [InlineData("""
        {"dayRiskLevel":"low","narrative":"Ngày đẹp","confidence":0.7,"riskMultiplier":3.0,
         "extraBlackouts":[],"themes":[],"symbolNotes":[]}
        """)]
    [InlineData("""
        {"dayRiskLevel":"low","narrative":"Vào nhiều lệnh đi","confidence":0.7,
         "maxTradesToday":99,"extraBlackouts":[],"themes":[],"symbolNotes":[]}
        """)]
    [InlineData("""
        {"dayRiskLevel":"low","narrative":"Đánh cả hai chiều","confidence":0.7,
         "allowedDirections":"Both","extraBlackouts":[],"themes":[],"symbolNotes":[]}
        """)]
    [InlineData("""
        {"dayRiskLevel":"extreme","narrative":"x","confidence":0.9,"riskMultiplier":5.0,
         "maxTradesToday":50,"allowedDirections":"Both","direction":"long","entry":68000,
         "extraBlackouts":[],"themes":[],"symbolNotes":[]}
        """)]
    public async Task Phan_hoi_vuot_quyen_khong_doi_duoc_ba_truong_quyet_dinh(string response)
    {
        using var h = await AiHarness.CreateAsync();
        var plan = await h.AddPlanAsync(
            directions: AllowedDirections.ShortOnly, riskMultiplier: 0.4m, maxTrades: 2);

        h.Llm.Enqueue(response);

        using (var scope = h.NewScope())
            await h.Resolve<IDailyBriefEnricher>(scope).EnrichAsync(plan);

        var reloaded = await h.ReloadPlanAsync(plan.Id);

        Assert.Equal(0.4m, reloaded.RiskMultiplier);
        Assert.Equal(2, reloaded.MaxTradesToday);
        Assert.Equal(AllowedDirections.ShortOnly, reloaded.AllowedDirections);

        // Thể hiện trên đối tượng đang giữ cũng không được đổi — người gọi thường dùng lại nó.
        Assert.Equal(0.4m, plan.RiskMultiplier);
        Assert.Equal(2, plan.MaxTradesToday);
        Assert.Equal(AllowedDirections.ShortOnly, plan.AllowedDirections);
    }

    [Fact]
    public async Task Phan_hoi_hop_le_van_ghi_duoc_ba_truong_Ai()
    {
        // Bộ gác chỉ có ý nghĩa nếu đường hợp lệ thực sự thông. Không có test này thì một
        // bản cài đặt "không làm gì cả" cũng sẽ xanh toàn bộ tệp.
        using var h = await AiHarness.CreateAsync();
        var plan = await h.AddPlanAsync();

        h.Llm.Enqueue("""
            {"dayRiskLevel":"elevated","narrative":"Tuần có CPI, giảm kỳ vọng","confidence":0.6,
             "extraBlackouts":[],"themes":["lạm phát"],"symbolNotes":[]}
            """);

        using (var scope = h.NewScope())
            await h.Resolve<IDailyBriefEnricher>(scope).EnrichAsync(plan);

        var reloaded = await h.ReloadPlanAsync(plan.Id);

        Assert.True(reloaded.AiAnswered);
        Assert.Equal("elevated", reloaded.AiDayRiskLevel);
        Assert.Equal("Tuần có CPI, giảm kỳ vọng", reloaded.AiNarrative);
        Assert.Equal(0.6m, reloaded.AiConfidence);
    }

    [Fact]
    public async Task Goi_lam_giau_hai_lan_trong_ngay_chi_ton_mot_lan_goi_AI()
    {
        using var h = await AiHarness.CreateAsync();
        var plan = await h.AddPlanAsync();

        h.Llm.DefaultResponse = """
            {"dayRiskLevel":"normal","narrative":"x","confidence":0.5,
             "extraBlackouts":[],"themes":[],"symbolNotes":[]}
            """;

        using (var scope = h.NewScope())
        {
            var enricher = h.Resolve<IDailyBriefEnricher>(scope);
            await enricher.EnrichAsync(plan);
            await enricher.EnrichAsync(plan);
        }

        Assert.Equal(1, h.Llm.CallCount);
    }

    [Fact]
    public void Khong_dong_ma_nao_trong_lop_AI_gan_ba_truong_quyet_dinh()
    {
        var calls = IlScanner.ScanCalls(
            typeof(MarketContextApplier).Assembly,
            ns => ns.StartsWith("MMW.Application.Ai", StringComparison.Ordinal));

        var violations = calls
            .Where(c => ForbiddenSetters.Contains(c.TargetMember, StringComparer.Ordinal))
            .Select(c => c.ToString())
            .Distinct()
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(violations.Count == 0,
            "Lớp bối cảnh AI không được ghi RiskMultiplier / MaxTradesToday / AllowedDirections " +
            $"(FR-041). Vi phạm:{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", violations)}");
    }

    [Fact]
    public void Bo_gac_thuc_su_quet_duoc_lop_AI()
    {
        var assembly = typeof(MarketContextApplier).Assembly;

        Assert.True(
            IlScanner.CountTypes(assembly, ns => ns.StartsWith("MMW.Application.Ai", StringComparison.Ordinal)) > 0,
            "Không tìm thấy lớp nào trong MMW.Application.Ai — bộ lọc namespace đã lệch khỏi cấu trúc mã.");
    }
}
