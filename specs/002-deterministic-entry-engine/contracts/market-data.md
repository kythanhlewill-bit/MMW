# Contract — Dữ liệu thị trường

**Namespace**: `MMW.Application.MarketData` · `MMW.Application.Abstractions`

---

## `IClock` — cổng thời gian

```csharp
namespace MMW.Application.Abstractions;

public interface IClock
{
    DateTime UtcNow { get; }
}
```

Cài đặt: `SystemClock` (Infrastructure) và `BacktestClock` (Application/Backtest).

**Đây là hợp đồng quan trọng nhất của feature.** Không có nó thì tính tương đương giữa kiểm thử lịch sử và chạy thật không tồn tại. Xem R-001.

**Cưỡng chế**: một test quét reflection toàn bộ `MMW.Application.Trading` và `MMW.Application.Backtest`, khẳng định không có tham chiếu tới `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow`, hay `Random`. Vi phạm làm đỏ bộ test — không phải cảnh báo, là lỗi.

---

## `IMarketDataProvider` — mở rộng

Ba phương thức hiện có giữ nguyên chữ ký. Năm phương thức mới:

```csharp
public interface IMarketDataProvider
{
    // --- Đã có ---
    Task<Ticker> GetTickerAsync(string symbol, CancellationToken ct = default);
    Task<IReadOnlyList<Candle>> GetCandlesAsync(string symbol, string interval, int limit = 100, CancellationToken ct = default);
    Task<SymbolPriceFilter?> GetPriceFilterAsync(string symbol, CancellationToken ct = default);

    // --- Mới (FR-003) ---
    Task<FundingSnapshot?> GetFundingAsync(string symbol, CancellationToken ct = default);
    Task<OpenInterestSeries?> GetOpenInterestHistAsync(string symbol, string period, int limit, CancellationToken ct = default);
    Task<LongShortRatio?> GetGlobalLongShortRatioAsync(string symbol, string period, CancellationToken ct = default);
    Task<DepthSnapshot?> GetDepthAsync(string symbol, int limit = 100, CancellationToken ct = default);
    Task<TakerFlow?> GetTakerBuySellRatioAsync(string symbol, string period, CancellationToken ct = default);
}
```

### Hợp đồng lỗi — khác với ba phương thức cũ

Năm phương thức mới trả **`null`** khi không lấy được dữ liệu; chúng **không ném ngoại lệ** cho lỗi mạng hay lỗi phía sàn.

Lý do: theo FR-006, thiếu dữ liệu ⟹ tiêu chí liên quan nhận 0 điểm và vòng chấm điểm **vẫn tiếp tục**. Nếu dùng ngoại lệ thì mỗi điểm gọi lại phải bọc `try/catch`, và chỉ cần quên một chỗ là cả chu kỳ đánh giá chết vì một nguồn dữ liệu phụ. Trả `null` làm cho hành vi đúng trở thành hành vi mặc định.

Lỗi vẫn được ghi log có cấu trúc kèm symbol — im lặng nuốt lỗi là vi phạm Nguyên tắc IV.

---

## Kiểu dữ liệu mới

```csharp
public sealed record FundingSnapshot(
    decimal LastFundingRate,
    DateTime NextFundingTimeUtc,
    decimal MarkPrice,
    DateTime RetrievedAtUtc);

public sealed record OpenInterestPoint(DateTime TimeUtc, decimal OpenInterest, decimal OpenInterestValue);

public sealed record OpenInterestSeries(
    string Symbol, string Period, IReadOnlyList<OpenInterestPoint> Points)
{
    public decimal? ChangePercent(TimeSpan window) => /* tính từ Points */;
}

public sealed record LongShortRatio(decimal LongShortRatioValue, decimal LongAccount, decimal ShortAccount, DateTime TimeUtc);

public sealed record DepthLevel(decimal Price, decimal Quantity);

public sealed record DepthSnapshot(
    IReadOnlyList<DepthLevel> Bids, IReadOnlyList<DepthLevel> Asks, DateTime RetrievedAtUtc)
{
    public decimal SpreadBps => /* (bestAsk - bestBid) / mid * 10000 */;
    public decimal DepthWithinBps(int bps, bool bidSide) => /* tổng khối lượng trong dải */;
}

public sealed record TakerFlow(decimal BuySellRatio, decimal BuyVolume, decimal SellVolume, DateTime TimeUtc);
```

---

## `Candle` — mở rộng

```csharp
public sealed record Candle(
    DateTime OpenTime, decimal Open, decimal High, decimal Low,
    decimal Close, decimal Volume, DateTime CloseTime);

public static class CandleExtensions
{
    /// <summary>Cắt bỏ nến chưa đóng ở cuối chuỗi. PHẢI gọi trước mọi phép tính chỉ báo (FR-001).</summary>
    public static IReadOnlyList<Candle> ClosedOnly(this IReadOnlyList<Candle> candles, IClock clock);
}
```

`IsClosed` được suy ra từ `clock.UtcNow >= CloseTime` chứ không lưu trữ — giữ cho `BinanceMarketDataProvider` và `ArchiveMarketDataProvider` hành xử giống hệt nhau (R-002).

---

## `IMarketSentimentProvider`

```csharp
public interface IMarketSentimentProvider
{
    /// <summary>Chỉ số tâm lý 0–100. Null khi không truy cập được (R-004).</summary>
    Task<int?> GetFearGreedIndexAsync(CancellationToken ct = default);
}
```

---

## Ràng buộc nguồn dữ liệu

| # | Ràng buộc | Nguồn |
|---|---|---|
| 1 | Không phương thức mới nào cần khoá truy cập tài khoản | FR-004, Nguyên tắc VII |
| 2 | Lỗi một symbol không chặn symbol còn lại | FR-050 |
| 3 | Nhóm `/futures/data/*` (OI, long/short, taker) chỉ có **30 ngày lịch sử**. **`/fapi/v1/fundingRate` thì có đủ ≥ 2 năm** | R-003 |
| 4 | Mọi lệnh gọi có timeout; hết timeout trả `null`, không treo chu kỳ | Hiệu năng |
| 5 | `ArchiveMarketDataProvider` cài **cùng interface**; trả `null` cho OI/long-short/depth, nhưng **phục vụ `GetFundingAsync` từ kho lịch sử phí vốn** | R-001, R-003 |
| 6 | `period` phải qua danh sách trắng `5m,15m,30m,1h,2h,4h,6h,12h,1d` ở phía client; sai thì **ném ngoại lệ**, không trả `null` | R-003 bẫy B1 |

Ràng buộc 6 là ngoại lệ duy nhất của hợp đồng "lỗi thì trả `null`", và có lý do: Binance trả HTTP 200 kèm mảng rỗng khi `period` sai. Nếu để nó đi theo đường `null` thì một lỗi đánh máy sẽ biến thành "thiếu dữ liệu ⟹ 0 điểm" và tiêu chí đó chết vĩnh viễn trong im lặng. `period` sai là lỗi lập trình, phải nổ ngay.

Ràng buộc 5 có hệ quả trực tiếp: kiểm thử lịch sử mất **10/100 điểm** (`liquidity.open_interest` 5 + `liquidity.spread_depth` 5), và điều này phải được ghi vào `BacktestRun.Limitations` — không giấu.
