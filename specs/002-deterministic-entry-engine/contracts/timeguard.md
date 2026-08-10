# Contract — Chặn theo khung giờ

**Namespace**: `MMW.Application.Trading.TimeGuard`

---

## `ITimeGuardService`

```csharp
public interface ITimeGuardService
{
    /// <summary>Thời điểm này có được vào lệnh mới không.</summary>
    Task<BlackoutDecision> CheckAsync(long tradingAccountId, string symbol, DateTime utcNow, CancellationToken ct = default);

    /// <summary>Mọi cửa sổ chặn giao với khoảng [from, to), đã hợp nhất phần chồng lấn (FR-012).</summary>
    Task<IReadOnlyList<BlackoutWindow>> GetWindowsAsync(long tradingAccountId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    /// <summary>Cửa sổ chặn kế tiếp bắt đầu trong vòng `withinMinutes`. Dùng cho xử lý vị thế đang mở (FR-013).</summary>
    Task<BlackoutWindow?> GetUpcomingAsync(long tradingAccountId, DateTime utcNow, int withinMinutes, CancellationToken ct = default);

    /// <summary>Phần lịch NẠP TAY còn hạn không (FR-014).</summary>
    Task<CalendarFreshness> GetCalendarFreshnessAsync(DateTime utcNow, CancellationToken ct = default);
}
```

```csharp
public sealed record BlackoutDecision(
    bool IsBlocked,
    BlackoutWindow? Window,
    string? ReasonVi);          // tiếng Việt, nêu tên sự kiện + giờ Việt Nam

public sealed record BlackoutWindow(
    DateTime FromUtc,
    DateTime ToUtc,
    DateTime EventAtUtc,
    ScheduledEventKind Kind,
    string Title,
    MacroEventImpact Impact,
    bool RequiresPositionAction,
    bool BlocksNewEntries);

public sealed record CalendarFreshness(bool IsStale, DateTime? LastSeededEventUtc, string? WarningVi)
{
    public IReadOnlyList<CalendarKindFreshness> Kinds { get; init; } = [];
}

public sealed record CalendarKindFreshness(
    ScheduledEventKind Kind, DateTime? LastSeededEventUtc, bool IsStale);
```

### Chênh lệch so với bản phác thảo ban đầu (ghi khi hiện thực hoá ở Phase 3)

| Thay đổi | Lý do |
|---|---|
| Ba phương thức nhận thêm `long tradingAccountId` | Độ rộng cửa sổ chặn là cấu hình THEO TÀI KHOẢN (`EngineSetting.BlackoutRules`, Nguyên tắc I). Bản phác thảo thiếu tham số này nên không đọc nổi cấu hình của ai. |
| Thêm `GetCalendarFreshnessAsync` | FR-014 cần một phép hỏi thuần để giao diện và job cảnh báo cùng dùng, không ai phải tự truy vấn bảng sự kiện. |
| `BlackoutWindow` thêm `EventAtUtc` | FR-015 yêu cầu ghi vết có *thời điểm sự kiện*. Sau khi hợp nhất thì thông tin này mất, nên phải mang theo. |
| `BlackoutWindow` thêm `BlocksNewEntries` | Không có nó thì cột `BlackoutRule.BlocksNewEntries` là cấu hình chết — cửa sổ chỉ-xử-lý-vị-thế không diễn đạt được. |

Cả bốn thay đổi đều là THÊM, không bớt phần nào của bản phác thảo.

### Ngữ nghĩa khoảng

Mọi khoảng là **nửa mở** `[FromUtc, ToUtc)`. Chọn nửa mở để phép hợp nhất khép kín: `[a,b) ∪ [b,c) = [a,c)` không để lại kẽ hở một tích tắc nào ở điểm nối. Hệ quả: đúng thời điểm `ToUtc` là ĐÃ RA khỏi cửa sổ.

Cửa sổ **chạm nhau đúng biên cũng hợp nhất** — để lộ ra hai dòng liền kề là mô tả sai sự thật, vì không có khoảnh khắc nào ở giữa được phép vào lệnh.

Thứ tự bắt buộc: **cắt cửa sổ AI trước, hợp nhất sau, cắt theo khoảng hỏi sau cùng**. Hợp nhất trước khi cắt sẽ để một cửa sổ AI dài 20 tiếng nuốt các cửa sổ thật rồi kéo chúng biến mất theo khi bị cắt; cắt theo khoảng hỏi trước khi hợp nhất sẽ trả về khoảng ngắn hơn sự thật.

### Ràng buộc

| # | Ràng buộc | Nguồn |
|---|---|---|
| 1 | Cửa sổ chồng lấn PHẢI hợp nhất thành một khoảng liên tục | FR-012 |
| 2 | Lịch nạp tay rỗng ⟹ cửa sổ **sinh bằng công thức vẫn hoạt động đủ 100%** | FR-014, SC-009 |
| 3 | Cửa sổ do AI đề xuất bị cắt về `EngineSetting.AiBlackoutMaxMinutes` | FR-011 |
| 4 | Sự kiện không có giờ cụ thể được xử lý theo hướng an toàn (chặn cả ngày sự kiện) | Edge Cases |
| 4b | *Cách hiện thực*: sự kiện chỉ-có-ngày được nạp với `OccursAtUtc = 00:00` và `DurationMinutes = 1440`, để công thức cửa sổ chung tự chặn trọn ngày — không có nhánh đặc biệt nào trong `TimeGuardService` | Ràng buộc 4 |
| 5 | Mọi lần chặn ghi vết dạng cấu trúc: loại, thời điểm sự kiện, biên cửa sổ, thời điểm đánh giá | FR-015 |
| 6 | `utcNow` **luôn** đến từ `IClock`, không bao giờ từ `DateTime.UtcNow` | R-001 |

Ràng buộc 2 là điều kiện thiết kế quan trọng nhất của service này: nếu quên cập nhật lịch sang năm sau, lớp bảo vệ **không được im lặng biến mất**. Cửa sổ sinh bằng công thức (thanh toán phí vốn, đáo hạn quyền chọn, cuối tuần) luôn có, và hệ thống phải kêu lên rằng phần nạp tay đã quá hạn.

---

## `IScheduledEventCalendar`

```csharp
public interface IScheduledEventCalendar
{
    Task<IReadOnlyList<ScheduledEvent>> GetBetweenAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    /// <summary>Mốc lịch cuối cùng theo từng loại; loại vắng mặt phải được coi là thiếu (FR-014).</summary>
    Task<IReadOnlyDictionary<ScheduledEventKind, DateTime>> GetLastSeededEventUtcByKindAsync(
        IReadOnlyCollection<ScheduledEventKind> kinds, CancellationToken ct = default);

    Task<int> ImportAsync(IEnumerable<ScheduledEvent> events, CancellationToken ct = default);
}
```

`ImportAsync` bất biến theo `SourceKey`: nạp lại cùng một tệp lịch không sinh bản ghi trùng.

---

## `IDerivedEventGenerator` — sự kiện tính bằng công thức

```csharp
public interface IDerivedEventGenerator
{
    /// <summary>Hàm THUẦN của khoảng thời gian. Không I/O, không đồng hồ, không cơ sở dữ liệu.</summary>
    IReadOnlyList<ScheduledEvent> Generate(DateTime fromUtc, DateTime toUtc, string symbol);
}
```

| Sự kiện | Quy tắc |
|---|---|
| Thanh toán phí vốn | 00:00, 08:00, 16:00 UTC mỗi ngày |
| Đáo hạn quyền chọn tuần | Thứ Sáu 08:00 UTC |
| Đáo hạn quyền chọn tháng | Thứ Sáu **cuối cùng** của tháng, 08:00 UTC |
| Khoảng trống cuối tuần | Chủ nhật 21:00 UTC, độ dài 120 phút |

Là hàm thuần nên kiểm thử bằng bảng đầu vào/đầu ra — bao gồm các trường hợp biên: tháng có 5 thứ Sáu, tuần bắc cầu qua giao thừa, năm nhuận.

---

## `SessionQualityTable`

```csharp
public interface ISessionQualityProvider
{
    /// <summary>
    /// Điểm 0–6 cho giờ UTC. Dùng bảng chuẩn khi tài khoản có dưới
    /// EngineSetting.PersonalStatsMinClosedTrades lệnh đã đóng; sau đó dùng tỷ lệ thắng thật.
    /// </summary>
    Task<SessionQuality> GetAsync(long tradingAccountId, DateTime utcNow, CancellationToken ct = default);
}

public sealed record SessionQuality(
    int Score,                  // 0–6
    string Label,
    bool IsPersonalised,        // true khi đã dùng thống kê thật của trader
    int SampleSize);
```

`IsPersonalised` phải hiển thị được trên giao diện. Thời điểm chuyển từ bảng chuẩn sang thống kê cá nhân làm điểm số nhảy bậc; trader cần biết vì sao thay vì tự đoán.

### Cách tính điểm cá nhân (chốt ở Phase 3)

Thống kê gom theo **khung phiên** (6 dòng của `SessionQualityRows`), không theo từng giờ: chia 50 lệnh cho 24 giờ thì giờ nào cũng chỉ một hai mẫu, và điểm sẽ nhảy theo nhiễu chứ không theo kỹ năng.

Điểm cá nhân được **kéo về bảng chuẩn theo cỡ mẫu** thay vì chia thẳng:

```
điểm = (n × tỷVongThắng × 6 + k × điểmChuẩn) / (n + k)
```

với `n` = số lệnh thắng+thua của khung phiên (hoà vốn không tính vào cả tử lẫn mẫu) và `k = EngineSetting.SessionStatsSmoothingTrades` (mặc định 10, Nguyên tắc I). Chia thẳng sẽ cho một khung phiên có đúng một lệnh thua điểm 0 và cấm cửa nó vĩnh viễn dựa trên một mẫu duy nhất.

`IsPersonalised` chỉ đúng khi tài khoản đã đủ `PersonalStatsMinClosedTrades` lệnh **và** khung phiên đang hỏi có `n > 0`. Đủ tổng số lệnh nhưng khung này chưa có lệnh nào thì điểm vẫn là giá trị chuẩn, và phải nói thẳng là chưa cá nhân hoá.

Bảng phiên thủng lỗ ⟹ **ném `InvalidOperationException`**, không trả 0 điểm. "Thiếu dữ liệu ⟹ 0 điểm" là cách hỏng tệ nhất: hệ thống vẫn chạy, chỉ là mỗi ngày đúng khung giờ đó lại mất 6 điểm mà không ai biết vì sao.

---

## Phụ trợ ngoài tầng quyết định

Hai lớp dưới đây nằm ở `MMW.Application.Services`, KHÔNG nằm trong `MMW.Application.Trading.*`, vì chúng gửi thông báo — kéo `INotificationService` vào namespace bị bộ gác hiến chương canh sẽ làm hỏng ranh giới tất định.

```csharp
public interface ICalendarFreshnessMonitor
{
    /// <summary>Kiểm tra và phát cảnh báo lịch quá hạn nếu cần (FR-014). Trả về đúng khi đã phát.</summary>
    Task<bool> RunAsync(DateTime utcNow, CancellationToken ct = default);
}

public interface IPositionManageService
{
    /// <summary>Rà soát vị thế đang mở và quyết định việc phải làm trước cửa sổ chặn (FR-013).</summary>
    Task<IReadOnlyList<PositionAction>> RunAsync(long tradingAccountId, DateTime utcNow, CancellationToken ct = default);
}

public enum PositionActionKind { MoveStopToBreakeven = 1, ClosePartial = 2 }

public sealed record PositionAction(
    long TradeId, string Symbol, PositionActionKind Kind, decimal? RMultiple, string ReasonVi);
```

Quy tắc bất di dịch của `IPositionManageService`: **mọi vị thế đang mở đều nhận đúng một hành động, không có nhánh nào để nguyên trạng.** Thiếu dừng lỗ hay không lấy được giá cũng phải hành động, và hành động an toàn hơn (`ClosePartial`). Đứng ngoài không vào lệnh mới lúc CPI ra mà vẫn ôm nguyên vị thế cũ thì cả tầng này chẳng tránh được gì.

Cảnh báo lịch quá hạn phát ở mức `Critical` chứ không phải `Warning`: loại `SystemHealth` đặt ngưỡng mặc định ở `Critical`, nên `Warning` sẽ bị lọc mất và cảnh báo không bao giờ đến tay ai.
