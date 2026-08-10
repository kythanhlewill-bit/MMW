using MMW.Application.Trading.TimeGuard;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.TimeGuard;

/// <summary>
/// T047 — sự kiện sinh bằng công thức. Đây là lớp bảo vệ KHÔNG phụ thuộc dữ liệu nạp tay:
/// dù cuốn lịch CPI/FOMC có quá hạn hay rỗng, các mốc này vẫn có (ràng buộc 2 của contract).
/// </summary>
/// <remarks>
/// Toàn bộ test ở đây gọi một hàm thuần, không đồng hồ, không cơ sở dữ liệu — nên chúng
/// là bảng đầu vào/đầu ra thuần tuý và chạy được ở bất kỳ đâu, bất kỳ lúc nào.
/// </remarks>
public class DerivedEventGeneratorTests
{
    private const string Symbol = "BTCUSDT";
    private static readonly IDerivedEventGenerator Generator = new DerivedEventGenerator();

    private static DateTime Utc(int y, int m, int d, int h = 0, int min = 0) =>
        new(y, m, d, h, min, 0, DateTimeKind.Utc);

    private static IReadOnlyList<ScheduledEvent> Generate(DateTime fromUtc, DateTime toUtc) =>
        Generator.Generate(fromUtc, toUtc, Symbol);

    private static List<ScheduledEvent> OfKind(IReadOnlyList<ScheduledEvent> events, ScheduledEventKind kind) =>
        events.Where(e => e.Kind == kind).OrderBy(e => e.OccursAtUtc).ToList();

    // ── Thanh toán phí vốn ──────────────────────────────────────────────

    [Fact]
    public void Moi_ngay_co_dung_ba_moc_thanh_toan_phi_von()
    {
        var events = Generate(Utc(2026, 8, 3), Utc(2026, 8, 4));
        var funding = OfKind(events, ScheduledEventKind.FundingSettlement);

        Assert.Equal(
            new[] { Utc(2026, 8, 3, 0), Utc(2026, 8, 3, 8), Utc(2026, 8, 3, 16) },
            funding.Select(e => e.OccursAtUtc));
    }

    [Fact]
    public void Ngay_29_thang_2_nam_nhuan_van_co_du_ba_moc_phi_von()
    {
        // 2028-02-29 tồn tại. Một phép cộng ngày sai sẽ nhảy cóc qua ngày này trong im lặng.
        var funding = OfKind(Generate(Utc(2028, 2, 29), Utc(2028, 3, 1)), ScheduledEventKind.FundingSettlement);

        Assert.Equal(3, funding.Count);
        Assert.All(funding, e => Assert.Equal(29, e.OccursAtUtc.Day));
    }

    [Theory]
    [InlineData(2027, 1095)]   // 365 ngày × 3
    [InlineData(2028, 1098)]   // 366 ngày × 3 — năm nhuận
    public void Ca_nam_co_du_ba_moc_moi_ngay_ke_ca_nam_nhuan(int year, int expected)
    {
        var funding = OfKind(Generate(Utc(year, 1, 1), Utc(year + 1, 1, 1)), ScheduledEventKind.FundingSettlement);

        Assert.Equal(expected, funding.Count);
    }

    // ── Đáo hạn quyền chọn ──────────────────────────────────────────────

    [Fact]
    public void Thu_Sau_khong_phai_cuoi_thang_la_dao_han_TUAN()
    {
        // 2026-08-07 là thứ Sáu ĐẦU tiên của tháng 8/2026.
        var expiry = OfKind(Generate(Utc(2026, 8, 7), Utc(2026, 8, 8)), ScheduledEventKind.OptionsExpiry);

        var e = Assert.Single(expiry);
        Assert.Equal(Utc(2026, 8, 7, 8), e.OccursAtUtc);
        Assert.Equal(MacroEventImpact.Medium, e.Impact);
        Assert.Contains("tuần", e.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Thu_Sau_cuoi_cung_cua_thang_la_dao_han_THANG()
    {
        // 2026-08-28 là thứ Sáu CUỐI CÙNG của tháng 8/2026.
        var expiry = OfKind(Generate(Utc(2026, 8, 28), Utc(2026, 8, 29)), ScheduledEventKind.OptionsExpiry);

        var e = Assert.Single(expiry);
        Assert.Equal(Utc(2026, 8, 28, 8), e.OccursAtUtc);
        Assert.Equal(MacroEventImpact.High, e.Impact);
        Assert.Contains("tháng", e.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Thu_Sau_cuoi_thang_chi_sinh_MOT_su_kien_chu_khong_phai_hai()
    {
        // Cái bẫy rõ nhất: sinh "đáo hạn tuần" cho mọi thứ Sáu rồi sinh thêm "đáo hạn tháng"
        // cho thứ Sáu cuối — thành hai sự kiện trùng giờ, và cửa sổ chặn bị đếm đôi.
        var expiry = OfKind(Generate(Utc(2026, 8, 1), Utc(2026, 9, 1)), ScheduledEventKind.OptionsExpiry);

        Assert.Equal(4, expiry.Count);                                   // tháng 8/2026 có đúng 4 thứ Sáu
        Assert.Single(expiry, e => e.Impact == MacroEventImpact.High);   // đúng một cái là đáo hạn tháng
    }

    [Fact]
    public void Thang_co_nam_thu_Sau_thi_chi_thu_Sau_thu_nam_la_dao_han_thang()
    {
        // Tháng 1/2027 có 5 thứ Sáu: 01, 08, 15, 22, 29.
        var expiry = OfKind(Generate(Utc(2027, 1, 1), Utc(2027, 2, 1)), ScheduledEventKind.OptionsExpiry);

        Assert.Equal(5, expiry.Count);
        var monthly = Assert.Single(expiry, e => e.Impact == MacroEventImpact.High);
        Assert.Equal(Utc(2027, 1, 29, 8), monthly.OccursAtUtc);
    }

    [Fact]
    public void Thu_Sau_cuoi_thang_cua_thang_hai_nam_nhuan_la_ngay_25_khong_phai_ngay_29()
    {
        // 2028-02-29 là thứ Ba. "Ngày cuối tháng lùi về thứ Sáu gần nhất" sẽ ra sai;
        // đúng phải là thứ Sáu cuối cùng = 2028-02-25.
        var expiry = OfKind(Generate(Utc(2028, 2, 1), Utc(2028, 3, 1)), ScheduledEventKind.OptionsExpiry);

        var monthly = Assert.Single(expiry, e => e.Impact == MacroEventImpact.High);
        Assert.Equal(Utc(2028, 2, 25, 8), monthly.OccursAtUtc);
    }

    // ── Khoảng trống cuối tuần ──────────────────────────────────────────

    [Fact]
    public void Chu_nhat_sinh_khoang_trong_cuoi_tuan_dai_120_phut()
    {
        // 2026-08-02 là Chủ nhật.
        var gap = OfKind(Generate(Utc(2026, 8, 2), Utc(2026, 8, 3)), ScheduledEventKind.WeekendGap);

        var e = Assert.Single(gap);
        Assert.Equal(Utc(2026, 8, 2, 21), e.OccursAtUtc);
        Assert.Equal(120, e.DurationMinutes);
    }

    [Fact]
    public void Ngay_khong_phai_Chu_nhat_thi_khong_co_khoang_trong_cuoi_tuan()
    {
        // 2026-08-03 là thứ Hai.
        Assert.Empty(OfKind(Generate(Utc(2026, 8, 3), Utc(2026, 8, 4)), ScheduledEventKind.WeekendGap));
    }

    // ── Biên khó ────────────────────────────────────────────────────────

    [Fact]
    public void Tuan_bac_cau_giao_thua_van_lien_mach_va_phan_loai_dung()
    {
        // 2026-12-28 (thứ Hai) → 2027-01-05. Thứ Sáu duy nhất trong khoảng là 2027-01-01,
        // vốn là thứ Sáu ĐẦU của tháng 1/2027 — không phải đáo hạn tháng, dù nó nằm sát
        // ngay sau thứ Sáu cuối của tháng 12/2026 (2026-12-25, ngoài khoảng).
        var events = Generate(Utc(2026, 12, 28), Utc(2027, 1, 5));

        Assert.Equal(8 * 3, OfKind(events, ScheduledEventKind.FundingSettlement).Count);

        var expiry = Assert.Single(OfKind(events, ScheduledEventKind.OptionsExpiry));
        Assert.Equal(Utc(2027, 1, 1, 8), expiry.OccursAtUtc);
        Assert.Equal(MacroEventImpact.Medium, expiry.Impact);

        var gap = Assert.Single(OfKind(events, ScheduledEventKind.WeekendGap));
        Assert.Equal(Utc(2027, 1, 3, 21), gap.OccursAtUtc);   // Chủ nhật 2027-01-03
    }

    [Fact]
    public void Khoang_thoi_gian_la_nua_mo_lay_bien_duoi_bo_bien_tren()
    {
        // [from, to): mốc 08:00 nằm đúng biên dưới thì LẤY, mốc 16:00 nằm đúng biên trên thì BỎ.
        var funding = OfKind(
            Generate(Utc(2026, 8, 3, 8), Utc(2026, 8, 3, 16)),
            ScheduledEventKind.FundingSettlement);

        var e = Assert.Single(funding);
        Assert.Equal(Utc(2026, 8, 3, 8), e.OccursAtUtc);
    }

    [Theory]
    [InlineData(0)]      // from == to
    [InlineData(-1)]     // from > to
    public void Khoang_rong_hoac_dao_nguoc_tra_ve_danh_sach_rong(int hoursAfter)
    {
        var from = Utc(2026, 8, 3);
        Assert.Empty(Generate(from, from.AddHours(hoursAfter)));
    }

    // ── Tính chất của hàm thuần ─────────────────────────────────────────

    [Fact]
    public void Goi_hai_lan_cung_dau_vao_cho_ket_qua_giong_het()
    {
        var a = Generate(Utc(2026, 8, 1), Utc(2026, 9, 1));
        var b = Generate(Utc(2026, 8, 1), Utc(2026, 9, 1));

        Assert.Equal(
            a.Select(e => (e.Kind, e.OccursAtUtc, e.SourceKey)),
            b.Select(e => (e.Kind, e.OccursAtUtc, e.SourceKey)));
    }

    [Fact]
    public void Moi_su_kien_deu_la_Derived_gio_UTC_va_co_SourceKey_duy_nhat()
    {
        var events = Generate(Utc(2026, 8, 1), Utc(2026, 9, 1));

        Assert.NotEmpty(events);
        Assert.All(events, e =>
        {
            Assert.Equal(ScheduledEventOrigin.Derived, e.Origin);
            Assert.Equal(DateTimeKind.Utc, e.OccursAtUtc.Kind);
            Assert.False(string.IsNullOrWhiteSpace(e.SourceKey));
        });

        // Trùng SourceKey nghĩa là hai cửa sổ chặn đè lên nhau khi nhập kho — và
        // ImportAsync bất biến theo SourceKey sẽ âm thầm nuốt mất một cái.
        Assert.Equal(events.Count, events.Select(e => e.SourceKey).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Gio_dia_phuong_bi_tu_choi_thay_vi_dien_giai_nham_thanh_UTC()
    {
        // Nhận bừa DateTimeKind.Local rồi coi như UTC là cách êm ái nhất để lệch 7 tiếng
        // trên máy Việt Nam mà không test nào đỏ.
        var local = new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Local);

        Assert.Throws<ArgumentException>(() => Generator.Generate(local, local.AddDays(1), Symbol));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Thieu_ma_giao_dich_thi_bao_loi(string? symbol)
    {
        Assert.Throws<ArgumentException>(() => Generator.Generate(Utc(2026, 8, 3), Utc(2026, 8, 4), symbol!));
    }
}
