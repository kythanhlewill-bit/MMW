# Contract — Kiểm thử lịch sử

**Namespace**: `MMW.Application.Backtest`

---

## Nguyên tắc chi phối

> Kiểm thử lịch sử **không có mã riêng của nó**. Nó thay hai cổng — `IClock` và `IMarketDataProvider` — rồi gọi đúng những service mà chạy thật gọi.

Bất kỳ dòng nào có dạng `if (isBacktest)` bên trong `MMW.Application.Trading` đều là vi phạm FR-053 và phải bị chặn ở review.

---

## `IBacktestEngine`

```csharp
public interface IBacktestEngine
{
    Task<BacktestReport> RunAsync(BacktestRequest request, CancellationToken ct = default);
}

public sealed record BacktestRequest(
    string Name,
    DateTime FromUtc,
    DateTime ToUtc,
    IReadOnlyList<string> Symbols,
    long TradingAccountId,
    EngineSetting? SettingsOverride = null);
```

### Vòng lặp chính

```
1. Nạp toàn bộ nến của [FromUtc, ToUtc] từ KlineArchive vào bộ nhớ.
2. Với mỗi mốc đóng nến 15m theo thứ tự thời gian tăng dần:
     a. BacktestClock.Advance(mốc + 1 phút)          ← khớp độ trễ thật ở R-011
     b. Sang ngày UTC mới → gọi DailyPlanService.GenerateAsync (CÙNG service với chạy thật)
     c. TimeGuardService.CheckAsync                   (CÙNG service)
     d. EntryScorer.Score                             (CÙNG service)
     e. Điểm đạt ngưỡng → mở vị thế mô phỏng
     f. Cập nhật các vị thế đang mở: kiểm tra chạm dừng lỗ / chốt lời trên nến kế tiếp
3. Tổng hợp báo cáo.
```

**Bước 2f — thứ tự kiểm tra khi một nến chạm cả hai mức**: luôn giả định **dừng lỗ khớp trước**. Dữ liệu nến không cho biết giá chạm mức nào trước trong nội bộ cây nến, và giả định ngược lại sẽ thổi phồng kết quả một cách có hệ thống. Đây là lựa chọn thận trọng có chủ ý và phải được ghi trong `Limitations`.

---

## `BacktestClock`

```csharp
public sealed class BacktestClock : IClock
{
    public DateTime UtcNow { get; private set; }
    public void Advance(DateTime toUtc);   // chỉ tiến, không lùi
}
```

`Advance` ném ngoại lệ khi bị gọi với thời điểm lùi về quá khứ. Thời gian đi lùi trong một lần chạy là dấu hiệu chắc chắn của lỗi nhìn trước tương lai (look-ahead bias) — phải nổ ngay chứ không được âm thầm cho ra kết quả đẹp.

---

## `ArchiveMarketDataProvider`

```csharp
public sealed class ArchiveMarketDataProvider : IMarketDataProvider
{
    // GetCandlesAsync  → đọc từ KlineArchive, CHỈ trả nến có CloseTime <= clock.UtcNow
    // GetTickerAsync   → giá đóng của nến đã đóng gần nhất
    // GetFundingAsync            → đọc từ kho lịch sử phí vốn (có đủ ≥ 2 năm, R-003)
    // GetOpenInterestHistAsync   → null (chỉ 30 ngày, không dựng lại được)
    // GetGlobalLongShortRatioAsync, GetDepthAsync, GetTakerBuySellRatioAsync → null
}
```

**Bất biến chống nhìn trước tương lai**: `GetCandlesAsync` không bao giờ trả cây nến có `CloseTime > clock.UtcNow`. Đây là một dòng lọc duy nhất và là dòng quan trọng nhất trong toàn bộ engine kiểm thử. Kèm test riêng cho nó.

---

## `IKlineArchiveService`

```csharp
public interface IKlineArchiveService
{
    /// <summary>Nạp bổ sung từ sàn. Bất biến: nạp lại cùng khoảng không sinh bản ghi trùng (FR-005).</summary>
    Task<int> BackfillAsync(string symbol, string interval, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    Task<IReadOnlyList<Candle>> GetRangeAsync(string symbol, string interval, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    /// <summary>Các khoảng thiếu nến trong kho. Rỗng ⟹ dữ liệu liền mạch.</summary>
    Task<IReadOnlyList<(DateTime From, DateTime To)>> FindGapsAsync(string symbol, string interval, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
}
```

`FindGapsAsync` phải được gọi **trước** mỗi lần chạy kiểm thử. Chạy trên dữ liệu khuyết cho ra kết quả trông hợp lệ nhưng sai — kiểu lỗi tệ nhất. Có khoảng trống ⟹ từ chối chạy, không phải cảnh báo rồi chạy tiếp.

---

## `BacktestReport`

```csharp
public sealed record BacktestReport(
    long RunId,
    int TradeCount,
    decimal WinRate,
    decimal ExpectancyR,
    decimal MaxDrawdownPercent,
    int LongestLossStreak,
    decimal TotalFees,
    decimal TotalSlippage,
    IReadOnlyDictionary<int, HourStats> ByHourUtc,
    IReadOnlyDictionary<DayRegime, RegimeStats> ByRegime,
    IReadOnlyList<string> Limitations);   // KHÔNG ĐƯỢC rỗng
```

### `Limitations` bắt buộc có tối thiểu

1. Các tiêu chí bị 0 điểm do thiếu dữ liệu lịch sử, kèm tổng số điểm bị mất (hiện là **10/100**: `liquidity.open_interest` 5 + `liquidity.spread_depth` 5)
1b. Phí vốn dùng tỷ lệ **đã thanh toán** thay cho tỷ lệ **dự phóng** mà chạy thật dùng — một xấp xỉ, phải nói rõ
2. Giả định dừng lỗ khớp trước khi một nến chạm cả hai mức
3. Phí và trượt giá đã dùng
4. Bỏ qua phí vốn khi giữ vị thế qua mốc thanh toán
5. Số nến bị thiếu trong kho, nếu có

Ràng buộc `Limitations` không rỗng được cưỡng chế bằng test. Một báo cáo kiểm thử không nêu hạn chế của chính nó sẽ được đọc như một lời hứa — và đó chính là cách người ta thuyết phục bản thân bật giao dịch thật quá sớm.

---

## Test tương đương — tiêu chí chấp nhận bắt buộc (SC-003)

```
1. Nạp một khoảng dữ liệu vào KlineArchive.
2. Chạy BacktestEngine trên khoảng đó, thu chuỗi EntryScorecard (A).
3. Chạy SignalEvalService ở chế độ mô phỏng, cùng khoảng, cùng dữ liệu,
   với BacktestClock, thu chuỗi EntryScorecard (B).
4. Khẳng định A và B khớp nhau ở mọi trường: điểm từng tiêu chí, điểm tổng,
   lý do veto, và kích thước cuối cùng.
```

Đây là test đắt nhất và có giá trị cao nhất trong feature. Nó đỏ đúng vào lúc ai đó vô tình thêm một nhánh mã riêng cho kiểm thử — thời điểm mà mọi con số kiểm thử bắt đầu nói dối.
