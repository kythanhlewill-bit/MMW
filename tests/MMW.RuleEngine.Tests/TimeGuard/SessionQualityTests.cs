using Microsoft.Extensions.DependencyInjection;
using MMW.Application.Trading.TimeGuard;
using MMW.Domain.Entities;
using MMW.Domain.Enums;
using Xunit;

namespace MMW.RuleEngine.Tests.TimeGuard;

/// <summary>
/// T051 / FR-030, FR-031 — bảng phiên chuẩn khi chưa đủ dữ liệu, thống kê thật khi đã đủ.
/// </summary>
/// <remarks>
/// Điểm tinh tế nằm ở chỗ chuyển giao. Chia thẳng "số lệnh thắng trên tổng số lệnh trong khung
/// giờ" sẽ cho một khung giờ có đúng một lệnh thua điểm 0 vĩnh viễn — cấm cửa khung giờ đó dựa
/// trên một mẫu duy nhất. Nên điểm cá nhân được KÉO VỀ bảng chuẩn theo cỡ mẫu: mẫu càng lớn thì
/// càng tin số thật, mẫu càng nhỏ thì càng giữ giá trị chuẩn.
///
/// Hệ số kéo về nằm ở <c>EngineSetting.SessionStatsSmoothingTrades</c> chứ không viết cứng —
/// Nguyên tắc I.
/// </remarks>
public class SessionQualityTests
{
    private const int AsianHour = 3;        // khoảng 0–7,  điểm chuẩn 4, nhãn "Phiên Á"
    private const int OverlapHour = 14;     // khoảng 13–16, điểm chuẩn 2, nhãn "Chồng lấn New York"
    private const int NightHour = 22;       // khoảng 21–24, điểm chuẩn 5, nhãn "Đêm mỏng"

    private static DateTime AtHour(int hour) => new(2026, 8, 5, hour, 30, 0, DateTimeKind.Utc);

    private static IEnumerable<Trade> ClosedTrades(long accountId, int hour, int wins, int losses)
    {
        var openedAt = AtHour(hour);
        for (var i = 0; i < wins + losses; i++)
        {
            yield return new Trade
            {
                TradingAccountId = accountId,
                Symbol = "BTCUSDT",
                Direction = TradeDirection.Long,
                Status = TradeStatus.Closed,
                EntryPrice = 100m,
                Quantity = 1m,
                OpenedAt = openedAt.AddDays(-i),
                ClosedAt = openedAt.AddDays(-i).AddHours(2),
                Outcome = i < wins ? TradeOutcome.Win : TradeOutcome.Loss,
            };
        }
    }

    private static async Task<SessionQuality> GetAsync(TimeGuardHarness harness, int hour)
    {
        using var scope = harness.NewScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISessionQualityProvider>();
        return await provider.GetAsync(harness.AccountId, AtHour(hour));
    }

    // ── Chưa đủ dữ liệu: dùng bảng chuẩn ────────────────────────────────

    [Fact]
    public async Task Tai_khoan_moi_dung_bang_chuan()
    {
        using var harness = await TimeGuardHarness.CreateAsync();

        var quality = await GetAsync(harness, OverlapHour);

        Assert.Equal(2, quality.Score);
        Assert.Equal("Chồng lấn New York", quality.Label);
        Assert.False(quality.IsPersonalised);
    }

    /// <remarks>
    /// Điểm ở đây KHÔNG theo giờ vàng của thị trường mà theo net R đo được trên 2.900 phiếu của
    /// chính tài khoản này — xem <c>EngineSettingDefaults.SessionQualityRows</c>. Hai khung từng
    /// được chấm cao nhất (London 5, chồng lấn NY 6) hoá ra là hai khung lỗ nặng nhất, còn "đêm
    /// mỏng" từng bị chấm 1 lại là khung tốt nhất. Test này khoá đúng chiều đã đảo.
    /// </remarks>
    [Theory]
    [InlineData(3, 4, "Phiên Á")]
    [InlineData(8, 5, "Mở cửa London")]
    [InlineData(10, 1, "London")]
    [InlineData(14, 2, "Chồng lấn New York")]
    [InlineData(18, 4, "New York chiều")]
    [InlineData(22, 5, "Đêm mỏng")]
    public async Task Bang_chuan_phu_kin_moi_khung_gio(int hour, int expectedScore, string expectedLabel)
    {
        using var harness = await TimeGuardHarness.CreateAsync();

        var quality = await GetAsync(harness, hour);

        Assert.Equal(expectedScore, quality.Score);
        Assert.Equal(expectedLabel, quality.Label);
    }

    [Fact]
    public async Task Duoi_nguong_mot_lenh_van_chua_ca_nhan_hoa()
    {
        // 49 lệnh đóng, ngưỡng 50. Đúng một lệnh nữa mới đổi chế độ.
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddClosedTradesAsync(ClosedTrades(harness.AccountId, AsianHour, wins: 0, losses: 49));

        var quality = await GetAsync(harness, AsianHour);

        Assert.False(quality.IsPersonalised);
        Assert.Equal(4, quality.Score);   // vẫn là điểm chuẩn của phiên Á
    }

    // ── Đủ dữ liệu: chuyển sang thống kê thật ───────────────────────────

    [Fact]
    public async Task Dat_dung_nguong_thi_ca_nhan_hoa()
    {
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddClosedTradesAsync(ClosedTrades(harness.AccountId, AsianHour, wins: 50, losses: 0));

        var quality = await GetAsync(harness, AsianHour);

        Assert.True(quality.IsPersonalised);
        Assert.Equal(50, quality.SampleSize);
    }

    [Theory]
    [InlineData(50, 0, 6)]    // (50×1,0×6 + 10×4) / 60 = 5,67 → 6
    [InlineData(0, 50, 1)]    // (50×0,0×6 + 10×4) / 60 = 0,67 → 1
    [InlineData(25, 25, 3)]   // (50×0,5×6 + 10×4) / 60 = 3,17 → 3
    public async Task Diem_ca_nhan_duoc_keo_ve_bang_chuan_theo_co_mau(int wins, int losses, int expected)
    {
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddClosedTradesAsync(ClosedTrades(harness.AccountId, AsianHour, wins, losses));

        var quality = await GetAsync(harness, AsianHour);

        Assert.Equal(expected, quality.Score);
    }

    [Fact]
    public async Task Mot_lenh_thua_don_le_khong_xoa_so_mot_khung_gio()
    {
        // 49 lệnh thắng ở phiên Á + 1 lệnh thua ở khung đêm = 50 lệnh, đã cá nhân hoá.
        // Khung đêm chỉ có đúng 1 mẫu và mẫu đó thua: (1×0×6 + 10×5) / 11 = 4,55 → 5.
        // Chia thẳng sẽ ra 0 và cấm cửa khung giờ tốt nhất trong ngày vì một lệnh.
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddClosedTradesAsync(ClosedTrades(harness.AccountId, AsianHour, wins: 49, losses: 0));
        await harness.AddClosedTradesAsync(ClosedTrades(harness.AccountId, NightHour, wins: 0, losses: 1));

        var quality = await GetAsync(harness, NightHour);

        Assert.True(quality.IsPersonalised);
        Assert.Equal(1, quality.SampleSize);
        Assert.Equal(5, quality.Score);
    }

    [Fact]
    public async Task Khung_gio_chua_co_lenh_nao_thi_giu_diem_chuan_va_bao_la_chua_ca_nhan_hoa()
    {
        // Tài khoản đã đủ 50 lệnh nhưng toàn ở phiên Á. Hỏi về khung chồng lấn thì không có
        // gì để cá nhân hoá — phải nói thẳng là chưa, thay vì trả điểm chuẩn kèm nhãn "đã cá nhân hoá".
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddClosedTradesAsync(ClosedTrades(harness.AccountId, AsianHour, wins: 30, losses: 20));

        var quality = await GetAsync(harness, OverlapHour);

        Assert.False(quality.IsPersonalised);
        Assert.Equal(0, quality.SampleSize);
        Assert.Equal(2, quality.Score);
    }

    // ── Lệnh nào được đếm ───────────────────────────────────────────────

    [Fact]
    public async Task Lenh_chua_dong_khong_duoc_tinh()
    {
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddClosedTradesAsync(ClosedTrades(harness.AccountId, AsianHour, wins: 49, losses: 0));
        await harness.AddClosedTradesAsync(new[]
        {
            new Trade
            {
                TradingAccountId = harness.AccountId,
                Symbol = "BTCUSDT", Direction = TradeDirection.Long,
                Status = TradeStatus.Open, EntryPrice = 100m, Quantity = 1m,
                OpenedAt = AtHour(AsianHour),
            },
        });

        Assert.False((await GetAsync(harness, AsianHour)).IsPersonalised);
    }

    [Fact]
    public async Task Lenh_hoa_von_khong_tinh_vao_ty_le_thang()
    {
        // 50 lệnh thắng + 50 lệnh hoà vốn ở cùng khung giờ. Hoà vốn không phải thắng cũng
        // không phải thua; đếm nó là thua sẽ kéo tỷ lệ thắng xuống một nửa một cách vô cớ.
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddClosedTradesAsync(ClosedTrades(harness.AccountId, AsianHour, wins: 50, losses: 0));
        await harness.AddClosedTradesAsync(
            ClosedTrades(harness.AccountId, AsianHour, wins: 0, losses: 50)
                .Select(t => { t.Outcome = TradeOutcome.BreakEven; return t; })
                .ToList());

        var quality = await GetAsync(harness, AsianHour);

        Assert.Equal(50, quality.SampleSize);
        Assert.Equal(6, quality.Score);   // (50×1,0×6 + 10×4) / 60 = 5,67 → 6
    }

    [Fact]
    public async Task Lenh_cua_tai_khoan_khac_khong_duoc_tinh()
    {
        using var harness = await TimeGuardHarness.CreateAsync();
        await harness.AddClosedTradesAsync(ClosedTrades(harness.AccountId + 999, AsianHour, wins: 50, losses: 0));

        Assert.False((await GetAsync(harness, AsianHour)).IsPersonalised);
    }

    // ── Cấu hình hỏng phải nổ, không được trả 0 điểm ────────────────────

    [Fact]
    public async Task Bang_phien_thung_lo_thi_bao_loi_chu_khong_am_tham_tra_0_diem()
    {
        // "Thiếu dữ liệu ⟹ 0 điểm" là cách hỏng tệ nhất: hệ thống vẫn chạy, chỉ là mỗi ngày
        // đúng khung giờ đó lại mất 6 điểm mà không ai biết vì sao.
        using var harness = await TimeGuardHarness.CreateAsync(setting =>
        {
            var hole = setting.SessionQualityRows.First(r => r.FromHourUtc == 13);
            setting.SessionQualityRows.Remove(hole);
        });

        using var scope = harness.NewScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISessionQualityProvider>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetAsync(harness.AccountId, AtHour(OverlapHour)));
    }

    [Fact]
    public async Task Tai_khoan_khong_co_cau_hinh_engine_thi_bao_loi()
    {
        using var harness = await TimeGuardHarness.CreateAsync();

        using var scope = harness.NewScope();
        var provider = scope.ServiceProvider.GetRequiredService<ISessionQualityProvider>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetAsync(harness.AccountId + 999, AtHour(OverlapHour)));
    }
}
