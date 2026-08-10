using MMW.Domain.Enums;

namespace MMW.Application.Trading.TimeGuard;

/// <summary>
/// Một khoảng thời gian không được vào lệnh, kèm lý do.
/// </summary>
/// <remarks>
/// Khoảng là NỬA MỞ <c>[FromUtc, ToUtc)</c>. Chọn nửa mở để phép hợp nhất (FR-012) khép kín:
/// <c>[a,b) ∪ [b,c) = [a,c)</c> không để lại kẽ hở một tích tắc nào ở điểm nối, còn nếu dùng
/// khoảng đóng thì hai cửa sổ liền nhau sẽ đè lên nhau đúng một thời điểm và phép hợp nhất
/// phải xử lý riêng.
///
/// Sau khi hợp nhất, một cửa sổ có thể gồm nhiều sự kiện. Khi đó <see cref="Kind"/>,
/// <see cref="Impact"/> và <see cref="EventAtUtc"/> thuộc về sự kiện NẶNG NHẤT trong nhóm, còn
/// <see cref="RequiresPositionAction"/> và <see cref="BlocksNewEntries"/> là phép HOẶC của cả
/// nhóm — hợp nhất không bao giờ được làm mất một lớp bảo vệ (Nguyên tắc III).
/// </remarks>
/// <param name="FromUtc">Thời điểm bắt đầu chặn, bao gồm.</param>
/// <param name="ToUtc">Thời điểm hết chặn, KHÔNG bao gồm.</param>
/// <param name="EventAtUtc">Thời điểm sự kiện nặng nhất trong cửa sổ. Cần cho ghi vết FR-015.</param>
/// <param name="Kind">Loại của sự kiện nặng nhất.</param>
/// <param name="Title">Tên sự kiện; nhiều sự kiện thì nối bằng " + ".</param>
/// <param name="Impact">Mức tác động cao nhất trong nhóm.</param>
/// <param name="RequiresPositionAction">Có buộc xử lý vị thế đang mở không (FR-013).</param>
/// <param name="BlocksNewEntries">Có chặn lệnh MỚI không. Sai nghĩa là chỉ cần xử lý vị thế.</param>
public sealed record BlackoutWindow(
    DateTime FromUtc,
    DateTime ToUtc,
    DateTime EventAtUtc,
    ScheduledEventKind Kind,
    string Title,
    MacroEventImpact Impact,
    bool RequiresPositionAction,
    bool BlocksNewEntries)
{
    public bool Contains(DateTime utc) => utc >= FromUtc && utc < ToUtc;

    public int DurationMinutes => (int)(ToUtc - FromUtc).TotalMinutes;
}

/// <summary>Kết quả hỏi "thời điểm này có được vào lệnh mới không".</summary>
/// <param name="ReasonVi">Lý do bằng tiếng Việt, nêu giờ Việt Nam. Null khi không bị chặn.</param>
public sealed record BlackoutDecision(bool IsBlocked, BlackoutWindow? Window, string? ReasonVi)
{
    /// <summary>Không có cửa sổ nào chặn. Zero lệnh là kết quả đúng, và cho phép cũng vậy.</summary>
    public static BlackoutDecision Allowed { get; } = new(false, null, null);
}

/// <summary>
/// Tình trạng cập nhật của phần lịch NẠP TAY (FR-014).
/// </summary>
/// <param name="LastSeededEventUtc">
/// Mốc phủ lịch ngắn nhất trong các loại bắt buộc. Null nghĩa là còn ít nhất một loại chưa có dữ liệu.
/// </param>
public sealed record CalendarFreshness(bool IsStale, DateTime? LastSeededEventUtc, string? WarningVi)
{
    /// <summary>Chi tiết theo loại để UI/giám sát chỉ ra chính xác lịch nào đang thiếu.</summary>
    public IReadOnlyList<CalendarKindFreshness> Kinds { get; init; } = [];
}

public sealed record CalendarKindFreshness(
    ScheduledEventKind Kind,
    DateTime? LastSeededEventUtc,
    bool IsStale);
