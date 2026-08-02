using MMW.Application.Trading.Structure;
using Xunit;

namespace MMW.RuleEngine.Tests.Constitution;

/// <summary>
/// Bộ gác Nguyên tắc II: tầng quyết định KHÔNG được có nguồn bất định nào ngoài hai cổng
/// <c>IClock</c> và <c>IMarketDataProvider</c>.
/// </summary>
/// <remarks>
/// Không có test này thì tính tất định là một quy ước phải nhớ, và quy ước phải nhớ thì
/// sẽ bị quên — thường là vào lúc sửa gấp một lỗi lúc 11 giờ đêm. Một lệnh
/// <c>DateTime.UtcNow</c> lọt vào đây làm kiểm thử lịch sử ngừng tái lập được chạy thật,
/// nhưng KHÔNG làm bất cứ test nào khác đỏ — nó chỉ âm thầm làm mọi con số backtest sai.
///
/// Vì vậy đây là test đỏ trước cho một tính chất, không phải cho một hàm.
/// </remarks>
public class DeterminismGuardTests
{
    /// <summary>Các namespace bị gác. Mọi thứ bên trong phải thuần và tất định.</summary>
    private static bool IsGuarded(string ns) =>
        ns.StartsWith("MMW.Application.Trading", StringComparison.Ordinal)
        || ns.StartsWith("MMW.Application.Backtest", StringComparison.Ordinal);

    /// <summary>
    /// (Kiểu, thành viên) bị cấm. Property tĩnh biên dịch thành lời gọi <c>get_X</c>.
    /// </summary>
    private static readonly (string Type, string Member)[] Forbidden =
    {
        ("System.DateTime", "get_Now"),
        ("System.DateTime", "get_UtcNow"),
        ("System.DateTime", "get_Today"),
        ("System.DateTimeOffset", "get_Now"),
        ("System.DateTimeOffset", "get_UtcNow"),
        ("System.Random", ".ctor"),
        ("System.Random", "get_Shared"),

        // Ngoài đặc tả nhưng cùng bản chất: một GUID mới mỗi lần chạy cũng phá tính tái lập.
        // Nguyên tắc III cho phép THÊM lớp chặn, không cho bớt.
        ("System.Guid", "NewGuid"),
    };

    [Fact]
    public void Tang_quyet_dinh_khong_duoc_cham_dong_ho_he_thong_hay_so_ngau_nhien()
    {
        var assembly = typeof(SwingDetector).Assembly;
        var calls = IlScanner.ScanCalls(assembly, IsGuarded);

        var violations = calls
            .Where(c => Forbidden.Any(f => f.Type == c.TargetType && f.Member == c.TargetMember))
            .Select(c => c.ToString())
            .Distinct()
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(violations.Count == 0,
            "Tầng quyết định phải lấy thời gian qua IClock và không được dùng số ngẫu nhiên. " +
            $"Vi phạm tìm thấy:{Environment.NewLine}  {string.Join($"{Environment.NewLine}  ", violations)}");
    }

    [Fact]
    public void Bo_gac_thuc_su_co_quet_duoc_thu_gi_do()
    {
        // Một bộ gác xanh vì không quét gì còn tệ hơn không có bộ gác: nó tạo cảm giác
        // an toàn giả. Nếu ai đó đổi tên namespace, test này đỏ trước.
        var assembly = typeof(SwingDetector).Assembly;

        Assert.True(IlScanner.CountTypes(assembly, IsGuarded) > 0,
            "Không tìm thấy lớp nào trong MMW.Application.Trading / MMW.Application.Backtest — " +
            "bộ lọc namespace đã lệch khỏi cấu trúc mã.");

        Assert.NotEmpty(IlScanner.ScanCalls(assembly, IsGuarded));
    }

    [Fact]
    public void Bo_quet_IL_phat_hien_duoc_vi_pham_that()
    {
        // Kiểm chứng chính bộ quét: nó phải tìm thấy DateTime.UtcNow trong lớp mồi dưới đây.
        // Không có test này thì test chính ở trên có thể xanh chỉ vì bộ quét bị hỏng.
        var calls = IlScanner.ScanCalls(
            typeof(DeterminismGuardTests).Assembly,
            ns => ns == "MMW.RuleEngine.Tests.Constitution");

        Assert.Contains(calls, c => c.TargetType == "System.DateTime" && c.TargetMember == "get_UtcNow");
        Assert.Contains(calls, c => c.TargetType == "System.Random" && c.TargetMember == ".ctor");
    }
}

/// <summary>
/// Lớp mồi cho <see cref="DeterminismGuardTests.Bo_quet_IL_phat_hien_duoc_vi_pham_that"/>.
/// Cố tình vi phạm để chứng minh bộ quét IL thực sự bắt được. KHÔNG dùng vào việc gì khác.
/// </summary>
internal static class DeliberateDeterminismViolation
{
    public static DateTime Now() => DateTime.UtcNow;

    public static int Roll() => new Random().Next();

    /// <summary>Phương thức async — kiểm chứng bộ quét nhìn được vào máy trạng thái.</summary>
    public static async Task<DateTime> NowAsync()
    {
        await Task.Yield();
        return DateTime.UtcNow;
    }
}
