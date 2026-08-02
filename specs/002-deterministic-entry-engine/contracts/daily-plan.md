# Contract — Kế hoạch ngày

**Namespace**: `MMW.Application.Trading.DailyPlanning`

---

## `IDailyPlanService`

```csharp
public interface IDailyPlanService
{
    /// <summary>
    /// Sinh kế hoạch cho ngày UTC. BẤT BIẾN theo (tradingAccountId, planDate):
    /// gọi lại trong cùng ngày trả về bản đã có, KHÔNG ghi đè.
    /// </summary>
    Task<DailyPlan> GenerateAsync(long tradingAccountId, DateOnly planDateUtc, CancellationToken ct = default);

    /// <summary>Kế hoạch của ngày hiện tại. Null ⟹ mọi lệnh mới bị chặn (FR-023).</summary>
    Task<DailyPlan?> GetCurrentAsync(long tradingAccountId, CancellationToken ct = default);
}
```

**Không có phương thức trả kế hoạch mặc định.** Đây là chủ ý: FR-023 cấm dùng kế hoạch mặc định cho phép giao dịch, và cách chắc chắn nhất để cấm là không cung cấp đường dẫn nào tạo ra nó.

---

## `IDayRegimeClassifier` — phần tất định

```csharp
public interface IDayRegimeClassifier
{
    /// <summary>Hàm THUẦN. Không I/O, không đồng hồ. Toàn bộ đầu vào nằm trong inputs.</summary>
    RegimeClassification Classify(DailyPlanInputs inputs, EngineSetting settings);
}
```

```csharp
public sealed record DailyPlanInputs
{
    public required IReadOnlyList<Candle> BtcDailyCandles { get; init; }   // ≥ 90 nến đã đóng
    public required IReadOnlyList<Candle> SymbolDailyCandles { get; init; }
    public required IReadOnlyList<ScheduledEvent> TodayEvents { get; init; }

    // Null khi nguồn không khả dụng → ghi vào MissingInputs, KHÔNG làm hỏng việc sinh kế hoạch
    public decimal? FundingRate { get; init; }
    public decimal? OpenInterestChange24hPercent { get; init; }
    public decimal? LongShortAccountRatio { get; init; }
    public int?     FearGreedIndex { get; init; }
}

public sealed record RegimeClassification(
    DayRegime Regime,
    VolatilityRegime Volatility,
    AllowedDirections AllowedDirections,
    decimal RiskMultiplier,
    int MaxTradesToday,
    string BtcStructure,
    decimal? AtrPercentile,
    IReadOnlyList<string> MissingInputs);
```

---

## Thuật toán phân loại

### Bước 1 — cấu trúc giá (20 phiên gần nhất)

Dùng điểm xoay fractal theo R-007 với `SwingPivotBars` từ cấu hình:

- **TrendUp** — đỉnh xoay cuối > đỉnh xoay trước **và** đáy xoay cuối > đáy xoay trước
- **TrendDown** — đối xứng
- **Range** — mọi trường hợp còn lại

### Bước 2 — chế độ biến động

`ATR(14)` khung ngày chia giá đóng cửa, lấy phân vị so với 90 phiên (nearest-rank, R-009):

| Phân vị | Chế độ |
|---|---|
| < 25 | Low |
| 25–75 | Normal |
| 75–90 | High |
| > 90 | **Extreme** |

Dưới 60 phiên dữ liệu → `AtrPercentile = null`, chế độ mặc định `Normal`, ghi `MissingInputs`.

### Bước 3 — ánh xạ sang tham số (FR-019)

| Điều kiện | Chiều | Hệ số | Số lệnh |
|---|---|---|---|
| TrendUp + Normal | LongOnly | 1.0 | 5 |
| TrendDown + Normal | ShortOnly | 1.0 | 5 |
| Range + Low | Both | 0.5 | 3 |
| Bất kỳ + Extreme | Both | 0.3 | 2 |
| Có sự kiện tác động cao trong ngày | Both | 0.4 | 2 |

### Bước 4 — hợp nhất khi nhiều dòng cùng khớp (FR-020)

```
RiskMultiplier = MIN(tất cả dòng khớp)
MaxTradesToday = MIN(tất cả dòng khớp)
AllowedDirections = giao của tất cả dòng khớp
```

Ví dụ: TrendUp + Extreme + ngày có tin ⟹ `min(1.0, 0.3, 0.4) = 0.3`, `min(5, 2, 2) = 2`, `LongOnly ∩ Both ∩ Both = LongOnly`.

Lấy giá trị nhỏ nhất chứ không lấy dòng khớp đầu tiên, và giao thay vì hợp — cả hai đều nghiêng về phía thận trọng. Đây là quy tắc cần nhất quán tuyệt đối, vì nó chạy vào đúng những ngày nguy hiểm nhất.

### Bước 5 — phạt khi thiếu dữ liệu (FR-022)

```
MissingInputs không rỗng ⟹ RiskMultiplier = MIN(RiskMultiplier, 0.5)
```

Thiếu dữ liệu là lý do để thận trọng hơn, không bao giờ là lý do để nới ra.

---

## Bất biến — cưỡng chế bằng test

```
1. Cùng DailyPlanInputs → cùng RegimeClassification, 100% số lần
2. Volatility == Extreme ⟹ RiskMultiplier ≤ 0.3
3. MissingInputs.Any() ⟹ RiskMultiplier ≤ 0.5
4. GenerateAsync gọi hai lần cùng ngày ⟹ cùng DailyPlan.Id
5. AllowedDirections == None ⟹ MaxTradesToday == 0
6. Classify KHÔNG bao giờ ném ngoại lệ vì thiếu dữ liệu — trả kế hoạch thận trọng thay vì đổ vỡ
```

Bất biến 6 quan trọng hơn vẻ ngoài của nó: một ngoại lệ ở đây làm job kế hoạch ngày chết, để hệ thống không có kế hoạch, và theo FR-023 thì không có kế hoạch nghĩa là **cả ngày không giao dịch được**. Suy biến an toàn nhưng không mong muốn — nên tránh bằng cách không ném.

---

## Lớp AI (tuỳ chọn, chạy sau khi phần tất định xong)

```csharp
public interface IDailyBriefEnricher
{
    /// <summary>
    /// Làm giàu kế hoạch ĐÃ HOÀN CHỈNH. Chỉ ghi vào AiNarrative, AiDayRiskLevel,
    /// AiConfidence, và có thể THÊM ScheduledEvent với Origin = AiDetected.
    /// KHÔNG BAO GIỜ chạm RiskMultiplier, MaxTradesToday, hay AllowedDirections (FR-041).
    /// </summary>
    Task EnrichAsync(DailyPlan plan, CancellationToken ct = default);
}
```

Chữ ký nhận `DailyPlan` đã hoàn chỉnh chứ không tham gia vào việc tạo ra nó — ranh giới nằm ở kiểu dữ liệu, không nằm ở kỷ luật lập trình. Kèm một test khẳng định ba trường ràng buộc không đổi sau khi `EnrichAsync` chạy với phản hồi AI cố tình vi phạm.
