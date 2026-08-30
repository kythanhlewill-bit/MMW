using System.Globalization;

namespace MMW.Infrastructure.Exchanges.Binance;

/// <summary>
/// Theo dõi lệnh cấm IP của Binance để các lời gọi sau tự dừng lại thay vì gõ cửa tiếp.
/// </summary>
/// <remarks>
/// Vì sao phải có: khi trả <c>418</c> kèm mã <c>-1003</c>, Binance nói rõ thời điểm hết cấm.
/// Không đọc con số đó thì mọi job định kỳ vẫn gọi tiếp đúng nhịp cũ, và mỗi lần gọi trong lúc
/// đang bị cấm lại NỚI DÀI lệnh cấm — một vòng lặp tự nuôi nó.
///
/// Nhật ký VPS ngày 30/08/2026 cho thấy đúng vòng đó: <c>TradeTrailingService</c> đập vào tường
/// mỗi 3 phút cho lệnh #57, và mốc hết cấm nhích lên sau mỗi lần (…515794 → …876311). Cái giá
/// không phải vài dòng log — trong suốt thời gian ấy việc kéo dừng lỗ không chạy được, nên vị
/// thế nằm trần đúng lúc ta tưởng nó đang được bảo vệ.
///
/// Lệnh cấm gắn theo ĐỊA CHỈ IP chứ không theo API key, nên bản dùng chung
/// (<see cref="Shared"/>) là một thực thể duy nhất cho cả tiến trình. Lớp này vẫn tạo được
/// nhiều thực thể để kiểm thử không phải dùng chung trạng thái với nhau.
/// </remarks>
public sealed class BinanceIpBanTracker
{
    /// <summary>Bản dùng chung cho mọi provider trong tiến trình — mọi lời gọi đi ra cùng một IP.</summary>
    public static BinanceIpBanTracker Shared { get; } = new();

    /// <summary>
    /// Lùi mặc định khi sàn báo vượt hạn mức mà KHÔNG kèm mốc hết cấm.
    /// </summary>
    /// <remarks>
    /// <c>429</c> là cảnh báo (còn kịp chậm lại), <c>418</c> là đã bị cấm — nên hai mức lùi khác
    /// nhau. Thà lùi thừa vài phút còn hơn ăn thêm một lệnh cấm dài vì vẫn gõ cửa.
    /// </remarks>
    public static readonly TimeSpan DefaultRateLimitBackoff = TimeSpan.FromMinutes(2);

    /// <inheritdoc cref="DefaultRateLimitBackoff"/>
    public static readonly TimeSpan DefaultBanBackoff = TimeSpan.FromMinutes(10);

    private long _bannedUntilUnixMs;

    /// <summary>Thời điểm hết cấm, hoặc <c>null</c> khi không bị cấm.</summary>
    public DateTimeOffset? BannedUntil
    {
        get
        {
            var ms = Interlocked.Read(ref _bannedUntilUnixMs);
            return ms == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(ms);
        }
    }

    /// <summary>Còn đang bị cấm tại thời điểm <paramref name="now"/> không.</summary>
    /// <param name="remaining">Thời gian còn lại của lệnh cấm; <see cref="TimeSpan.Zero"/> nếu không bị cấm.</param>
    public bool IsBanned(DateTimeOffset now, out TimeSpan remaining)
    {
        var until = Interlocked.Read(ref _bannedUntilUnixMs);
        var nowMs = now.ToUnixTimeMilliseconds();

        if (until <= nowMs)
        {
            remaining = TimeSpan.Zero;
            return false;
        }

        remaining = TimeSpan.FromMilliseconds(until - nowMs);
        return true;
    }

    /// <summary>Ghi nhận một phản hồi lỗi. Chỉ <c>418</c> và <c>429</c> mới tạo lệnh cấm.</summary>
    public void Note(int statusCode, string? body, DateTimeOffset now)
    {
        if (statusCode is not (418 or 429)) return;

        var until = ParseBannedUntil(body)
                    ?? now.Add(statusCode == 418 ? DefaultBanBackoff : DefaultRateLimitBackoff)
                        .ToUnixTimeMilliseconds();

        // So-rồi-đổi trong vòng lặp: nhiều job có thể cùng đụng tường một lúc, và phép gán thẳng
        // sẽ để một phản hồi cũ ghi đè mốc xa hơn của phản hồi mới. Chỉ NỚI RA, không rút ngắn.
        while (true)
        {
            var current = Interlocked.Read(ref _bannedUntilUnixMs);
            if (until <= current) return;
            if (Interlocked.CompareExchange(ref _bannedUntilUnixMs, until, current) == current) return;
        }
    }

    /// <summary>Xoá lệnh cấm đang ghi nhận. Dùng cho kiểm thử và cho thao tác tay khi cần.</summary>
    public void Reset() => Interlocked.Exchange(ref _bannedUntilUnixMs, 0);

    /// <summary>
    /// Mốc hết cấm (Unix ms) đọc từ thông điệp lỗi, hoặc <c>null</c> nếu thông điệp không nêu.
    /// </summary>
    /// <remarks>
    /// Dạng thật của thông điệp:
    /// <code>
    /// {"code":-1003,"msg":"Way too many requests; IP(130.176.187.73) banned until 1788068515794. Please use the websocket..."}
    /// </code>
    /// Đọc con số thay vì đoán: nó chính xác, còn phép đoán thì hoặc lùi thừa (mất thời gian bảo
    /// vệ vị thế) hoặc lùi thiếu (ăn thêm một lệnh cấm nữa).
    /// </remarks>
    public static long? ParseBannedUntil(string? body)
    {
        if (string.IsNullOrEmpty(body)) return null;

        const string marker = "banned until ";
        var start = body.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;

        start += marker.Length;
        var end = start;
        while (end < body.Length && char.IsDigit(body[end])) end++;

        return end > start
               && long.TryParse(body.AsSpan(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture, out var ms)
            ? ms
            : null;
    }
}
