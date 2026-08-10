using MMW.Application.Ai;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.Ai;

/// <summary>
/// Sáu bước kiểm chứng phía nhận của Daily Brief (contracts/ai-context.md).
/// </summary>
/// <remarks>
/// Kiểm chứng nằm ở PHÍA NHẬN, không nằm trong lời nhắc. Lời nhắc là một yêu cầu lịch sự;
/// bộ kiểm chứng là một hàng rào. Test dưới đây giả định lời nhắc đã bị phớt lờ hoàn toàn —
/// đó là giả định đúng cho một thành phần mà ta không kiểm soát được đầu ra.
/// </remarks>
public class DailyBriefValidationTests
{
    private static readonly DateTime Now = new(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc);

    private static DailyBriefValidator Validator() => new();

    private static EngineSetting Settings(int blackoutMaxMinutes = 120) =>
        new() { AiBlackoutMaxMinutes = blackoutMaxMinutes };

    private static ScheduledEvent[] Calendar(params DateTime[] occurrences) =>
        occurrences.Select((t, i) => new ScheduledEvent
        {
            Kind = ScheduledEventKind.Cpi,
            Title = "Sự kiện đã có trong lịch",
            OccursAtUtc = t,
            Impact = MacroEventImpact.High,
            Origin = ScheduledEventOrigin.Seeded,
            SourceKey = $"seed:{i}",
        }).ToArray();

    private static string Brief(string body) => $$"""
        {"dayRiskLevel":"normal","narrative":"Bình thường","confidence":0.5,{{body}}
         "themes":[],"symbolNotes":[]}
        """;

    // ── 1. confidence bị cắt về trần 0.8 ────────────────────────────────────

    [Theory]
    [InlineData(0.99, 0.8)]
    [InlineData(1.0, 0.8)]
    [InlineData(0.8, 0.8)]
    [InlineData(0.4, 0.4)]
    [InlineData(-3.0, 0.0)]
    public void Confidence_bi_cat_ve_tran_08(double given, double expected)
    {
        var raw = $$"""
            {"dayRiskLevel":"normal","narrative":"x","confidence":{{given}},
             "extraBlackouts":[],"themes":[],"symbolNotes":[]}
            """;

        var result = Validator().Validate(raw, [], Now, Settings());

        Assert.True(result.Accepted);
        Assert.Equal((decimal)expected, result.Confidence);
    }

    [Fact]
    public void Confidence_bi_cat_thi_duoc_ghi_vet()
    {
        var raw = """
            {"dayRiskLevel":"normal","narrative":"x","confidence":0.99,
             "extraBlackouts":[],"themes":[],"symbolNotes":[]}
            """;

        var result = Validator().Validate(raw, [], Now, Settings());

        Assert.Contains(result.RejectedFields, f => f.Contains("confidence", StringComparison.OrdinalIgnoreCase));
    }

    // ── 2. Cửa sổ trùng sự kiện đã có trong lịch thì loại ───────────────────

    [Fact]
    public void Cua_so_trum_len_su_kien_da_co_trong_lich_thi_bi_loai()
    {
        var known = Now.AddHours(3);
        var raw = Brief($$"""
            "extraBlackouts":[{"fromUtc":"{{known.AddMinutes(-30):o}}","toUtc":"{{known.AddMinutes(30):o}}",
              "reason":"CPI","severity":"high"}],
            """);

        var result = Validator().Validate(raw, Calendar(known), Now, Settings());

        Assert.Empty(result.ExtraBlackouts);
        Assert.Contains(result.RejectedFields, f => f.Contains("extraBlackouts", StringComparison.Ordinal));
    }

    [Fact]
    public void Cua_so_cho_tin_dot_xuat_khong_co_trong_lich_thi_duoc_giu()
    {
        var known = Now.AddHours(10);
        var shock = Now.AddHours(3);
        var raw = Brief($$"""
            "extraBlackouts":[{"fromUtc":"{{shock:o}}","toUtc":"{{shock.AddMinutes(45):o}}",
              "reason":"Sàn lớn dừng rút tiền","severity":"high"}],
            """);

        var result = Validator().Validate(raw, Calendar(known), Now, Settings());

        var window = Assert.Single(result.ExtraBlackouts);
        Assert.Equal(45, (int)(window.ToUtc - window.FromUtc).TotalMinutes);
    }

    // ── 3. Độ dài cửa sổ bị cắt về trần cấu hình ────────────────────────────

    [Fact]
    public void Cua_so_dai_hon_tran_thi_bi_cat_ve_tran()
    {
        var start = Now.AddHours(2);
        var raw = Brief($$"""
            "extraBlackouts":[{"fromUtc":"{{start:o}}","toUtc":"{{start.AddHours(20):o}}",
              "reason":"Sốc","severity":"high"}],
            """);

        var result = Validator().Validate(raw, [], Now, Settings(blackoutMaxMinutes: 90));

        var window = Assert.Single(result.ExtraBlackouts);
        Assert.Equal(start, window.FromUtc);
        Assert.Equal(start.AddMinutes(90), window.ToUtc);
        Assert.Contains(result.RejectedFields, f => f.Contains("toUtc", StringComparison.Ordinal));
    }

    // ── 4. from < to, và cả hai nằm trong 48 giờ tới ────────────────────────

    [Fact]
    public void Cua_so_dao_nguoc_thi_bi_loai()
    {
        var start = Now.AddHours(2);
        var raw = Brief($$"""
            "extraBlackouts":[{"fromUtc":"{{start:o}}","toUtc":"{{start.AddHours(-1):o}}",
              "reason":"Sốc","severity":"high"}],
            """);

        Assert.Empty(Validator().Validate(raw, [], Now, Settings()).ExtraBlackouts);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(60)]
    public void Cua_so_ngoai_pham_vi_48_gio_toi_thi_bi_loai(int hoursFromNow)
    {
        var start = Now.AddHours(hoursFromNow);
        var raw = Brief($$"""
            "extraBlackouts":[{"fromUtc":"{{start:o}}","toUtc":"{{start.AddMinutes(30):o}}",
              "reason":"Sốc","severity":"high"}],
            """);

        Assert.Empty(Validator().Validate(raw, [], Now, Settings()).ExtraBlackouts);
    }

    // ── 5. Khoá gợi ý lệnh ⟹ loại TOÀN BỘ phản hồi ──────────────────────────

    [Theory]
    [InlineData("entry")]
    [InlineData("stopLoss")]
    [InlineData("stop_loss")]
    [InlineData("takeProfit")]
    [InlineData("direction")]
    [InlineData("side")]
    [InlineData("action")]
    public void Bat_ky_khoa_goi_y_lenh_nao_cung_lam_hong_ca_phan_hoi(string key)
    {
        var raw = $$"""
            {"dayRiskLevel":"extreme","narrative":"x","confidence":0.5,
             "extraBlackouts":[],"themes":[],"symbolNotes":[],"{{key}}":"long"}
            """;

        var result = Validator().Validate(raw, [], Now, Settings());

        Assert.False(result.Accepted);
        Assert.Null(result.DayRiskLevel);
        Assert.Null(result.Narrative);
        Assert.Null(result.Confidence);
        Assert.Empty(result.ExtraBlackouts);
        Assert.Contains(result.RejectedFields, f => f.Contains(key, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Khoa_goi_y_lenh_nam_sau_trong_cay_JSON_van_bi_bat()
    {
        // Nếu chỉ soi khoá ở tầng ngoài cùng thì lách bằng cách lồng vào symbolNotes là xong.
        var raw = """
            {"dayRiskLevel":"normal","narrative":"x","confidence":0.5,"extraBlackouts":[],
             "themes":[],"symbolNotes":[{"symbol":"BTCUSDT","caution":"cẩn thận","entry":68000}]}
            """;

        Assert.False(Validator().Validate(raw, [], Now, Settings()).Accepted);
    }

    // ── 6. JSON không hợp lệ ⟹ bối cảnh trung tính ──────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("xin lỗi, tôi không thể trả lời câu hỏi này")]
    [InlineData("{\"dayRiskLevel\": \"extreme\", \"narrative\":")]
    public void JSON_khong_hop_le_thi_tra_boi_canh_trung_tinh(string? raw)
    {
        var result = Validator().Validate(raw, [], Now, Settings());

        Assert.False(result.Accepted);
        Assert.Null(result.DayRiskLevel);
        Assert.Empty(result.ExtraBlackouts);
        Assert.NotEmpty(result.RejectedFields);
    }

    [Fact]
    public void JSON_boc_trong_khoi_ma_van_doc_duoc_sau_mot_lan_sua()
    {
        var raw = """
            Đây là kết quả phân tích:
            ```json
            {"dayRiskLevel":"elevated","narrative":"Có tin","confidence":0.6,
             "extraBlackouts":[],"themes":[],"symbolNotes":[]}
            ```
            """;

        var result = Validator().Validate(raw, [], Now, Settings());

        Assert.True(result.Accepted);
        Assert.Equal("elevated", result.DayRiskLevel);
    }

    // ── Chuẩn hoá các trường còn lại ────────────────────────────────────────

    [Fact]
    public void Muc_rui_ro_ngay_khong_thuoc_bang_thi_bo_qua()
    {
        var raw = """
            {"dayRiskLevel":"APOCALYPTIC","narrative":"x","confidence":0.5,
             "extraBlackouts":[],"themes":[],"symbolNotes":[]}
            """;

        var result = Validator().Validate(raw, [], Now, Settings());

        Assert.True(result.Accepted);
        Assert.Null(result.DayRiskLevel);
    }

    [Fact]
    public void Ban_tuong_thuat_dai_qua_300_ky_tu_thi_bi_cat()
    {
        var raw = $$"""
            {"dayRiskLevel":"normal","narrative":"{{new string('a', 500)}}","confidence":0.5,
             "extraBlackouts":[],"themes":[],"symbolNotes":[]}
            """;

        var result = Validator().Validate(raw, [], Now, Settings());

        Assert.NotNull(result.Narrative);
        Assert.True(result.Narrative!.Length <= 300);
    }
}
