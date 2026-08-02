# Contract — Lớp bối cảnh AI

**Namespace**: `MMW.Application.Ai`

> AI chỉ được **veto hoặc giảm**. Không bao giờ được tạo lệnh, chọn hướng, hay tăng kích thước.
> Ràng buộc này được cưỡng chế bằng **kiểu dữ liệu và số học**, không bằng lời nhắc trong prompt.

---

## `MarketContextApplier` — điểm cưỡng chế duy nhất

```csharp
public interface IMarketContextApplier
{
    /// <summary>
    /// Hệ số AI áp cho kích thước lệnh. Kết quả LUÔN nằm trong [0.0, 1.0].
    /// Không có bối cảnh, bối cảnh hết hạn, hoặc AI chết → trả về 1.0.
    /// </summary>
    decimal GetSizeMultiplier(IReadOnlyList<MarketContextRecord> activeContext, string symbol, TradeDirection direction);
}
```

Kiểu trả về là một số trong `[0, 1]` nhân vào kích thước. Không có đường dẫn nào trong hệ thống cho phép AI trả về giá trị lớn hơn 1 — **AI không thể làm lệnh to lên vì không tồn tại phép toán nào cho phép điều đó**, chứ không phải vì có một câu `if` chặn lại.

| Mức độ bối cảnh | Hệ số |
|---|---|
| Không có / hết hạn / AI chết | 1.0 |
| `noise`, `low` | 1.0 |
| `medium` | 0.75 |
| `high` | 0.5 |
| `critical` | 0.0 (veto hoàn toàn) |

Chỉ áp khi bối cảnh **liên quan tới symbol** và **ngược chiều lệnh**. Bối cảnh thuận chiều **không** làm tăng hệ số.

---

## `IMarketContextService`

```csharp
public interface IMarketContextService
{
    /// <summary>Bối cảnh còn hiệu lực: ExpiresAtUtc > clock.UtcNow (FR-044).</summary>
    Task<IReadOnlyList<MarketContextRecord>> GetActiveAsync(string symbol, CancellationToken ct = default);

    /// <summary>Chạy Daily Brief. Ghi vào DailyPlan.Ai* và có thể thêm ScheduledEvent Origin=AiDetected.</summary>
    Task<int> RunDailyBriefAsync(DailyPlan plan, CancellationToken ct = default);

    /// <summary>Phân loại các headline mới. Chỉ xử lý tin chưa có SourceKey trong kho.</summary>
    Task<int> ClassifyNewsAsync(CancellationToken ct = default);
}
```

---

## Prompt 1 — Daily Brief

Chạy 1 lần/ngày sau khi kế hoạch ngày tất định đã hoàn chỉnh.

**Đầu vào**: `providedCalendar[]` (từ `ScheduledEvent`), `recentHeadlines[]`, `marketStats{}` — toàn bộ do hệ thống cung cấp.

**Ràng buộc trong system prompt** (bắt buộc có đủ):

```
- CHỈ dùng sự kiện có trong providedCalendar. Sự kiện không có trong đó
  thì KHÔNG TỒN TẠI. Không được thêm từ trí nhớ.
- Không suy ra ngày/giờ. Mọi timestamp phải copy nguyên văn từ input.
- Không đề xuất long/short/entry/stopLoss/takeProfit. Không có ngoại lệ.
- Không chắc → hạ severity, không phải nâng.
- Toàn bộ văn bản hướng tới người dùng bằng tiếng Việt.
```

**Schema đầu ra**:

```json
{
  "dayRiskLevel": "low|normal|elevated|extreme",
  "narrative": "<300 ký tự, tiếng Việt",
  "extraBlackouts": [{"fromUtc": "ISO", "toUtc": "ISO", "reason": "...", "severity": "medium|high"}],
  "themes": ["..."],
  "symbolNotes": [{"symbol": "...", "caution": "..."}],
  "confidence": 0.0
}
```

**Kiểm chứng phía nhận** — mọi trường đều bị soi trước khi dùng:

| # | Kiểm tra | Vi phạm thì |
|---|---|---|
| 1 | `confidence` bị cắt về trần 0.8 | Cắt, ghi vết |
| 2 | Mỗi `extraBlackouts` phải **không** trùng sự kiện đã có trong `providedCalendar` | Loại bỏ |
| 3 | Độ dài mỗi cửa sổ ≤ `EngineSetting.AiBlackoutMaxMinutes` | Cắt về trần |
| 4 | `fromUtc < toUtc`, cả hai trong vòng 48 giờ tới | Loại bỏ |
| 5 | Xuất hiện bất kỳ khoá nào gợi ý lệnh (`entry`, `stopLoss`, `takeProfit`, `direction`, `side`, `action`) | Loại bỏ **toàn bộ phản hồi**, ghi vào `RejectedFields` |
| 6 | JSON không hợp lệ sau một lần thử sửa | Bối cảnh trung tính, vòng quyết định chạy bình thường |

Kiểm tra 5 nghiêm khắc có chủ ý: một phản hồi cố đưa ra tín hiệu giao dịch là dấu hiệu prompt đã trôi khỏi vai trò, và phần còn lại của phản hồi đó không đáng tin.

---

## Prompt 2 — News Classifier

Chạy mỗi 15 phút, **chỉ khi có headline mới** (`SourceKey` chưa có trong kho).

**Schema đầu ra**:

```json
{
  "severity": "noise|low|medium|high|critical",
  "affectedSymbols": ["BTCUSDT"],
  "leaning": "bullish|bearish|neutral",
  "halfLifeMinutes": 0,
  "isRumor": false
}
```

**Kiểm chứng phía nhận**:

| # | Kiểm tra | Vi phạm thì |
|---|---|---|
| 1 | `isRumor == true` ⟹ `severity` trần ở `medium` | Hạ cấp |
| 2 | `halfLifeMinutes` trong `[0, 1440]` | Cắt về biên |
| 3 | `affectedSymbols` lọc theo danh sách symbol đang theo dõi | Bỏ phần thừa |
| 4 | Không rõ ràng ⟹ `noise` | Mặc định an toàn |

---

## Ngân sách gọi (FR-046)

| Mục đích | Lần/ngày |
|---|---|
| Daily Brief | 1 |
| News Classifier | 0–20 (chỉ khi có tin mới) |
| Đường so sánh song song | ~4 (khi bật) |
| **Tổng** | **< 30** |

Vòng đánh giá cơ hội và vòng quản lý vị thế: **0** (FR-049).

---

## Test bắt buộc — lớp chống lạm quyền

Mỗi trường hợp dưới đây là một test riêng, tất cả nằm trong vùng bắt buộc test đỏ trước của Nguyên tắc VI:

```
1. AI trả kèm entry/stopLoss/takeProfit         → toàn bộ phản hồi bị loại, ghi RejectedFields
2. AI trả confidence = 0.99                      → cắt về 0.8
3. AI bịa một sự kiện không có trong input       → sự kiện bị loại, ghi vết
4. AI trả cửa sổ chặn dài 20 tiếng               → cắt về AiBlackoutMaxMinutes
5. AI trả JSON hỏng                              → bối cảnh trung tính, vòng quyết định chạy bình thường
6. AI trả chuỗi rỗng                             → như trên
7. ILlmService ném ngoại lệ                      → như trên
8. ILlmService.IsConfigured == false             → không gọi mạng, hệ số 1.0
9. Bối cảnh critical ngược chiều lệnh            → hệ số 0.0, lệnh bị veto
10. Bối cảnh critical THUẬN chiều lệnh           → hệ số 1.0, KHÔNG tăng
11. Bối cảnh đã hết hạn                          → hệ số 1.0
12. EnrichAsync với phản hồi vi phạm             → RiskMultiplier / MaxTradesToday /
                                                    AllowedDirections KHÔNG đổi
```

Trường hợp 10 là trường hợp dễ bị bỏ sót nhất và cũng là ranh giới thật của Nguyên tắc II: một bối cảnh lạc quan mạnh mẽ **không phải** lý do để vào lệnh to hơn. AI chỉ có một hướng tác động, và hướng đó là xuống.
