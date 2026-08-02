# Contract — Chặn theo khung giờ

**Namespace**: `MMW.Application.Trading.TimeGuard`

---

## `ITimeGuardService`

```csharp
public interface ITimeGuardService
{
    /// <summary>Thời điểm này có được vào lệnh mới không.</summary>
    Task<BlackoutDecision> CheckAsync(string symbol, DateTime utcNow, CancellationToken ct = default);

    /// <summary>Mọi cửa sổ chặn giao với khoảng [from, to), đã hợp nhất phần chồng lấn (FR-012).</summary>
    Task<IReadOnlyList<BlackoutWindow>> GetWindowsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    /// <summary>Cửa sổ chặn kế tiếp bắt đầu trong vòng `withinMinutes`. Dùng cho xử lý vị thế đang mở (FR-013).</summary>
    Task<BlackoutWindow?> GetUpcomingAsync(DateTime utcNow, int withinMinutes, CancellationToken ct = default);
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
    ScheduledEventKind Kind,
    string Title,
    MacroEventImpact Impact,
    bool RequiresPositionAction);
```

### Ràng buộc

| # | Ràng buộc | Nguồn |
|---|---|---|
| 1 | Cửa sổ chồng lấn PHẢI hợp nhất thành một khoảng liên tục | FR-012 |
| 2 | Lịch nạp tay rỗng ⟹ cửa sổ **sinh bằng công thức vẫn hoạt động đủ 100%** | FR-014, SC-009 |
| 3 | Cửa sổ do AI đề xuất bị cắt về `EngineSetting.AiBlackoutMaxMinutes` | FR-011 |
| 4 | Sự kiện không có giờ cụ thể được xử lý theo hướng an toàn (chặn cả ngày sự kiện) | Edge Cases |
| 5 | Mọi lần chặn ghi vết dạng cấu trúc: loại, thời điểm sự kiện, biên cửa sổ, thời điểm đánh giá | FR-015 |
| 6 | `utcNow` **luôn** đến từ `IClock`, không bao giờ từ `DateTime.UtcNow` | R-001 |

Ràng buộc 2 là điều kiện thiết kế quan trọng nhất của service này: nếu quên cập nhật lịch sang năm sau, lớp bảo vệ **không được im lặng biến mất**. Cửa sổ sinh bằng công thức (thanh toán phí vốn, đáo hạn quyền chọn, cuối tuần) luôn có, và hệ thống phải kêu lên rằng phần nạp tay đã quá hạn.

---

## `IScheduledEventCalendar`

```csharp
public interface IScheduledEventCalendar
{
    Task<IReadOnlyList<ScheduledEvent>> GetBetweenAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    /// <summary>Sự kiện nạp tay cuối cùng. Null hoặc đã ở quá khứ ⟹ lịch quá hạn (FR-014).</summary>
    Task<DateTime?> GetLastSeededEventUtcAsync(CancellationToken ct = default);

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
