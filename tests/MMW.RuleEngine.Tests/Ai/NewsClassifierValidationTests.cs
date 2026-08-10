using MMW.Application.Ai;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.Ai;

/// <summary>
/// Bốn bước kiểm chứng phía nhận của News Classifier (contracts/ai-context.md).
/// </summary>
public class NewsClassifierValidationTests
{
    private static readonly string[] Watched = ["BTCUSDT", "ETHUSDT"];

    private static NewsClassifierValidator Validator() => new();

    private static EngineSetting Settings(int defaultTtl = 240) =>
        new() { AiContextDefaultTtlMinutes = defaultTtl };

    private static string News(
        string severity = "high",
        string symbols = "[\"BTCUSDT\"]",
        string leaning = "bearish",
        string halfLife = "120",
        string isRumor = "false") => $$"""
        {"severity":"{{severity}}","affectedSymbols":{{symbols}},"leaning":"{{leaning}}",
         "halfLifeMinutes":{{halfLife}},"isRumor":{{isRumor}}}
        """;

    // ── 1. Tin đồn ⟹ mức nghiêm trọng trần ở medium ─────────────────────────

    [Theory]
    [InlineData("critical", "medium")]
    [InlineData("high", "medium")]
    [InlineData("medium", "medium")]
    [InlineData("low", "low")]
    [InlineData("noise", "noise")]
    public void Tin_don_bi_ha_muc_nghiem_trong_ve_tran_medium(string given, string expected)
    {
        var result = Validator().Validate(News(severity: given, isRumor: "true"), Watched, Settings());

        Assert.True(result.Accepted);
        Assert.True(result.IsRumor);
        Assert.Equal(expected, result.Severity);
    }

    [Fact]
    public void Tin_da_xac_nhan_thi_giu_nguyen_muc_nghiem_trong()
    {
        var result = Validator().Validate(News(severity: "critical", isRumor: "false"), Watched, Settings());

        Assert.Equal("critical", result.Severity);
    }

    [Fact]
    public void Ha_cap_vi_tin_don_thi_duoc_ghi_vet()
    {
        var result = Validator().Validate(News(severity: "critical", isRumor: "true"), Watched, Settings());

        Assert.Contains(result.RejectedFields, f => f.Contains("severity", StringComparison.OrdinalIgnoreCase));
    }

    // ── 2. halfLifeMinutes cắt về [0, 1440] ─────────────────────────────────

    [Theory]
    [InlineData("0", 0)]
    [InlineData("120", 120)]
    [InlineData("1440", 1440)]
    [InlineData("99999", 1440)]
    [InlineData("-30", 0)]
    public void Chu_ky_ban_ra_bi_cat_ve_bien(string given, int expected)
    {
        var result = Validator().Validate(News(halfLife: given), Watched, Settings());

        Assert.Equal(expected, result.HalfLifeMinutes);
    }

    [Fact]
    public void Thieu_chu_ky_ban_ra_thi_dung_gia_tri_mac_dinh_cua_cau_hinh()
    {
        var raw = """
            {"severity":"high","affectedSymbols":["BTCUSDT"],"leaning":"bearish","isRumor":false}
            """;

        var result = Validator().Validate(raw, Watched, Settings(defaultTtl: 180));

        Assert.Equal(180, result.HalfLifeMinutes);
    }

    // ── 3. affectedSymbols lọc theo danh sách đang theo dõi ─────────────────

    [Fact]
    public void Ma_khong_theo_doi_bi_loai_khoi_danh_sach()
    {
        var result = Validator().Validate(
            News(symbols: "[\"BTCUSDT\",\"DOGEUSDT\",\"XRPUSDT\"]"), Watched, Settings());

        Assert.Equal(["BTCUSDT"], result.AffectedSymbols);
    }

    [Fact]
    public void Danh_sach_ma_rong_nghia_la_toan_thi_truong()
    {
        // Một tin vĩ mô không nhắm vào mã nào cụ thể vẫn phải áp cho mọi mã. Coi rỗng là
        // "không áp cho ai" sẽ làm đúng nhóm tin nguy hiểm nhất trở nên vô hại.
        var result = Validator().Validate(News(symbols: "[]"), Watched, Settings());

        Assert.True(result.Accepted);
        Assert.Empty(result.AffectedSymbols);
    }

    [Fact]
    public void Ma_duoc_chuan_hoa_ve_chu_hoa_truoc_khi_doi_chieu()
    {
        var result = Validator().Validate(News(symbols: "[\"btcusdt\"]"), Watched, Settings());

        Assert.Equal(["BTCUSDT"], result.AffectedSymbols);
    }

    // ── 4. Không rõ ràng ⟹ noise ────────────────────────────────────────────

    [Theory]
    [InlineData("CATASTROPHIC")]
    [InlineData("")]
    [InlineData("severe")]
    public void Muc_nghiem_trong_khong_thuoc_bang_thi_ve_noise(string given)
    {
        var result = Validator().Validate(News(severity: given), Watched, Settings());

        Assert.Equal("noise", result.Severity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("không đủ thông tin để phân loại")]
    [InlineData("{\"severity\": \"critical\",")]
    public void JSON_khong_hop_le_thi_ve_boi_canh_trung_tinh(string? raw)
    {
        var result = Validator().Validate(raw, Watched, Settings());

        Assert.False(result.Accepted);
        Assert.Equal("noise", result.Severity);
    }

    [Fact]
    public void Chieu_khong_thuoc_bang_thi_ve_trung_tinh()
    {
        var result = Validator().Validate(News(leaning: "sideways-ish"), Watched, Settings());

        Assert.Equal(MarketBias.Neutral, result.Leaning);
    }

    [Theory]
    [InlineData("bullish", MarketBias.Bullish)]
    [InlineData("bearish", MarketBias.Bearish)]
    [InlineData("neutral", MarketBias.Neutral)]
    public void Chieu_hop_le_duoc_doc_dung(string given, MarketBias expected)
    {
        Assert.Equal(expected, Validator().Validate(News(leaning: given), Watched, Settings()).Leaning);
    }

    // ── Mở rộng: khoá gợi ý lệnh cũng bị chặn ở đường tin ───────────────────

    [Fact]
    public void Khoa_goi_y_lenh_lam_hong_ca_phan_hoi_phan_loai_tin()
    {
        // Hợp đồng chỉ yêu cầu bước này cho Daily Brief, nhưng lý do thì giống hệt: một phản
        // hồi cố đưa ra tín hiệu giao dịch là dấu hiệu lời nhắc đã trôi khỏi vai trò, và phần
        // còn lại của phản hồi đó không đáng tin. Nguyên tắc III cho THÊM lớp chặn.
        var raw = """
            {"severity":"critical","affectedSymbols":["BTCUSDT"],"leaning":"bearish",
             "halfLifeMinutes":120,"isRumor":false,"action":"short ngay"}
            """;

        var result = Validator().Validate(raw, Watched, Settings());

        Assert.False(result.Accepted);
        Assert.Equal("noise", result.Severity);
        Assert.Contains(result.RejectedFields, f => f.Contains("action", StringComparison.OrdinalIgnoreCase));
    }
}
