using MMW.Infrastructure.Exchanges.Binance;
using Xunit;

namespace MMW.RuleEngine.Tests.Execution;

/// <summary>
/// Ngắt mạch khi Binance cấm IP.
/// </summary>
/// <remarks>
/// Hồi quy cho một vòng lặp tự nuôi nó, quan sát trên VPS ngày 30/08/2026. Sàn trả <c>418</c>
/// kèm mốc hết cấm, mã cũ không đọc mốc đó, nên <c>TradeTrailingService</c> vẫn gọi lại mỗi 3
/// phút — và mỗi lần gọi lại đẩy mốc ra xa thêm (…515794 → …876311).
///
/// Điều đắt nhất không phải hạn mức API mà là thứ KHÔNG xảy ra trong lúc đó: việc kéo dừng lỗ
/// cho lệnh #57 hỏng suốt, nên vị thế nằm trần đúng lúc nhật ký nói nó đang được bảo vệ.
///
/// Mỗi test dựng một thực thể riêng thay vì dùng <c>BinanceIpBanTracker.Shared</c> — trạng thái
/// cấm là trạng thái tiến trình, và dùng chung sẽ làm kết quả phụ thuộc thứ tự chạy.
/// </remarks>
public sealed class BinanceIpBanTrackerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 30, 0, TimeSpan.Zero);

    /// <summary>Thông điệp thật của Binance, chép nguyên từ nhật ký.</summary>
    private static string BanBody(long untilUnixMs) =>
        $$"""{"code":-1003,"msg":"Way too many requests; IP(130.176.187.73) banned until {{untilUnixMs}}. Please use the websocket for live updates to avoid bans."}""";

    // ── Đọc mốc hết cấm ─────────────────────────────────────────────────

    [Fact]
    public void Doc_duoc_moc_het_cam_trong_thong_diep_that()
    {
        Assert.Equal(1788068515794L, BinanceIpBanTracker.ParseBannedUntil(BanBody(1788068515794L)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("""{"code":-1121,"msg":"Invalid symbol."}""")]
    [InlineData("banned until soon")]
    public void Thong_diep_khong_neu_moc_thi_tra_null(string? body)
    {
        Assert.Null(BinanceIpBanTracker.ParseBannedUntil(body));
    }

    // ── Chặn lời gọi trong thời gian cấm ────────────────────────────────

    [Fact]
    public void Sau_418_thi_bao_dang_bi_cam_toi_dung_moc_san_neu()
    {
        var tracker = new BinanceIpBanTracker();
        var until = Now.AddMinutes(5);

        tracker.Note(418, BanBody(until.ToUnixTimeMilliseconds()), Now);

        Assert.True(tracker.IsBanned(Now, out var remaining));
        Assert.Equal(TimeSpan.FromMinutes(5), remaining);
        Assert.Equal(until, tracker.BannedUntil);
    }

    [Fact]
    public void Qua_moc_het_cam_thi_goi_lai_binh_thuong()
    {
        var tracker = new BinanceIpBanTracker();
        tracker.Note(418, BanBody(Now.AddMinutes(5).ToUnixTimeMilliseconds()), Now);

        Assert.False(tracker.IsBanned(Now.AddMinutes(5).AddSeconds(1), out var remaining));
        Assert.Equal(TimeSpan.Zero, remaining);
    }

    /// <summary>
    /// Phản hồi cũ tới sau KHÔNG được rút ngắn lệnh cấm đang có.
    /// </summary>
    /// <remarks>
    /// Nhiều job cùng đụng tường trong vài giây, và phản hồi không nhất thiết về theo thứ tự.
    /// Phép gán thẳng sẽ để một mốc gần ghi đè mốc xa — nghĩa là ta lại gọi sớm, và lại bị nới
    /// dài. Chỉ nới ra, không rút ngắn.
    /// </remarks>
    [Fact]
    public void Moc_gan_hon_khong_duoc_rut_ngan_lenh_cam_dang_co()
    {
        var tracker = new BinanceIpBanTracker();
        var far = Now.AddMinutes(30);

        tracker.Note(418, BanBody(far.ToUnixTimeMilliseconds()), Now);
        tracker.Note(418, BanBody(Now.AddMinutes(2).ToUnixTimeMilliseconds()), Now);

        Assert.Equal(far, tracker.BannedUntil);
    }

    // ── Không nêu mốc thì lùi mặc định, và hai mã lùi khác nhau ──────────

    [Fact]
    public void Khong_neu_moc_thi_418_lui_lau_hon_429()
    {
        var banned = new BinanceIpBanTracker();
        var throttled = new BinanceIpBanTracker();

        banned.Note(418, """{"code":-1003,"msg":"Way too many requests."}""", Now);
        throttled.Note(429, """{"code":-1003,"msg":"Too many requests."}""", Now);

        Assert.Equal(Now.Add(BinanceIpBanTracker.DefaultBanBackoff), banned.BannedUntil);
        Assert.Equal(Now.Add(BinanceIpBanTracker.DefaultRateLimitBackoff), throttled.BannedUntil);
        Assert.True(banned.BannedUntil > throttled.BannedUntil);
    }

    // ── Lỗi khác không được biến thành lệnh cấm ─────────────────────────

    /// <remarks>
    /// Quan trọng không kém chiều ngược lại. Lỗi <c>-5022</c> (post-only bị từ chối) và
    /// <c>-1111</c> (sai precision) xảy ra thường xuyên trong sổ này; coi chúng là lệnh cấm sẽ
    /// làm engine tự khoá mình mười phút vì một lệnh đặt sai giá.
    /// </remarks>
    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(500)]
    public void Ma_loi_khac_khong_tao_lenh_cam(int statusCode)
    {
        var tracker = new BinanceIpBanTracker();

        tracker.Note(statusCode, """{"code":-5022,"msg":"Due to the order could not be executed as maker."}""", Now);

        Assert.False(tracker.IsBanned(Now, out _));
        Assert.Null(tracker.BannedUntil);
    }

    [Fact]
    public void Reset_xoa_lenh_cam()
    {
        var tracker = new BinanceIpBanTracker();
        tracker.Note(418, BanBody(Now.AddHours(1).ToUnixTimeMilliseconds()), Now);

        tracker.Reset();

        Assert.False(tracker.IsBanned(Now, out _));
    }
}
