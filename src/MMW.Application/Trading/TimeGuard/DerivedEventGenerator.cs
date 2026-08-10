using MMW.Domain.Entities;
using MMW.Domain.Enums;

namespace MMW.Application.Trading.TimeGuard;

/// <summary>Sinh các sự kiện tính được bằng công thức lịch, không cần nguồn dữ liệu ngoài.</summary>
public interface IDerivedEventGenerator
{
    /// <summary>
    /// Hàm THUẦN của khoảng thời gian. Không I/O, không đồng hồ, không cơ sở dữ liệu.
    /// Trả về các sự kiện có mốc nằm trong nửa khoảng <c>[fromUtc, toUtc)</c>.
    /// </summary>
    IReadOnlyList<ScheduledEvent> Generate(DateTime fromUtc, DateTime toUtc, string symbol);
}

/// <summary>
/// Lớp bảo vệ không phụ thuộc dữ liệu nạp tay: thanh toán phí vốn, đáo hạn quyền chọn và
/// khoảng trống cuối tuần đều suy ra được từ cuốn lịch, nên chúng vẫn hoạt động đủ 100% kể cả
/// khi bảng sự kiện rỗng hoặc đã quá hạn (ràng buộc 2 của contract, SC-009).
/// </summary>
/// <remarks>
/// Các mốc giờ dưới đây là ĐẶC ĐIỂM CỦA SÀN chứ không phải ngưỡng của thuật toán, nên chúng
/// nằm trong mã chứ không nằm trong cấu hình: Binance USDT-M thanh toán phí vốn lúc 00/08/16
/// UTC, Deribit đáo hạn quyền chọn thứ Sáu 08:00 UTC, CME đóng cửa cuối tuần. Đổi những con số
/// này nghĩa là đổi sang một sàn khác, không phải chỉnh một tham số. Phần CẤU HÌNH ĐƯỢC là độ
/// rộng cửa sổ chặn quanh mỗi mốc — nằm ở <see cref="BlackoutRule"/> (Nguyên tắc I).
/// </remarks>
public sealed class DerivedEventGenerator : IDerivedEventGenerator
{
    /// <summary>Ba mốc thanh toán phí vốn hằng ngày của hợp đồng vĩnh cửu USDT-M.</summary>
    private static readonly int[] FundingHoursUtc = { 0, 8, 16 };

    private const int OptionsExpiryHourUtc = 8;
    private const int WeekendGapHourUtc = 21;
    private const int WeekendGapDurationMinutes = 120;

    /// <summary>
    /// Chặn trên độ dài khoảng để một lời gọi lỡ tay không sinh hàng triệu bản ghi.
    /// Mười năm đủ cho mọi nhu cầu kiểm thử lịch sử của hệ thống này.
    /// </summary>
    private const int MaxSpanDays = 3660;

    public IReadOnlyList<ScheduledEvent> Generate(DateTime fromUtc, DateTime toUtc, string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Thiếu mã giao dịch.", nameof(symbol));

        var from = AsUtc(fromUtc, nameof(fromUtc));
        var to = AsUtc(toUtc, nameof(toUtc));

        if (to <= from) return Array.Empty<ScheduledEvent>();

        if ((to - from).TotalDays > MaxSpanDays)
            throw new ArgumentException(
                $"Khoảng thời gian {(to - from).TotalDays:N0} ngày vượt trần {MaxSpanDays} ngày.", nameof(toUtc));

        var events = new List<ScheduledEvent>();

        for (var day = from.Date; day < to; day = day.AddDays(1))
        {
            foreach (var hour in FundingHoursUtc)
            {
                var at = day.AddHours(hour);
                if (!InRange(at, from, to)) continue;

                events.Add(new ScheduledEvent
                {
                    Kind = ScheduledEventKind.FundingSettlement,
                    Title = $"Thanh toán phí vốn {at:HH:mm} UTC",
                    OccursAtUtc = at,
                    Impact = MacroEventImpact.Low,
                    Origin = ScheduledEventOrigin.Derived,
                    SourceKey = $"derived:funding:{at:yyyy-MM-ddTHH}",
                });
            }

            if (day.DayOfWeek == DayOfWeek.Friday)
            {
                var at = day.AddHours(OptionsExpiryHourUtc);
                if (InRange(at, from, to))
                {
                    // Thứ Sáu cuối tháng là đáo hạn THÁNG — khối lượng lớn hơn hẳn đáo hạn tuần.
                    // Sinh đúng MỘT sự kiện chứ không phải một tuần cộng một tháng chồng lên nhau.
                    var isMonthly = IsLastFridayOfMonth(day);

                    events.Add(new ScheduledEvent
                    {
                        Kind = ScheduledEventKind.OptionsExpiry,
                        Title = isMonthly ? "Đáo hạn quyền chọn tháng" : "Đáo hạn quyền chọn tuần",
                        OccursAtUtc = at,
                        Impact = isMonthly ? MacroEventImpact.High : MacroEventImpact.Medium,
                        Origin = ScheduledEventOrigin.Derived,
                        SourceKey = $"derived:optex:{at:yyyy-MM-dd}",
                    });
                }
            }

            if (day.DayOfWeek == DayOfWeek.Sunday)
            {
                var at = day.AddHours(WeekendGapHourUtc);
                if (InRange(at, from, to))
                {
                    events.Add(new ScheduledEvent
                    {
                        Kind = ScheduledEventKind.WeekendGap,
                        Title = "Khoảng trống cuối tuần",
                        OccursAtUtc = at,
                        DurationMinutes = WeekendGapDurationMinutes,
                        Impact = MacroEventImpact.Medium,
                        Origin = ScheduledEventOrigin.Derived,
                        SourceKey = $"derived:weekendgap:{at:yyyy-MM-dd}",
                    });
                }
            }
        }

        return events;
    }

    private static bool InRange(DateTime at, DateTime from, DateTime to) => at >= from && at < to;

    /// <summary>
    /// Cộng 7 ngày mà sang tháng khác thì đây là thứ Sáu cuối cùng. Đúng cho cả tháng có 5 thứ
    /// Sáu lẫn tháng Hai năm nhuận, vì nó không giả định gì về ngày cuối tháng.
    /// </summary>
    private static bool IsLastFridayOfMonth(DateTime friday) => friday.AddDays(7).Month != friday.Month;

    /// <summary>
    /// Từ chối <see cref="DateTimeKind.Local"/>. Nhận bừa giờ địa phương rồi coi như UTC là cách
    /// êm ái nhất để lệch 7 tiếng trên máy Việt Nam mà không test nào đỏ.
    /// </summary>
    private static DateTime AsUtc(DateTime value, string paramName) => value.Kind switch
    {
        DateTimeKind.Local => throw new ArgumentException(
            "Cần thời điểm UTC, nhận được giờ địa phương.", paramName),
        DateTimeKind.Utc => value,
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
