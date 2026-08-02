using MMW.Domain.Enums;

namespace MMW.Domain.Entities;

/// <summary>
/// Một mẩu bối cảnh thị trường do lớp AI sinh ra. Chỉ dùng để VETO hoặc GIẢM kích thước.
/// </summary>
/// <remarks>
/// Quy tắc đọc: <c>WHERE ExpiresAtUtc &gt; clock.UtcNow</c>. Bản ghi hết hạn coi như
/// không tồn tại nhưng KHÔNG xoá — giữ lại để truy vết về sau xem AI đã nói gì và
/// nó có đúng không.
/// </remarks>
public class MarketContextRecord : BaseEntity
{
    public MarketContextKind Kind { get; set; }

    /// <summary><c>noise</c> / <c>low</c> / <c>medium</c> / <c>high</c> / <c>critical</c>.</summary>
    public string Severity { get; set; } = "noise";

    public MarketBias Leaning { get; set; }

    /// <summary>Phân tách bằng dấu phẩy.</summary>
    public string? AffectedSymbols { get; set; }

    /// <summary>Tiếng Việt.</summary>
    public string? Narrative { get; set; }

    /// <summary>Tin đồn chưa xác nhận ⟹ <see cref="Severity"/> bị cắt trần ở <c>medium</c>.</summary>
    public bool IsRumor { get; set; }

    public DateTime RecordedAtUtc { get; set; }

    /// <summary>Tính từ <c>halfLifeMinutes</c> của phản hồi (FR-044).</summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Chống xử lý lại cùng một tin.</summary>
    public string? SourceKey { get; set; }

    /// <summary>Phản hồi thô, ghi vết theo Nguyên tắc IV.</summary>
    public string? RawResponseJson { get; set; }

    /// <summary>
    /// Các trường bị loại vì AI vượt quyền (FR-041, FR-043). Trường này khác rỗng là tín hiệu
    /// prompt đã trôi khỏi vai trò và cần xem lại — nó đáng được theo dõi như một chỉ số sức khoẻ.
    /// </summary>
    public string? RejectedFields { get; set; }
}
