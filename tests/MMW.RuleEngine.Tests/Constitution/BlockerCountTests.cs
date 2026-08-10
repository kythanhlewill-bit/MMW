using MMW.Application.MarketData;
using MMW.Application.Services;
using System.Text.Json;
using Xunit;

namespace MMW.RuleEngine.Tests.Constitution;

/// <summary>
/// Bộ gác Nguyên tắc III và SC-010: số lớp chặn trước khi gửi lệnh thật CHỈ ĐƯỢC TĂNG.
/// </summary>
/// <remarks>
/// Nguyên tắc III là điều khoản không thương lượng của hiến chương. Nhưng "đừng bớt lớp chặn"
/// là một lời dặn, mà lời dặn thì không chặn được ai — nhất là khi có người đang gỡ một lớp
/// chặn "tạm thời" để thử một thứ gì đó và quên trả lại.
///
/// Test này biến lời dặn thành con số. Gỡ một lớp chặn thì test đỏ, và người gỡ phải sửa
/// con số mốc một cách CÓ Ý THỨC — kèm theo lý do trong commit.
/// </remarks>
public class BlockerCountTests
{
    /// <summary>
    /// Số lời gọi <c>BlockAsync</c> trong luồng đặt lệnh thật, chốt tại thời điểm bắt đầu
    /// Deterministic Intraday Trading Engine (2026-08-02, trước Phase 3).
    ///
    /// Con số này CHỈ ĐƯỢC TĂNG. Giảm nó là vi phạm Nguyên tắc III và SC-010.
    /// </summary>
    private const int BaselineBlockerCount = 14;

    private static int CountBlockCalls()
    {
        var calls = IlScanner.ScanCalls(
            typeof(LiveOrderService).Assembly,
            ns => ns == "MMW.Application.Services");

        return calls.Count(c =>
            c.CallerType == nameof(LiveOrderService)
            && c.TargetMember == "BlockAsync");
    }

    [Fact]
    public void So_lop_chan_khong_duoc_giam_so_voi_moc_baseline()
    {
        var actual = CountBlockCalls();

        Assert.True(actual >= BaselineBlockerCount,
            $"Luồng đặt lệnh thật hiện có {actual} lớp chặn, ít hơn mốc {BaselineBlockerCount}. " +
            "Nguyên tắc III chỉ cho phép THÊM lớp chặn, không cho bớt. Nếu việc giảm là có chủ ý " +
            "và đã được cân nhắc, hãy sửa BaselineBlockerCount kèm lý do trong commit.");
    }

    [Fact]
    public void Bo_dem_thuc_su_dem_duoc_chu_khong_xanh_vi_dem_ra_khong()
    {
        // Bộ đếm trả 0 sẽ luôn thoả "≥ 0" nếu ai đó lỡ hạ mốc — và một bộ gác luôn xanh
        // thì vô dụng. Khẳng định riêng rằng nó thực sự nhìn thấy luồng đặt lệnh.
        Assert.True(CountBlockCalls() > 0,
            "Không đếm được lời gọi BlockAsync nào trong LiveOrderService — " +
            "bộ quét IL đã lệch khỏi cấu trúc mã (đổi tên lớp, đổi tên phương thức, hoặc đổi namespace).");
    }

    [Fact]
    public void Giao_dich_that_van_mac_dinh_TAT()
    {
        // SC-014. Đọc thẳng giá trị mặc định của lớp cấu hình, không đọc appsettings —
        // mặc định an toàn phải nằm trong MÃ, để một tệp cấu hình thiếu không mở khoá gì cả.
        var options = new LiveTradingOptions();

        Assert.False(options.Enabled);
        Assert.True(options.UseTestnet);
    }

    [Fact]
    public void Appsettings_cung_giu_live_TAT_va_testnet_BAT()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MMW.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var path = Path.Combine(directory!.FullName, "src", "MMW.Web", "appsettings.json");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var live = json.RootElement.GetProperty("LiveTrading");
        var bootstrap = json.RootElement.GetProperty("BootstrapAdmin");

        Assert.False(live.GetProperty("Enabled").GetBoolean());
        Assert.True(live.GetProperty("UseTestnet").GetBoolean());
        Assert.Equal(string.Empty, bootstrap.GetProperty("Password").GetString());
    }
}
