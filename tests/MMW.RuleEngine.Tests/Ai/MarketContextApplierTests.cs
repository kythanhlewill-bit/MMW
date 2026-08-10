using MMW.Application.Ai;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.Ai;

/// <summary>
/// Mười hai trường hợp chống lạm quyền của <c>contracts/ai-context.md</c>, đo ở ĐIỂM CUỐI:
/// hệ số AI nhân vào kích thước lệnh.
/// </summary>
/// <remarks>
/// Các test kiểm chứng từng trường trong phản hồi nằm ở <c>DailyBriefValidationTests</c> và
/// <c>NewsClassifierValidationTests</c>. Tệp này hỏi một câu duy nhất, cho cả mười hai kiểu
/// phản hồi dị thường: <b>có phản hồi nào làm quyết định RỦI RO HƠN không?</b>
///
/// Đó là điều kiện kiểm thử độc lập của US6, và nó không thể suy ra từ các test từng trường:
/// mỗi trường có thể được xử lý đúng mà tổng hợp lại vẫn sai.
/// </remarks>
public class MarketContextApplierTests
{
    private const string Symbol = "BTCUSDT";

    /// <summary>Phản hồi tin đúng khuôn, mức nghiêm trọng và chiều do test đặt.</summary>
    private static string NewsJson(string severity, string leaning, int halfLife = 120) => $$"""
        {"severity":"{{severity}}","affectedSymbols":["BTCUSDT"],"leaning":"{{leaning}}",
         "halfLifeMinutes":{{halfLife}},"isRumor":false}
        """;

    /// <summary>Chạy trọn vòng phân loại tin rồi trả hệ số mà tầng chấm điểm sẽ nhận được.</summary>
    private static async Task<decimal> MultiplierAfterNewsAsync(
        AiHarness h, string response, TradeDirection direction, string symbol = Symbol)
    {
        h.Headlines.Add("tin-1", "Một tiêu đề nào đó");
        h.Llm.Enqueue(response);

        using var scope = h.NewScope();
        await h.Resolve<IMarketContextService>(scope).ClassifyNewsAsync();

        return await MultiplierAsync(h, direction, symbol);
    }

    private static async Task<decimal> MultiplierAsync(AiHarness h, TradeDirection direction, string symbol = Symbol)
    {
        using var scope = h.NewScope();
        var context = await h.Resolve<IMarketContextService>(scope).GetActiveAsync(symbol);
        return h.Resolve<IMarketContextApplier>(scope).GetSizeMultiplier(context, symbol, direction);
    }

    // ── Ca 1–8: phản hồi dị thường KHÔNG được làm quyết định rủi ro hơn ──────

    [Fact]
    public async Task Ca1_AI_tra_kem_lenh_giao_dich_thi_toan_bo_phan_hoi_bi_loai()
    {
        using var h = await AiHarness.CreateAsync();
        var plan = await h.AddPlanAsync();

        h.Llm.Enqueue("""
            {"dayRiskLevel":"extreme","narrative":"Rủi ro cao","confidence":0.7,
             "extraBlackouts":[],"themes":[],"symbolNotes":[],
             "entry":68000,"stopLoss":67000,"takeProfit":70000}
            """);

        using (var scope = h.NewScope())
            await h.Resolve<IDailyBriefEnricher>(scope).EnrichAsync(plan);

        var reloaded = await h.ReloadPlanAsync(plan.Id);
        Assert.Null(reloaded.AiDayRiskLevel);
        Assert.Null(reloaded.AiNarrative);

        var records = await h.ContextRecordsAsync();
        Assert.All(records, r => Assert.False(string.IsNullOrWhiteSpace(r.RejectedFields)));

        Assert.Equal(1.0m, await MultiplierAsync(h, TradeDirection.Long));
    }

    [Fact]
    public async Task Ca2_Confidence_099_bi_cat_ve_08()
    {
        using var h = await AiHarness.CreateAsync();
        var plan = await h.AddPlanAsync();

        h.Llm.Enqueue("""
            {"dayRiskLevel":"normal","narrative":"Ngày bình thường","confidence":0.99,
             "extraBlackouts":[],"themes":[],"symbolNotes":[]}
            """);

        using (var scope = h.NewScope())
            await h.Resolve<IDailyBriefEnricher>(scope).EnrichAsync(plan);

        var reloaded = await h.ReloadPlanAsync(plan.Id);
        Assert.Equal(0.8m, reloaded.AiConfidence);
        Assert.Equal(1.0m, await MultiplierAsync(h, TradeDirection.Long));
    }

    [Fact]
    public async Task Ca3_Su_kien_bia_khong_co_trong_lich_van_vao_duoc_nhung_duoc_ghi_vet()
    {
        // Cửa sổ do AI đề xuất là đường HỢP LỆ duy nhất cho tin đột xuất (FR-011), nên nó
        // KHÔNG bị loại chỉ vì không có trong lịch — nó bị loại khi TRÙNG lịch đã có. Ca này
        // chốt cả hai vế: sự kiện thật thì loại, tin đột xuất thì giữ nhưng phải là AiDetected.
        using var h = await AiHarness.CreateAsync();
        var planDate = DateOnly.FromDateTime(h.Clock.UtcNow);
        var plan = await h.AddPlanAsync(planDate);

        var realEvent = planDate.ToDateTime(new TimeOnly(13, 30), DateTimeKind.Utc);
        await h.AddEventsAsync(new ScheduledEvent
        {
            Kind = ScheduledEventKind.Cpi,
            Title = "CPI tháng 3",
            OccursAtUtc = realEvent,
            Impact = MacroEventImpact.Critical,
            Origin = ScheduledEventOrigin.Seeded,
            SourceKey = "bls:cpi:2026-03",
        });

        h.Llm.Enqueue($$"""
            {"dayRiskLevel":"elevated","narrative":"Có tin","confidence":0.5,
             "extraBlackouts":[
               {"fromUtc":"{{realEvent.AddMinutes(-10):yyyy-MM-ddTHH:mm:ssZ}}",
                "toUtc":"{{realEvent.AddMinutes(20):yyyy-MM-ddTHH:mm:ssZ}}",
                "reason":"CPI tháng 3","severity":"high"}],
             "themes":[],"symbolNotes":[]}
            """);

        using (var scope = h.NewScope())
            await h.Resolve<IDailyBriefEnricher>(scope).EnrichAsync(plan);

        Assert.Empty(await h.AiEventsAsync());

        var record = Assert.Single(await h.ContextRecordsAsync());
        Assert.Contains("extraBlackouts", record.RejectedFields ?? "");
    }

    [Fact]
    public async Task Ca4_Cua_so_chan_20_tieng_bi_cat_ve_tran_cau_hinh()
    {
        using var h = await AiHarness.CreateAsync();
        var plan = await h.AddPlanAsync();
        var start = h.Clock.UtcNow.AddHours(2);

        h.Llm.Enqueue($$"""
            {"dayRiskLevel":"extreme","narrative":"Sốc","confidence":0.6,
             "extraBlackouts":[
               {"fromUtc":"{{start:yyyy-MM-ddTHH:mm:ssZ}}",
                "toUtc":"{{start.AddHours(20):yyyy-MM-ddTHH:mm:ssZ}}",
                "reason":"Sàn lớn dừng rút tiền","severity":"high"}],
             "themes":[],"symbolNotes":[]}
            """);

        using (var scope = h.NewScope())
            await h.Resolve<IDailyBriefEnricher>(scope).EnrichAsync(plan);

        var evt = Assert.Single(await h.AiEventsAsync());
        Assert.Equal(ScheduledEventKind.AiDetectedShock, evt.Kind);
        Assert.Equal(120, evt.DurationMinutes);
    }

    [Fact]
    public async Task Ca5_JSON_hong_thi_boi_canh_trung_tinh()
    {
        using var h = await AiHarness.CreateAsync();
        var plan = await h.AddPlanAsync();

        h.Llm.Enqueue("{\"dayRiskLevel\": \"extreme\", \"narrative\": bị cắt giữa chừng");

        using (var scope = h.NewScope())
            await h.Resolve<IDailyBriefEnricher>(scope).EnrichAsync(plan);

        var reloaded = await h.ReloadPlanAsync(plan.Id);
        Assert.Null(reloaded.AiDayRiskLevel);
        Assert.Equal(1.0m, await MultiplierAsync(h, TradeDirection.Long));
    }

    [Fact]
    public async Task Ca6_Chuoi_rong_thi_boi_canh_trung_tinh()
    {
        using var h = await AiHarness.CreateAsync();
        var plan = await h.AddPlanAsync();

        h.Llm.Enqueue("");

        using (var scope = h.NewScope())
            await h.Resolve<IDailyBriefEnricher>(scope).EnrichAsync(plan);

        Assert.False((await h.ReloadPlanAsync(plan.Id)).AiAnswered);
        Assert.Equal(1.0m, await MultiplierAsync(h, TradeDirection.Long));
    }

    [Fact]
    public async Task Ca7_Dich_vu_AI_nem_ngoai_le_thi_vong_quyet_dinh_van_chay()
    {
        using var h = await AiHarness.CreateAsync();
        var plan = await h.AddPlanAsync();
        h.Llm.Throws = true;

        using (var scope = h.NewScope())
            await h.Resolve<IDailyBriefEnricher>(scope).EnrichAsync(plan);

        var reloaded = await h.ReloadPlanAsync(plan.Id);
        Assert.False(reloaded.AiAnswered);
        Assert.Equal(1.0m, reloaded.RiskMultiplier);
        Assert.Equal(1.0m, await MultiplierAsync(h, TradeDirection.Long));
    }

    [Fact]
    public async Task Ca8_AI_chua_cau_hinh_thi_khong_goi_mang_va_he_so_bang_1()
    {
        using var h = await AiHarness.CreateAsync();
        var plan = await h.AddPlanAsync();
        h.Llm.Configured = false;
        h.Headlines.Add("tin-1", "Một tiêu đề nào đó");

        using (var scope = h.NewScope())
        {
            await h.Resolve<IDailyBriefEnricher>(scope).EnrichAsync(plan);
            await h.Resolve<IMarketContextService>(scope).ClassifyNewsAsync();
        }

        Assert.Equal(0, h.Llm.CallCount);
        Assert.Equal(1.0m, await MultiplierAsync(h, TradeDirection.Long));
    }

    // ── Ca 9–11: ranh giới thật của Nguyên tắc II ───────────────────────────

    [Fact]
    public async Task Ca9_Boi_canh_critical_nguoc_chieu_lenh_thi_veto_hoan_toan()
    {
        using var h = await AiHarness.CreateAsync();

        var multiplier = await MultiplierAfterNewsAsync(
            h, NewsJson("critical", "bearish"), TradeDirection.Long);

        Assert.Equal(0.0m, multiplier);
    }

    [Fact]
    public async Task Ca10_Boi_canh_critical_THUAN_chieu_lenh_KHONG_lam_lenh_to_hon()
    {
        // Trường hợp dễ bị bỏ sót nhất. Một bối cảnh lạc quan mạnh mẽ KHÔNG phải lý do
        // vào lệnh to hơn — AI chỉ có một hướng tác động, và hướng đó là xuống.
        using var h = await AiHarness.CreateAsync();

        var multiplier = await MultiplierAfterNewsAsync(
            h, NewsJson("critical", "bullish"), TradeDirection.Long);

        Assert.Equal(1.0m, multiplier);
    }

    [Fact]
    public async Task Ca11_Boi_canh_da_het_han_thi_he_so_bang_1()
    {
        using var h = await AiHarness.CreateAsync();

        // Tin nghiêm trọng nhất, ngược chiều, nhưng chu kỳ bán rã chỉ 30 phút.
        var multiplier = await MultiplierAfterNewsAsync(
            h, NewsJson("critical", "bearish", halfLife: 30), TradeDirection.Long);
        Assert.Equal(0.0m, multiplier);

        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(31);
        Assert.Equal(1.0m, await MultiplierAsync(h, TradeDirection.Long));
    }

    [Fact]
    public async Task Ca12_Phan_hoi_vi_pham_khong_doi_duoc_ba_truong_quyet_dinh_cua_ke_hoach()
    {
        using var h = await AiHarness.CreateAsync();
        var plan = await h.AddPlanAsync(directions: AllowedDirections.LongOnly, riskMultiplier: 0.5m, maxTrades: 3);

        h.Llm.Enqueue("""
            {"dayRiskLevel":"low","narrative":"Ngày đẹp, tăng rủi ro lên",
             "confidence":0.95,"riskMultiplier":2.0,"maxTradesToday":20,
             "allowedDirections":"Both","extraBlackouts":[],"themes":[],"symbolNotes":[]}
            """);

        using (var scope = h.NewScope())
            await h.Resolve<IDailyBriefEnricher>(scope).EnrichAsync(plan);

        var reloaded = await h.ReloadPlanAsync(plan.Id);
        Assert.Equal(0.5m, reloaded.RiskMultiplier);
        Assert.Equal(3, reloaded.MaxTradesToday);
        Assert.Equal(AllowedDirections.LongOnly, reloaded.AllowedDirections);
    }

    // ── Bảng mức nghiêm trọng và điều kiện áp dụng ──────────────────────────

    [Theory]
    [InlineData("noise", 1.0)]
    [InlineData("low", 1.0)]
    [InlineData("medium", 0.75)]
    [InlineData("high", 0.5)]
    [InlineData("critical", 0.0)]
    public async Task Bang_muc_nghiem_trong_dung_nhu_hop_dong(string severity, double expected)
    {
        using var h = await AiHarness.CreateAsync();

        var multiplier = await MultiplierAfterNewsAsync(
            h, NewsJson(severity, "bearish"), TradeDirection.Long);

        Assert.Equal((decimal)expected, multiplier);
    }

    [Fact]
    public async Task Boi_canh_cua_ma_khac_khong_anh_huong_lenh_cua_ma_nay()
    {
        using var h = await AiHarness.CreateAsync();

        var multiplier = await MultiplierAfterNewsAsync(
            h, NewsJson("critical", "bearish"), TradeDirection.Long, symbol: "ETHUSDT");

        Assert.Equal(1.0m, multiplier);
    }

    [Fact]
    public void Khong_co_boi_canh_nao_thi_he_so_bang_1()
    {
        var applier = new MarketContextApplier(new TestClock(TestClock.Default));

        Assert.Equal(1.0m, applier.GetSizeMultiplier(
            Array.Empty<MarketContextRecord>(), Symbol, TradeDirection.Long));
    }

    [Fact]
    public void Nhieu_boi_canh_cung_luc_thi_lay_muc_chat_nhat_chu_khong_nhan_don()
    {
        // Nhân dồn sẽ khiến ảnh hưởng của AI tỉ lệ với ĐỘ ỒN của nguồn tin: ba tin `high`
        // cho 0.125 thay vì 0.5. Một luồng RSS lắm lời sẽ lặng lẽ bóp chết việc vào lệnh.
        var clock = new TestClock(TestClock.Default);
        var applier = new MarketContextApplier(clock);

        var records = new[]
        {
            Record("high", MarketBias.Bearish, clock.UtcNow),
            Record("high", MarketBias.Bearish, clock.UtcNow),
            Record("medium", MarketBias.Bearish, clock.UtcNow),
        };

        Assert.Equal(0.5m, applier.GetSizeMultiplier(records, Symbol, TradeDirection.Long));
    }

    [Fact]
    public void Muc_nghiem_trong_la_chuoi_rac_thi_coi_nhu_khong_co_boi_canh()
    {
        var clock = new TestClock(TestClock.Default);
        var applier = new MarketContextApplier(clock);

        var records = new[] { Record("CATASTROPHIC", MarketBias.Bearish, clock.UtcNow) };

        Assert.Equal(1.0m, applier.GetSizeMultiplier(records, Symbol, TradeDirection.Long));
    }

    [Fact]
    public void Boi_canh_trung_lap_nhung_nghiem_trong_van_ap_cho_ca_hai_chieu()
    {
        // "Sàn lớn bị hack, chưa rõ giá chạy hướng nào" là tin nguy hiểm nhất trong ngày,
        // và nó không có chiều. Bỏ qua nó vì `leaning = neutral` là bỏ đúng lớp tin cần chặn.
        var clock = new TestClock(TestClock.Default);
        var applier = new MarketContextApplier(clock);

        var records = new[] { Record("critical", MarketBias.Neutral, clock.UtcNow) };

        Assert.Equal(0.0m, applier.GetSizeMultiplier(records, Symbol, TradeDirection.Long));
        Assert.Equal(0.0m, applier.GetSizeMultiplier(records, Symbol, TradeDirection.Short));
    }

    [Fact]
    public void He_so_luon_nam_trong_khoang_0_den_1()
    {
        var clock = new TestClock(TestClock.Default);
        var applier = new MarketContextApplier(clock);

        string[] severities = ["noise", "low", "medium", "high", "critical", "", "???"];
        MarketBias[] leanings = [MarketBias.Bearish, MarketBias.Neutral, MarketBias.Bullish];
        TradeDirection[] directions = [TradeDirection.Long, TradeDirection.Short];

        foreach (var s in severities)
        foreach (var l in leanings)
        foreach (var d in directions)
        {
            var value = applier.GetSizeMultiplier([Record(s, l, clock.UtcNow)], Symbol, d);
            Assert.InRange(value, 0m, 1m);
        }
    }

    private static MarketContextRecord Record(string severity, MarketBias leaning, DateTime nowUtc) =>
        new()
        {
            Kind = MarketContextKind.NewsItem,
            Severity = severity,
            Leaning = leaning,
            AffectedSymbols = Symbol,
            RecordedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.AddHours(2),
        };
}
