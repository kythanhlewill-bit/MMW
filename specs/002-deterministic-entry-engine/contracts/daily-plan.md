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
| *(dòng nền — luôn khớp)* | theo cấu trúc | 1.0 | 5 |
| TrendUp + Normal | LongOnly | 1.0 | 5 |
| TrendDown + Normal | ShortOnly | 1.0 | 5 |
| Range + Low | Both | 0.5 | 3 |
| Bất kỳ + Extreme | Both | 0.3 | 2 |
| Có sự kiện tác động cao trong ngày | Both | 0.4 | 2 |

**Dòng nền là bổ sung ở Phase 3–4, không có trong bản phác thảo.** Năm dòng của FR-019 không phủ hết tổ hợp cấu trúc × biến động: "TrendUp + High", "Range + Normal", "TrendDown + Low"… đều không khớp dòng nào, và "Range + Normal" là loại ngày rất thường gặp. Không có dòng nền thì những ngày ấy rơi vào trạng thái không xác định.

Chiều của dòng nền lấy theo CẤU TRÚC (`TrendUp → LongOnly`, `TrendDown → ShortOnly`, `Range → Both`), vì ràng buộc đã chốt với người dùng là **ngày trend chỉ vào một chiều thuận trend, không đánh ngược**. Để dòng nền cho "cả hai" thì ngày "tăng + biến động cao" sẽ mở cửa cho lệnh bán ngược xu hướng — đúng thứ bị cấm. Hệ số 1.0 và 5 lệnh là giá trị trung tính, đủ rộng để không lấn át các dòng siết bên dưới khi hợp nhất bằng `MIN`.

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

## Chênh lệch so với bản phác thảo (ghi khi hiện thực hoá ở Phase 4)

| Thay đổi | Lý do |
|---|---|
| Thêm enum `DayStructure { TrendUp, TrendDown, Range }` | `DayRegime` là NHÃN của cả ngày và có thể là `HighVolatility` hay `EventDay` — những giá trị không nói gì về cấu trúc giá. Trộn hai khái niệm làm mất thông tin cấu trúc đúng lúc cần nhất: ngày vừa có tin vừa đang trong xu hướng. |
| Tách `RegimeTable.Resolve` và `VolatilityBands.From` thành API công khai | Để bảng FR-019 và bốn vùng biến động kiểm thử được TRỰC TIẾP. Kiểm gián tiếp qua chuỗi nến là làm được nhưng mong manh: test đỏ vì bộ dữ liệu lệch chứ không phải vì bảng sai. Thêm một dòng bảng = thêm một phần tử (Nguyên tắc V). |
| Thêm dòng nền vào bảng | Xem mục Bước 3. |
| Thêm record `KeyLevels` + `KeyLevels.From` | FR-018 đòi mức giá tham chiếu nhưng `RegimeClassification` không có chỗ chứa; tính chúng là phép thuần nên tách riêng thay vì nhét vào service. |
| Thêm `DailyPlanGate.Check` | FR-021 và FR-023 cần một nơi cưỡng chế; chữ ký cố tình KHÔNG nhận điểm số để "bất kể điểm số" đúng vĩnh viễn. |
| Thêm `DailyPlanInputNames` | `MissingInputs` đi vào cơ sở dữ liệu, ra giao diện và bị test so khớp — gõ tay ba nơi thì sớm muộn lệch một nơi. |

### Ghi chú hiện thực

**Kế hoạch neo theo BTC.** `SymbolDailyCandles` nhận đúng chuỗi nến BTC: thực thể `DailyPlan` chỉ có MỘT bộ mức giá tham chiếu, nên kế hoạch là của cả ngày chứ không của từng mã, và BTC là mã dẫn dắt. Trường vẫn giữ riêng để sau này tách theo mã không phải đổi chữ ký.

**Chuỗi phân vị dựng bằng cách cắt đuôi.** Làm trơn Wilder là hồi quy khởi tạo ở đầu chuỗi, nên cắt phần đuôi không đụng vào phần đầu — giá trị tính được ở mỗi vị trí đúng bằng giá trị của một phép tính cuộn liên tục. Hệ quả cần biết khi viết test: bên trong một khối biên độ không đổi, ATR **đơn điệu**, nên chuỗi hai mức kết thúc ở mức thấp luôn cho phân vị ≈ 0 (giá trị hiện tại là nhỏ nhất chuỗi). Muốn dựng một ngày "bình thường" phải dùng ít nhất ba mức.

**Đừng so `RegimeClassification` bằng `==`.** `MissingInputs` là `IReadOnlyList` nên record so nó theo tham chiếu; hai lần phân loại cùng đầu vào sẽ báo "khác nhau" dù mọi giá trị đều trùng. Kiểm tính tất định bằng cách so từng thành phần.

**Job bù chỉ bù cho HÔM NAY.** Kế hoạch là bất biến, nên nếu khởi động lúc 08:00 mà sinh luôn kế hoạch ngày mai thì bản ấy dựa trên dữ liệu của nửa ngày trước và job 23:30 sẽ không bao giờ thay được nó nữa.

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
