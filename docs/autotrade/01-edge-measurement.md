# MMW — Hệ Đo Edge cho Autotrade Cá Nhân

> **Tài liệu số 01 của tuyến autotrade.** Đây là **cổng (gate)**: mọi công việc khác — sửa bug,
> tối ưu prompt, mở live — đều vô nghĩa cho tới khi trả lời được câu hỏi
> *"tín hiệu của tôi có tốt hơn tung đồng xu không?"*
>
> Ngày lập: **2026-07-31** · Repo: `D:/KYLT/MMW` · Người dùng: 1 · Quỹ thời gian: 15–20 h/tuần
>
> **CẢNH BÁO ĐỌC TRƯỚC:** Tài liệu này **không chứa bất kỳ kết quả backtest nào của MMW**,
> vì MMW **chưa từng được backtest**. Mọi con số ở đây thuộc đúng một trong ba loại:
> **(a)** đo trực tiếp từ Binance public API trong lúc soạn tài liệu (ghi `[đo 2026-07-31]`);
> **(b)** trích từ mã nguồn (`file.cs:dòng`);
> **(c)** phép tính minh hoạ có ghi rõ giả định đầu vào (ghi `(ước lượng)` hoặc `(giả định)`).
> Không có loại (d) "hệ thống này sẽ kiếm X%/tháng". Nếu bạn thấy con số nào không rơi vào
> (a)/(b)/(c), đó là lỗi tài liệu — hãy xoá nó.

---

## 0. Tóm tắt điều hành — 8 kết luận

| # | Kết luận | Bằng chứng |
|---|---|---|
| 1 | **~19% tín hiệu mà production sinh ra là "tín hiệu ma"** — chỉ tồn tại vì nến 1h chưa đóng; đến lúc nến đóng thì điều kiện biến mất. Mọi backtest trên nến đã đóng đo **một chiến lược khác** với chiến lược đang chạy. | Đo 124 giờ × 3 symbol, [đo 2026-07-31]. §3.1 |
| 2 | Bug SPOT-vs-PERP **không phá huỷ phép đo như lo ngại**, nhưng làm **sai đơn vị R khoảng 4,5–5,9%/lệnh** và là rủi ro thật ở đường live. Ưu tiên sửa vì rẻ + ảnh hưởng tiền thật, không phải vì nó vô hiệu hoá backtest. | Mô phỏng 931 case/symbol, [đo 2026-07-31]. §3.7 |
| 3 | **Funding KHÔNG phải chi phí lớn ở khung 1h.** Phí taker lớn hơn funding khoảng **45 lần** với lệnh giữ ~12h. Funding chỉ đáng kể khi giữ > 2–3 ngày hoặc trong pha funding cao. | Funding BTCUSDT 500 kỳ, [đo 2026-07-31]. §3.5 |
| 4 | **Chi phí giao dịch ăn ~0,118 R mỗi lệnh trên BTC.** Win-rate hoà vốn ở RR=2 là **37,7%**, không phải 33,3%. | Tính từ ATR đo được + giả định fee. §3.5 |
| 5 | **Edge dưới +0,10 R/lệnh là KHÔNG THỂ ĐO ĐƯỢC ở quy mô này** — cần ~1.390 lệnh. Chỉ edge ≥ +0,30 R mới đo xong trong ~155 lệnh. | Công thức cỡ mẫu. §4.2 |
| 6 | **Ở vốn $2.000, expectancy chỉ để trả hoá đơn LLM ($1.078/năm) là +0,108 R/lệnh — đúng bằng mức không đo được.** Ràng buộc ràng buộc nhất không phải chiến lược mà là **chi phí LLM**. | `docs/strategy/03-financial-analyst.md` + tính toán. §4.5 |
| 7 | **Forward-test trên testnet hiện đang HỎNG HOÀN TOÀN** — không phải chỉ sai size: `TradeResultSyncService.cs:134` đọc fills từ **mainnet** trong khi lệnh đặt lên **testnet**, nên lệnh **không bao giờ đóng** trong journal. | `LiveOrderService.cs:97` vs `TradeResultSyncService.cs:68,134` → `BinanceAccountProvider.cs:55–59`. §6.1 |
| 8 | **Có kho dữ liệu lịch sử miễn phí bao gồm cả symbol đã huỷ niêm yết** — `data.binance.vision` có **787 symbol USDT-M** all-time so với **529** đang giao dịch. Bỏ qua nó = mất 33% vũ trụ mẫu, toàn bộ là symbol đã chết. | S3 listing + `fapi/v1/exchangeInfo`, [đo 2026-07-31]. §3.2 |

**Khuyến nghị hành động một dòng:** chuyển `market-scan` sang **chỉ quyết định trên nến đã đóng**.
Việc này đồng thời (i) xoá bỏ 19% tín hiệu ma, (ii) làm production **trở nên backtest được**, và
(iii) cắt **91,7% số lượt gọi LLM của market-scan**. Một thay đổi, ba vấn đề lớn nhất.

---

## 1. Kiểm kê dữ liệu — cái gì THẬT SỰ có

### 1.1 Bảng nguồn dữ liệu

| Nguồn | File | Lưu gì | Tần suất ghi | Độ sâu lịch sử (ước lượng) | Tái dựng được quyết định vào lệnh? |
|---|---|---|---|---|---|
| `AiSignalScanRecord` | `src/MMW.Domain/Entities/AiSignalScanRecord.cs` | Symbol, Interval, ScannedAt, Price, Rsi, Ema20, Ema50, MacdHistogram, Atr, Status, Action, Score, Confidence, Entry, SL, TP, RR, RejectReason, AiReason, SystemPrompt, RequestJson, ResponseJson, RepairResponseJson | **Mỗi lượt quét mỗi symbol** — 288/ngày × N symbol | Migration `20260605154718_AddAiAndExchangeApiAudit` → **tối đa 56 ngày** tính đến 2026-07-31 | **CÓ, một phần.** Đủ để dựng lại *đầu vào* và *đầu ra* của quyết định. **Thiếu**: OHLC của nến tại thời điểm quét (chỉ có `Price` = close bán phần) |
| `IndicatorRecord` | `IndicatorRecord.cs` | Price, Rsi, Ema20, Ema50, Macd, MacdSignal, MacdHistogram, Atr, Bias, ScannedAt | Mỗi lượt quét mỗi symbol (`MarketScanService.cs:178`) | Migration `20260531031537_AddHistoryAndSignals` → **tối đa 61 ngày** | Có — nhưng trùng lặp gần hoàn toàn với `AiSignalScanRecord`, giá trị bổ sung thấp |
| `MarketSnapshot` | `MarketSnapshot.cs` | Như trên nhưng **upsert** (chỉ giữ bản mới nhất) | Ghi đè mỗi lượt quét | **Không có lịch sử** | Không |
| `TradeSignal` | `TradeSignal.cs` | Direction, Bias, Score, Entry, SL, TP, RR, Reason, CreatedAt | Chỉ khi AI ra tín hiệu **và** vượt các cổng lọc | Cùng 61 ngày | Có, nhưng **đã bị `ApplyAiLevels` sửa SL/TP** (`MarketScanService.cs:191`) → không phải giá trị AI gốc |
| `Trade` | `Trade.cs` | Entry/Exit/SL/TP, Quantity, Fee, RealizedPnl, RiskAmount, RiskPercent, PlannedRiskReward, **RMultiple**, Outcome, OpenedAt, ClosedAt | Mỗi lệnh | Từ 2026-05-31 | Có — đây là **sự thật mặt đất** duy nhất |
| `TradingDay` | `TradingDay.cs` | TradeCount, WinCount, LossCount, GrossProfit/Loss, NetPnl, MaxConsecutiveLosses | Theo ngày | — | Không dùng để đo edge: **không có expectancy, không có profit factor** |

### 1.2 Sáu lỗ hổng chặn phép đo — và mức nghiêm trọng

**(1) Không có khoá ngoại nào nối `AiSignalScanRecord` → `TradeSignal` → `Trade`.**
`grep "SignalId|TradeSignalId|AiSignalScanRecordId" src/MMW.Domain/Entities/*.cs` = **0 kết quả**.
Liên kết duy nhất là **chuỗi văn bản tiếng Việt** trong `Trade.Note`:

```csharp
// src/MMW.Application/Services/TradeService.cs:119
Note = $"Tạo từ đề xuất #{signal.Id} ({signal.Symbol} {signal.Direction})",
```

→ Muốn quy kết một lệnh về tín hiệu sinh ra nó, phải regex một câu tiếng Việt.
**Mức độ: chặn attribution, không chặn phép đo.** Vá bằng 1 cột `TradeSignalId` + migration (~2h).

**(2) `Trade.Source = TradeSource.Manual` ngay cả khi lệnh do hệ thống tự tạo.**

```csharp
// src/MMW.Application/Services/TradeService.cs:113
Source = TradeSource.Manual,
```

→ Không lọc được "lệnh do bot sinh" khỏi "lệnh tay". Nếu chủ dự án có vào lệnh tay trong cùng
tài khoản, **mọi thống kê edge đều bị nhiễm**. **Mức độ: nghiêm trọng nếu có trade tay.**

**(3) Prompt yêu cầu AI dùng EMA200 nhưng KHÔNG BAO GIỜ gửi EMA200.**
`grep -ri "ema200" src/` = **0 kết quả**. Nhưng `MarketScanService.cs:47–49` ghi rõ:
"LONG ưu tiên khi: Price > EMA50, EMA20 > EMA50, **EMA50 >= EMA200**".
Payload gửi đi (`MarketScanService.cs`, `technicalSnapshot`) chỉ có Price/Rsi/Ema20/Ema50/Macd/
MacdSignal/MacdHistogram/Atr — và `recentCandles` chỉ **24 nến** (`candles.TakeLast(24)`), không đủ
để LLM tự tính EMA200 (cần ≥ 200).
→ LLM hoặc bịa, hoặc lờ quy tắc 2. **Mức độ: làm hỏng khả năng diễn giải "vì sao AI quyết định vậy".**

**(4) Không lưu OHLC của nến tại thời điểm quét.** Chỉ có `Price`. Nghĩa là không tái dựng được
`High`/`Low` bán phần. Nhưng **`Atr` đã được lưu sẵn** → SL/TP vẫn tái dựng được.
**Mức độ: chấp nhận được.**

**(5) Không lưu kết quả cho tín hiệu KHÔNG được vào lệnh.** `Status = "Wait"`/`"Rejected"` có được
lưu (tốt, đúng Hiến chương IV), nhưng **không có gì ghi lại "nếu vào lệnh thì sẽ ra sao"**.
**Mức độ: đây chính là thứ công cụ đo hồi cứu sinh ra để lấp** — lấy klines về mô phỏng.

**(6) Không có `KlineBar` nào được lưu.** Mọi phép đo đều phải tải lại klines từ Binance.
**Mức độ: bắt buộc phải xây kho klines cục bộ, xem §5.**

### 1.3 Việc đầu tiên phải làm: ĐẾM DÒNG

Tôi không truy vấn được database. **Chưa đếm được số dòng thì mọi tranh luận về Đường A đều là
giả thuyết.** Chạy đúng 4 câu lệnh này trước khi làm bất cứ việc gì khác:

```sql
-- (1) Đường A có sống được không?
SELECT Status, COUNT(*) AS n, MIN(ScannedAt) AS tu, MAX(ScannedAt) AS den
FROM AiSignalScanRecords
GROUP BY Status ORDER BY n DESC;

-- (2) Bao nhiêu quyết định VÀO LỆNH thật sự (khác WAIT/Rejected)?
SELECT Symbol, Action, COUNT(*) AS n
FROM AiSignalScanRecords
WHERE Status = 'Accepted'
GROUP BY Symbol, Action;

-- (3) Hệ thống thực sự chạy bao nhiêu ngày liên tục?
SELECT CAST(ScannedAt AS date) AS ngay, COUNT(*) AS luot_quet
FROM AiSignalScanRecords
GROUP BY CAST(ScannedAt AS date) ORDER BY ngay;

-- (4) Có bao nhiêu lệnh ĐÃ ĐÓNG có RMultiple? (mẫu sự thật mặt đất)
SELECT COUNT(*) AS n, AVG(RMultiple) AS expectancy_R, MIN(ClosedAt), MAX(ClosedAt)
FROM Trades WHERE Status = 2 AND RMultiple IS NOT NULL;   -- 2 = Closed, kiểm lại enum
```

**Quy tắc quyết định ngay tại đây:**

| Kết quả câu (2) | Kết luận |
|---|---|
| < 30 dòng `Accepted` | **Đường A chết.** Bỏ hẳn, đi thẳng Đường B. |
| 30 – 150 dòng | Đường A chỉ dùng để **kiểm chứng engine**, không kết luận edge được (xem §4.2) |
| > 150 dòng | Đường A đáng chạy song song với B |

Ước lượng bi quan: commit cuối là **2026-06-01**, có **143 mục chưa commit**, migration
`AddAiAndExchangeApiAudit` ra ngày **2026-06-05** — tức bảng audit ra đời **sau** commit cuối.
Xác suất cao hệ thống chỉ chạy trên máy dev từng đợt ngắn. **Giả định làm việc: Đường A có ít mẫu.**

---

## 2. Hai đường đo — so sánh thẳng

### 2.1 Bảng so sánh

| Tiêu chí | **Đường A** — hồi cứu từ `AiSignalScanRecord` | **Đường B** — backtest `SignalGenerator` trên klines |
|---|---|---|
| Đo cái gì | Đúng thứ đang chạy production (AI + preflight) | Thành phần deterministic (`SignalGenerator.cs`) **không nằm trên luồng production** |
| Cỡ mẫu | **Chưa biết — phải đếm.** Trần lý thuyết: 56 ngày × 288 lượt × N symbol, nhưng chỉ đếm dòng `Accepted` | **Không giới hạn.** Archive có klines 1h từ 2020-01 cho BTCUSDT [đo 2026-07-31] |
| Tái lập được không | **KHÔNG.** LLM không tất định; prompt/model có thể đã đổi giữa chừng; không lưu version model | **CÓ, 100%.** Logic thuần số học (`SignalGenerator.cs:8–51`) |
| Chạy lại với tham số khác | **KHÔNG.** Không gọi lại LLM cho quá khứ được (và nếu gọi lại thì đó là lookahead vì model biết tương lai) | **CÓ.** Đổi `StopAtrMultiple`, `RewardRisk`, `minScore` tuỳ ý |
| Survivorship bias | Có — chỉ gồm symbol trong watchlist hiện tại (mặc định 4 symbol, `SeedData.cs:61–64`) | **Kiểm soát được** — dùng archive gồm cả symbol đã huỷ (§3.2) |
| Giá trị nếu kết quả TỐT | Cao — đó là thứ thật | Trung bình — chưa chắc production sinh ra thế |
| Giá trị nếu kết quả XẤU | Rất cao — biết ngay là dừng | Cao — biết lõi deterministic vô dụng |
| Công sức | ~7 người-ngày (dùng chung engine với B) | ~12 người-ngày |

### 2.2 Trả lời trực tiếp: làm cái nào trước?

**Làm cả hai, nhưng thứ tự là B → A, và có một bước 0 rẻ hơn cả hai đứng trước.**

Lý do, xếp theo sức nặng:

1. **Cả A và B đều cần cùng một engine.** Engine phát lại nến + mô phỏng chạm SL/TP + mô hình chi
   phí + tính metric là **giống hệt nhau**; chỉ khác *nguồn quyết định*. Thiết kế đúng là một
   interface `IDecisionSource` với 3 cài đặt: deterministic, đọc-từ-DB (Đường A), và ngẫu nhiên
   (đường chuẩn null). **Xây B trước = xây luôn phần lớn của A.** Xây A trước = xây một công cụ
   dùng một lần.

2. **A không trả lời được câu hỏi nếu kết quả xấu.** Giả sử A cho expectancy âm. Bạn không biết vì
   sao: do LLM tệ? do prompt? do lọc score? do EMA200 bị bịa (§1.2 mục 3)? **Không chạy lại được**
   nên không tách nguyên nhân được. B cho phép tách: chạy lõi deterministic riêng, biết ngay lõi có
   edge hay không, rồi mới hỏi "LLM có cộng thêm gì lên lõi không".

3. **Nếu lõi deterministic KHÔNG có edge và LLM là toàn bộ chiến lược — thì câu trả lời trung thực
   là "không thể biết trước khi bỏ tiền thật".** Đó là một kết luận cực kỳ quan trọng và chỉ B mới
   đưa ra được. Biết điều này sớm đáng giá hơn mọi thứ khác trong tài liệu này.

4. **B cho phép chạy đường chuẩn ngẫu nhiên (null baseline)** — xem §3.4. Đây là phép thử rẻ nhất và
   giết chết nhiều chiến lược nhất. A không có đường chuẩn.

**Nhưng trước cả B: Bước 0 — kiểm toán độ ổn định tín hiệu (~1 người-ngày).**

Đây là phép đo tôi đã chạy thử ngay trong lúc soạn tài liệu này (§3.1) và nó đã cho ra con số quan
trọng nhất trong toàn bộ tài liệu: **19% tín hiệu là tín hiệu ma**. Bước 0 không cần database, không
cần engine, không cần LLM — chỉ cần klines 1h + 5m và ~150 dòng code. Nó trả lời câu hỏi *"backtest
nến-đóng có đo đúng thứ production làm không?"* — và nếu câu trả lời là "không" (nó là "không"), thì
**phải sửa production trước, rồi mới backtest**. Backtest một thứ mà production không chạy là đốt
6 tuần vô ích.

### 2.3 Thứ tự cuối cùng

```
Bước 0  (1 ngày)   Kiểm toán độ ổn định tín hiệu  → quyết định nến-đóng hay nến-đang-chạy
   ↓
Bước 0b (0.5 ngày) Đếm dòng DB (§1.3)             → quyết định Đường A sống hay chết
   ↓
Bước 1  (4 ngày)   Kho klines PERP + funding      → nền cho mọi thứ
   ↓
Bước 2  (5 ngày)   Engine phát lại + chi phí + metric + đường chuẩn ngẫu nhiên
   ↓
Bước 3  (2 ngày)   Đường B: DeterministicDecisionSource → có edge ở lõi không?
   ↓
Bước 4  (2 ngày)   Đường A: RecordedAiDecisionSource    → LLM cộng thêm gì lên lõi?
   ↓
Bước 5             Cổng GO/NO-GO (§4) → forward-test (§6) hoặc dừng
```

---

## 3. Phương pháp luận — từng cạm bẫy, neo vào codebase

### 3.1 Lookahead bias — và cái bẫy NGƯỢC LẠI nguy hiểm hơn ở đây

**Lookahead bias kinh điển:** dùng thông tin tương lai để ra quyết định quá khứ. Ở MMW, nguy cơ này
nằm ở chỗ backtest sẽ dùng nến 1h **đã đóng** để quyết định, rồi vào lệnh tại `Close` của chính nến
đó — tức là bạn đã "biết" nến đóng ở đâu tại thời điểm giữa nến. Cách chặn chuẩn: quyết định trên
nến `i`, **vào lệnh tại `Open` của nến `i+1`**.

**Nhưng cái bẫy thật ở MMW là chiều ngược lại.** Production **không** dùng nến đã đóng:

```csharp
// src/MMW.Application/MarketData/MarketAnalyzer.cs:23-24
var closes = candles.Select(c => c.Close).ToList();
var price = closes.Count > 0 ? closes[^1] : 0m;    // ← nến CUỐI CÙNG
```

Và nến cuối cùng mà Binance trả về là **nến đang chạy**, đã xác nhận bằng đo trực tiếp:

> `GET https://fapi.binance.com/fapi/v1/klines?symbol=BTCUSDT&interval=1h&limit=2` trả về nến cuối
> có `openTime = 1785459600000` (01:00:00.000 UTC) và `closeTime = 1785463199999` (01:59:59.999 UTC);
> `GET /fapi/v1/time` cùng lúc trả `serverTime = 2026-07-31T01:12:59.023Z` — **nằm giữa hai mốc**.
> ⇒ Nến cuối là nến CHƯA ĐÓNG. [Binance fapi, đo 2026-07-31]

Job chạy `*/5` trên khung `1h` ⇒ **11 trong 12 lượt quét mỗi giờ rơi vào nến đang chạy**
(`Program.cs:129`). Câu hỏi: điều đó sai lệch bao nhiêu?

**Tôi đã đo.** Phương pháp: lấy klines 5m (1.500 nến ≈ 125 giờ) và klines 1h của cùng symbol từ
`fapi`; với mỗi giờ, dựng lại chuỗi close = [các nến 1h đã đóng] + [close 5m tại phút thứ m]; tính
`score` **đúng theo `MarketAnalyzer.cs:35–53`** (EMA20/EMA50 + MACD histogram, `IndicatorService.cs`
được cài lại nguyên văn); so với `score` tại lúc nến đóng. Ngưỡng phát tín hiệu `|score| ≥ 2`
(`AppSetting.cs:18` — `MinSignalScore = 2`).

| Chỉ số | BTCUSDT | ETHUSDT | SOLUSDT |
|---|---|---|---|
| Số giờ đánh giá | 124 | 124 | 124 |
| % giờ mà score **thay đổi ít nhất 1 lần** trong nến so với lúc đóng | **36,3%** | 28,2% | 27,4% |
| % mốc 5' có bias **NGƯỢC** với nến đóng | 1,8% | 2,1% | 1,0% |
| % giờ **phát tín hiệu** ở đâu đó trong nến | 54,0% | 65,3% | 46,8% |
| % giờ phát tín hiệu **tại lúc nến đóng** | 44,4% | 53,2% | 37,9% |
| **% giờ "tín hiệu ma"** (phát trong nến, mất khi đóng) | **9,7%** | **12,9%** | **8,9%** |
| **Tín hiệu ma / tổng tín hiệu đã phát** | **17,9%** | **19,8%** | **19,0%** |
| % giờ bỏ lỡ (không phát trong nến nhưng phát lúc đóng) | 0,0% | 0,8% | 0,0% |

[Tự đo từ Binance `fapi/v1/klines`, 2026-07-31. Caveat: dùng score deterministic của
`MarketAnalyzer`, **không** qua LLM — LLM nằm sau và thêm phi-tất-định riêng. Ngoài ra dùng 300 nến
1h làm bối cảnh trong khi production dùng 200 (`MarketScanService.cs:22`), gây sai lệch EMA nhỏ.]

**Diễn giải — đây là kết luận quan trọng nhất tài liệu:**

- **Khoảng 1 trong 5 tín hiệu mà production phát ra không tồn tại trên nến đã đóng.** Chúng là sản
  phẩm của việc hỏi "xu hướng thế nào?" khi nến mới chạy được 10 phút.
- Vì hệ thống quét **12 lần/giờ**, xác suất bắt được một cấu hình nhất thời trong giờ (54–65%) cao
  hơn hẳn xác suất cấu hình đó tồn tại lúc nến đóng (38–53%). **Quét nhiều hơn không tìm được nhiều
  tín hiệu hơn — nó tìm được nhiều nhiễu hơn.**
- Chống trùng hiện tại chỉ chặn theo *(symbol, hướng, giá xấp xỉ, đang mở)*
  (`MarketScanService.cs:477–486`) nên không chặn được hiện tượng này.

**Hệ quả bắt buộc — chọn một trong hai, không có lựa chọn thứ ba:**

| Phương án | Nội dung | Ưu | Nhược |
|---|---|---|---|
| **P1 (khuyến nghị)** | Sửa production: chỉ quyết định khi nến đã đóng. Bỏ qua lượt quét nếu `utcNow` chưa qua `closeTime` của nến cuối; hoặc luôn cắt bỏ nến cuối cùng khỏi mảng trước khi `Analyze` | Production **trở nên backtest được**; xoá 19% tín hiệu ma; **cắt 91,7% lượt gọi LLM của market-scan** (288/ngày → 24/ngày mỗi symbol) | Mất khả năng phản ứng trong giờ — nhưng ở khung 1h điều đó vốn không có ý nghĩa |
| **P2** | Giữ nguyên production, backtest phải mô phỏng chế độ **nến-đang-chạy** bằng dữ liệu 5m | Đo đúng thứ đang chạy | Engine phức tạp gấp đôi; dữ liệu 5m nặng gấp 12; **vẫn giữ 92% chi phí LLM**; vẫn giữ 19% tín hiệu nhiễu |

Công cụ ở §5 hỗ trợ **cả hai** qua tham số `DecisionMode` — nhưng nếu chọn P2 thì phải chấp nhận
+2 người-ngày và chi phí LLM không giảm.

**Ba quy tắc chống lookahead bắt buộc trong engine:**

1. Quyết định trên nến `i` (đã đóng) ⇒ **vào lệnh tại `Open[i+1]`**, không phải `Close[i]`.
2. Khi tải klines, **loại bỏ nến cuối** nếu `closeTime >= now` (nến chưa đóng).
3. Chỉ báo phải tính trên **cửa sổ trượt 200 nến kết thúc tại `i`**, giống production
   (`MarketScanService.cs:22` `CandleLimit = 200`), **không** tính trên toàn bộ lịch sử.
   Lý do cụ thể: `IndicatorService.EmaSeries` (`IndicatorService.cs:119–141`) seed EMA bằng SMA của
   `period` phần tử **đầu mảng được truyền vào** — nên EMA phụ thuộc điểm bắt đầu cửa sổ. Backtest
   dùng toàn bộ lịch sử sẽ ra EMA khác production. Đây là lỗi im lặng, rất khó phát hiện.

### 3.2 Survivorship bias

**Đo trực tiếp, [2026-07-31]:**

| Nguồn | Số symbol |
|---|---|
| `fapi/v1/exchangeInfo`, `contractType=PERPETUAL`, `quoteAsset=USDT`, `status=TRADING` | **529** |
| Cùng trên, mọi trạng thái | 652 |
| ...trong đó `onboardDate` cách đây ≥ 2 năm | **210** (39,7% của 529) |
| `data.binance.vision` `data/futures/um/monthly/klines/` — symbol kết thúc `USDT`, **all-time** | **787** |

⇒ **258 symbol USDT-M (32,8%) từng giao dịch nay đã biến mất.** Xác nhận có mặt trong archive nhưng
không còn trong `exchangeInfo`: `SRMUSDT`, `BTCSTUSDT`, `COCOSUSDT`, `TOMOUSDT`, `HNTUSDT`,
`RAYUSDT`, `CVCUSDT`, `FTTUSDT`, `LUNAUSDT`, `BNXUSDT`, `SCUSDT`, `ANTUSDT`.

⇒ Chỉ **39,7%** symbol đang giao dịch có ≥ 2 năm lịch sử. Backtest 3 năm trên "danh sách symbol hôm
nay" tự động loại 60% vũ trụ, và loại theo hướng thiên lệch (giữ lại kẻ sống sót).

**Watchlist hiện tại chỉ có 4 symbol** (`src/MMW.Web/Data/SeedData.cs:61–64`: BTCUSDT, ETHUSDT,
BNBUSDT, SOLUSDT) — cả bốn đều là blue-chip đã sống 5+ năm. Đây là mẫu **cực kỳ** thiên lệch.

**Cách chặn:**

- **Nếu chỉ định giao dịch 4 symbol này mãi mãi:** survivorship bias **không áp dụng** — bạn không
  chọn symbol dựa trên kết quả quá khứ, bạn cố định danh sách. Nhưng lúc đó cỡ mẫu bị giới hạn nặng
  (4 symbol × 1h → tối đa ~35.000 nến/năm tổng cộng) và mọi kết quả chỉ có giá trị cho 4 symbol này.
- **Nếu định mở rộng vũ trụ:** bắt buộc dùng archive `data.binance.vision` để lấy cả symbol đã chết,
  và **áp bộ lọc thanh khoản tại thời điểm t**, không phải hôm nay (ví dụ: quote volume 30 ngày
  trước `t` ≥ ngưỡng). Bộ lọc dùng dữ liệu hôm nay = lookahead + survivorship cùng lúc.
- **Ghi nhận trung thực trong báo cáo:** liệt kê rõ symbol nào bị loại vì thiếu dữ liệu, và bao
  nhiêu % nến bị mất.

### 3.3 Overfitting — bao nhiêu tham số là quá nhiều?

**Số tham số hiện tại của lõi deterministic:**

| Tham số | Giá trị | Nguồn |
|---|---|---|
| `StopAtrMultiple` | 1.5 | `SignalGenerator.cs:9` |
| `RewardRisk` | 2 | `SignalGenerator.cs:12` |
| ATR period | 14 | `MarketAnalyzer.cs:30` |
| EMA nhanh / chậm | 20 / 50 | `MarketAnalyzer.cs:27–28` |
| MACD | 12/26/9 | `IndicatorService.cs:57` |
| RSI period | 14 | `MarketAnalyzer.cs:26` (chỉ ghi chú, không vào score) |
| `MinSignalScore` | 2 | `AppSetting.cs:18` |
| `CandleLimit` | 200 | `MarketScanService.cs:22` |

**≈ 10 tham số tự do.** Đây đã là nhiều.

**Quy tắc ngón tay cái nghiêm khắc (giả định làm việc, không phải định lý):** cần **tối thiểu ~50
lệnh độc lập cho mỗi tham số được tối ưu**. 10 tham số ⇒ 500 lệnh chỉ để *biện minh* cho việc dò
tham số. Với 4 symbol khung 1h, 500 lệnh không dễ có.

**Nguyên tắc thực hành cho MMW:**

1. **KHÔNG tối ưu gì ở vòng đầu.** Chạy đúng bộ tham số production hiện tại. Đây là một phép thử
   **một giả thuyết**, không phải một cuộc dò tìm. Kết quả có thể diễn giải sạch.
2. Nếu vòng đầu cho kết quả gần ngưỡng, chỉ được phép quét **tối đa 2 tham số**, mỗi tham số
   **tối đa 5 giá trị** ⇒ 25 tổ hợp. Ghi lại **toàn bộ 25 kết quả**, không chỉ cái tốt nhất (§3.4).
3. **Cấm** thêm bộ lọc mới sau khi nhìn kết quả ("bỏ giờ Á", "chỉ giao dịch khi ATR > X"). Mỗi bộ
   lọc thêm sau khi nhìn dữ liệu là một bậc tự do ẩn không bao giờ được tính vào p-value.
4. **Kiểm tra bề mặt tham số:** nếu `StopAtrMultiple = 1.5` cho kết quả tốt mà `1.4` và `1.6` cho
   kết quả tệ ⇒ đó là **đỉnh nhọn** = nhiễu. Chỉ tin **cao nguyên** (một vùng liền kề đều tốt).

### 3.4 Multiple testing — thử 50 biến thể rồi chọn cái tốt nhất

**Vấn đề, giải thích cho dev:** nếu bạn thử 50 chiến lược **hoàn toàn vô dụng**, cái tốt nhất trong
50 cái vẫn trông rất đẹp. Với ngưỡng ý nghĩa 5%, kỳ vọng có **2,5 chiến lược "có ý nghĩa thống kê"**
thuần do may mắn. Nếu bạn chỉ báo cáo cái tốt nhất, bạn đang báo cáo may mắn.

**Ba lớp phòng vệ, dùng cả ba:**

**(a) Ghi nhật ký MỌI thử nghiệm.** Bảng `BacktestRun` phải ghi mọi lần chạy, kể cả lần bạn xoá đi
vì xấu. Không có nhật ký này thì không tính được hình phạt multiple-testing. Đây là điều kiện *bắt
buộc*, không phải "nên có".

**(b) Hiệu chỉnh Bonferroni.** Đã thử `k` biến thể ⇒ ngưỡng p phải là `0,05 / k`, không phải `0,05`.
Thử 25 tổ hợp ⇒ cần **p < 0,002**. Con số này khắc nghiệt một cách cố ý.

**(c) Deflated Sharpe Ratio.** Phương pháp chuẩn để hiệu chỉnh Sharpe theo số thử nghiệm, phương sai
giữa các thử nghiệm, độ dài mẫu, độ lệch và độ nhọn. Nguồn: Bailey & López de Prado, *"The Deflated
Sharpe Ratio: Correcting for Selection Bias, Backtest Overfitting and Non-Normality"*, *Journal of
Portfolio Management* 40(5), tr. 94 — PDF công khai tại `davidhbailey.com/dhbpapers/deflated-sharpe.pdf`
[truy cập 2026-07-31]. **Không bắt buộc cài đặt DSR đầy đủ ở vòng 1** (tốn thời gian, ít giá trị khi
mới thử 1 giả thuyết); bắt buộc nếu bạn đi tới vòng dò tham số.

**(d) Đường chuẩn ngẫu nhiên (null baseline) — rẻ nhất, mạnh nhất, LÀM TRƯỚC TIÊN.**
Chạy engine với `RandomDecisionSource`: cùng phân bố symbol, cùng phân bố thời điểm, cùng SL/TP theo
ATR, cùng mô hình chi phí — chỉ **hướng lệnh là ngẫu nhiên**. Lặp 1.000 lần ⇒ có phân bố expectancy
của "không có edge". Nếu expectancy của chiến lược không nằm ngoài **phân vị 99** của phân bố này,
**dừng lại**. Phép thử này bắt được gần như mọi lỗi engine (lookahead, mô hình chi phí sai, mô phỏng
chạm sai) và mọi ảo tưởng edge. **Chi phí: ~0,5 người-ngày sau khi engine xong. Bắt buộc.**

### 3.5 Fee, funding, slippage — bỏ qua là tự lừa

**Đo được:**

| Thành phần | Giá trị | Nguồn |
|---|---|---|
| ATR(14) trung bình trên 1h, % giá | BTCUSDT **0,568%** · SOLUSDT **0,876%** | [tự đo, 1.000 nến 1h, `fapi`, 2026-07-31] |
| Khoảng SL = 1,5 × ATR | BTC **0,852%** · SOL **1,314%** của giá | Tính từ `SignalGenerator.cs:9` |
| Funding BTCUSDT, 500 kỳ (2026-02-14 → 2026-07-31) | trung bình **+0,0014785%/8h**; trung bình trị tuyệt đối **0,0040544%/8h**; **64,2%** số kỳ dương | [`fapi/v1/fundingRate`, đo 2026-07-31] |
| Chu kỳ funding | **8 giờ** (kỳ kế tiếp 08:00 UTC) | [`fapi/v1/premiumIndex`, đo 2026-07-31] |
| Phí taker | **giả định 0,05%/chiều** ⇒ 0,10% khứ hồi | Trang `binance.com/en/fee/futureFee` **yêu cầu đăng nhập**, không lấy được số chính thức. Nguồn thứ cấp: tradersunion.com nói maker "khoảng 0,02%" [2025-11-14] — yếu. **Phải xác minh bằng `GET /fapi/v1/commissionRate?symbol=` (endpoint có ký, đã xác nhận tồn tại: trả `-2014 API-key format invalid` chứ không phải 404, [đo 2026-07-31])** |
| Slippage | **(ước lượng)** ~0,5–1 bp cho lệnh MARKET 50 USDT trên BTCUSDT perp | Không đo được nếu không đặt lệnh thật. **Phải đo bằng forward-test.** |

**Quy đổi sang đơn vị R** (R = khoảng cách SL × khối lượng; phí tính trên notional):

```
chi_phí_R = phí_khứ_hồi_% / khoảng_SL_%
BTC:  0,10% / 0,852% = 0,1174 R
SOL:  0,10% / 1,314% = 0,0761 R
```

**⇒ Riêng phí đã ăn ~11,7% của một đơn vị rủi ro trên BTC, mỗi lệnh.**

**Funding — hiệu chỉnh một hiểu lầm phổ biến (kể cả trong brief giao việc):**

| Thời gian giữ lệnh | Số kỳ funding | Chi phí ở mức TB đo được (0,00148%/8h) | Ở mức cao (0,01%/8h) |
|---|---|---|---|
| 12 giờ | 1,5 | 0,0022% notional = **0,0026 R** | 0,015% = 0,018 R |
| 24 giờ | 3 | 0,0044% = 0,0052 R | 0,03% = 0,035 R |
| 72 giờ | 9 | 0,0133% = **0,0157 R** | 0,09% = **0,106 R** |

**Kết luận trung thực:** ở khung 1h với SL 1,5 ATR / TP 3 ATR, phần lớn lệnh kết thúc trong vòng
48 nến; **phí taker lớn hơn funding khoảng 45 lần** ở thời gian giữ ~12h. Funding **có thật và phải
đưa vào mô hình** (Hiến chương IV yêu cầu ghi vết đầy đủ, và bỏ qua là tự lừa), nhưng nó **không
phải** là thứ giết chết chiến lược này. Thứ giết chiến lược này là **phí + tần suất vào lệnh**.
Funding chỉ trở nên quan trọng nếu (a) giữ lệnh nhiều ngày, hoặc (b) rơi vào pha funding cao —
và 64,2% số kỳ là dương, tức phe LONG là phe trả tiền phần lớn thời gian.

**Bắt buộc trong mô hình chi phí của engine:**

1. Phí: **taker cả hai chiều**. Entry là MARKET (`LiveOrderService.cs:265`), thoát bằng
   `STOP_MARKET`/`TAKE_PROFIT_MARKET` → cũng là taker. **Không được giả định maker.**
2. Funding: cộng dồn **theo từng kỳ 8h thực tế** giữa `entryTime` và `exitTime`, dùng
   `FundingRates` đã tải về, dấu theo hướng lệnh (LONG trả khi rate dương).
3. Slippage: tham số cấu hình, **mặc định 1 bp mỗi chiều**, và **phải chạy phân tích độ nhạy**
   ở 0 / 1 / 3 / 5 bp. Nếu kết luận đảo chiều giữa 1 bp và 3 bp ⇒ không có edge, chỉ có nhiễu.
4. **Chạm SL trong nến gap** phải khớp tại `Open` của nến gap, không phải tại giá SL — nếu không
   bạn đang giả định thanh khoản không tồn tại.

### 3.6 Out-of-sample / walk-forward với lịch sử ít ỏi

**Sự thật về độ sâu dữ liệu:**

- Klines PERP: `data.binance.vision` có `BTCUSDT-1h-2020-01.zip` ⇒ **≥ 6,5 năm** cho blue-chip
  [đo 2026-07-31]. Đây **không** phải ràng buộc.
- **Ràng buộc thật là số LỆNH, không phải số nến.** 6 năm × 4 symbol × 1h = 210.240 nến, nhưng nếu
  chiến lược vào ~1 lệnh/symbol/tuần ⇒ 6 × 52 × 4 ≈ **1.250 lệnh**. Đó là con số cần quan tâm.
- Dữ liệu `AiSignalScanRecord` (Đường A): **tối đa 56 ngày** ⇒ walk-forward là bất khả thi.
  Đường A chỉ có thể cho **một** phép đo in-sample duy nhất. Phải ghi rõ điều này khi báo cáo.

**Sơ đồ chia dữ liệu (Đường B):**

```
2020-01 ─────────────────────────────────────────────────────── 2026-07
├──────────── VÙNG KHOÁ (2020-01 → 2024-12, 60%) ─────────────┤
│  Dùng để: phát triển engine, sửa bug, kiểm tra sanity        │
│  KHÔNG được dùng để kết luận edge                            │
                                        ├─ OOS (2025-01 → 2026-07, 40%) ─┤
                                        │ MỞ ĐÚNG MỘT LẦN.               │
                                        │ Số liệu OOS là số liệu duy nhất │
                                        │ được đưa vào cổng GO/NO-GO §4  │
```

**Quy tắc thép:** vùng OOS mở **một lần**. Nếu bạn nhìn OOS, thấy xấu, chỉnh chiến lược, rồi nhìn
lại — **OOS đã chết**, nó trở thành in-sample. Không có cách nào cứu. Nếu buộc phải chỉnh, phải
**cắt một vùng OOS mới** từ tương lai (tức là forward-test, §6).

**Walk-forward trong vùng khoá** (để kiểm tính ổn định, không để kết luận):

| Fold | Train | Test |
|---|---|---|
| 1 | 2020-01 → 2021-06 | 2021-07 → 2021-12 |
| 2 | 2020-01 → 2021-12 | 2022-01 → 2022-06 |
| 3 | 2020-01 → 2022-06 | 2022-07 → 2022-12 |
| 4 | 2020-01 → 2022-12 | 2023-01 → 2023-06 |
| 5 | 2020-01 → 2023-06 | 2023-07 → 2023-12 |
| 6 | 2020-01 → 2023-12 | 2024-01 → 2024-06 |

Vì §3.3 nói **không tối ưu ở vòng đầu**, "train" ở đây chỉ dùng để làm nóng chỉ báo. Giá trị của
walk-forward là **tính nhất quán theo chế độ thị trường**: 2021 là bull, 2022 là bear, 2023 là
sideway, 2024 là bull. Một chiến lược chỉ lãi trong 2021 không phải chiến lược, đó là beta.

### 3.7 SPOT vs PERP — đo thật, và một kết luận đi ngược trực giác

**Bug đã xác nhận:**

```csharp
// src/MMW.Infrastructure/Exchanges/Binance/BinanceOptions.cs:8
public string MarketDataBaseUrl { get; set; } = "https://data-api.binance.vision";
// src/MMW.Infrastructure/Exchanges/Binance/BinanceMarketDataProvider.cs:44-45
$"/api/v3/klines?symbol={symbol}&interval={interval}&limit={limit}"   // ← SPOT
```

Chỉ báo và giá SL/TP tuyệt đối tính trên **SPOT**, lệnh đặt trên **PERP**.

**Đo mức độ nhiễm, 1.000 nến 1h khớp theo `openTime`, [2026-07-31]:**

| Chỉ số | BTCUSDT | ETHUSDT | SOLUSDT |
|---|---|---|---|
| Chênh lệch |close| trung bình spot↔perp | **4,44 bp** | 4,71 bp | **5,51 bp** |
| Chênh lệch |close| lớn nhất | 7,4 bp | — | 10,3 bp |
| Chênh lệch râu nến (high/low) lớn nhất | **120 bp** | — | **212 bp** |
| **Sai số đơn vị R** = |entry_spot − giá_perp| / (1,5 × ATR_perp) | **5,9%** | 4,5% | 4,7% |
| **% case đảo kết quả** (SL/TP tính trên spot, khớp trên perp, vs. toàn perp) | **1,9%** | 2,1% | **2,3%** |

*(Mô phỏng: 931 case/symbol, long, SL = 1,5 ATR, TP = 3 ATR, horizon 48 nến. Đây là **chỉ số chất
lượng dữ liệu**, KHÔNG phải kết quả backtest — không có số win-rate hay PnL nào ở đây.)*

**Kết luận trung thực, đi ngược trực giác ban đầu:** bug spot/perp **không** làm mọi phép đo trở nên
vô nghĩa. Nó đảo kết quả ở ~2% số case và làm sai đơn vị R khoảng 5%. Với edge mục tiêu ≥ 0,20 R
(§4), sai số 5% trên trục R **không đủ để lật kết luận**.

**Nhưng vẫn phải sửa, vì ba lý do khác:**

1. **Rẻ.** Đổi endpoint sang `fapi/v1/klines` + `FuturesApiBaseUrl` — ~2 giờ.
2. **Râu nến lệch tới 212 bp** ⇒ ở các lệnh mà SL nằm gần râu, sai lệch **không** nhỏ. Trung bình
   che giấu đuôi phân bố.
3. **Ở đường live nó là tiền thật.** SL/TP tính trên giá spot rồi đặt lên sổ lệnh perp ⇒ khoảng cách
   SL thực tế khác khoảng cách đã tính ⇒ `RiskAmount` (`Trade.cs:40`) và `RiskPercent` sai ⇒ toàn bộ
   Rule Engine chấm sai. **Đây mới là lý do phải sửa trước khi mở live.**

**Bắt buộc:** engine backtest **không được** dùng `IMarketDataProvider` hiện tại. Phải có port riêng
`IHistoricalKlineProvider` trỏ thẳng `fapi` (§5.3).

### 3.8 Mơ hồ trong nến (intrabar ambiguity)

**Vấn đề:** nếu trong cùng một nến 1h, giá chạm cả SL lẫn TP, dữ liệu OHLC **không cho biết cái nào
đến trước**. Backtest ngây thơ thường giả định TP trước (lạc quan) hoặc SL trước (bi quan) — chênh
lệch giữa hai giả định có thể lớn hơn toàn bộ edge.

**Đo thật:** với SL = 1,5 ATR và TP = 3 ATR trên khung 1h, **0/931 case** ở BTCUSDT và SOLUSDT có
nến chạm cả hai [đo 2026-07-31]. Lý do: cần một nến 1h có biên độ ≥ 4,5 ATR ≈ 2,6% (BTC) — hiếm.

**Kết luận:** ở cấu hình SL/TP hiện tại, mơ hồ trong nến **không phải vấn đề**. Nhưng:

1. Engine **vẫn phải** đánh dấu case mơ hồ là `Ambiguous` và **báo cáo số lượng**, không âm thầm chọn
   một phía. Nếu sau này ai đó thu hẹp TP hoặc chuyển sang khung 15m, con số này sẽ khác ngay.
2. Nếu số case mơ hồ vượt **2%** tổng số lệnh ⇒ **bắt buộc** chuyển sang dữ liệu 5m để phân định thứ
   tự chạm. Đây là một cổng kiểm tra tự động trong engine, không phải quyết định thủ công.
3. Quy ước tạm khi mơ hồ mà chưa có 5m: **luôn tính là SL** (bi quan). Không bao giờ tính là TP.

### 3.9 Các cạm bẫy còn lại — bảng ngắn

| Cạm bẫy | Biểu hiện ở MMW | Cách chặn |
|---|---|---|
| **Nhiễm mẫu do lệnh tay** | `Trade.Source = Manual` cho cả lệnh bot (`TradeService.cs:113`) | Thêm cột `TradeSignalId`; lọc mẫu theo cột đó, **không** theo `Source` |
| **Lệnh chồng lấn không độc lập** | Nhiều lệnh cùng symbol, cùng lúc | Với kiểm định thống kê, chỉ dùng lệnh **không chồng thời gian trên cùng symbol**; báo cáo cả hai cỡ mẫu (thô và độc lập) |
| **Job chạy chồng** | Không job nào có `DisableConcurrentExecution` (`Program.cs:126–156`) | Không ảnh hưởng backtest, nhưng làm hỏng forward-test (lệnh trùng). Phải sửa trước §6 |
| **Ngày giao dịch theo UTC** | `TradingDayService.cs:36` | Giới hạn "5 lệnh/ngày" reset lúc 07:00 giờ VN. Backtest phải **mô phỏng đúng ranh giới UTC** để khớp production, rồi mới bàn có nên đổi không |
| **Số dư thay đổi làm size thay đổi** | `TradeService.cs:100–104` size theo `account.CurrentBalance` | Backtest phải dùng **R cố định** (mọi lệnh rủi ro đúng 1 R) để tách edge khỏi hiệu ứng compounding. Compounding tính riêng ở bước dựng đường vốn |
| **Sinh tồn của chính bot** | Bot dừng khi thua ⇒ mẫu bị cắt cụt | Backtest phải mô phỏng cả `MaxDailyLossPercent = 3%` và `MaxTradesPerDay = 5` (`RiskSetting.cs`) — vì chúng **thay đổi phân bố kết quả**, không chỉ giảm số lệnh |

---

## 4. Ngưỡng quyết định — con số cụ thể

### 4.1 Ngưỡng hoà vốn: phải thắng bao nhiêu % mới không lỗ?

Với RR = 2 (`SignalGenerator.cs:12`) và chi phí 0,118 R (BTC, §3.5):

```
E[R] = p × 2 − (1 − p) × 1 − 0,118 = 3p − 1,118
E[R] = 0  ⇒  p = 37,27%
```

Cộng slippage 1 bp/chiều (≈ 0,0023 R) ⇒ **p_hoà_vốn ≈ 37,3%**.
Nếu slippage là 5 bp/chiều (alt thanh khoản kém) ⇒ chi phí 0,13 R ⇒ **p_hoà_vốn ≈ 37,7%**.

**Ghi nhớ:** con số "33,3%" mà mọi bài viết RR-2 hay nhắc **là sai** khi có phí. Chênh 4 điểm phần
trăm win-rate là chênh rất nhiều.

### 4.2 Cỡ mẫu tối thiểu — con số quyết định mọi thứ

**Giải thích cho dev (không cần biết thống kê):** bạn đang hỏi *"tôi cần bao nhiêu lệnh để phân biệt
'chiến lược này có edge' với 'tôi may mắn'?"*. Câu trả lời phụ thuộc **edge to bao nhiêu** và
**kết quả dao động thế nào**. Edge càng nhỏ, dao động càng lớn ⇒ cần càng nhiều lệnh.

```
n ≥ (z_α + z_β)² × (σ_R / E_R)²
```

- `z_α = 1,645` (mức ý nghĩa 5%, một phía) · `z_β = 0,8416` (power 80%) ⇒ `(z_α + z_β)² = 6,183`
- `σ_R` = độ lệch chuẩn R mỗi lệnh. **(giả định) σ_R = 1,5** — suy ra từ hệ RR=2 với p≈0,40:
  `var = 0,4×(2−0,2)² + 0,6×(−1−0,2)² = 2,16` ⇒ `σ = 1,47`. Engine phải **đo lại σ_R thật** và tính lại bảng này.

| Expectancy thật `E_R` | Số lệnh cần | Ở 2 lệnh/ngày | Ở 500 lệnh/năm |
|---|---|---|---|
| +0,05 R | **5.565** | 7,6 năm | 11,1 năm |
| +0,10 R | **1.391** | 1,9 năm | 2,8 năm |
| +0,20 R | **348** | 5,7 tháng | 8,4 tháng |
| **+0,30 R** | **155** | **2,6 tháng** | 3,7 tháng |
| +0,50 R | 56 | 28 ngày | 1,3 tháng |
| +0,75 R | 25 | 13 ngày | 18 ngày |

**Đây là bảng quan trọng nhất trong tài liệu.** Nó nói:

- **Edge nhỏ là không thể quản lý được ở quy mô cá nhân.** Nếu chiến lược của bạn có edge thật
  +0,08 R, bạn sẽ **không bao giờ chứng minh được** trong đời sống của dự án này. Bạn sẽ chỉ thấy
  nhiễu và tự thuyết phục mình theo cả hai hướng.
- **Chỉ đi tiếp nếu backtest cho E_R ≥ +0,30 R.** Không phải vì +0,20 R là xấu, mà vì +0,20 R
  **không phân biệt được với 0** trong khung thời gian bạn có.
- Đường A với < 150 mẫu ⇒ chỉ phát hiện được edge ≥ +0,30 R. Với < 56 mẫu ⇒ chỉ ≥ +0,50 R.
  **Một đường A với 40 mẫu và expectancy +0,25 R không nói lên bất cứ điều gì.**

### 4.3 Bảng cổng GO / NO-GO

Áp dụng cho **kết quả OOS** (§3.6), sau khi trừ **toàn bộ** chi phí (§3.5):

| # | Tiêu chí | GO | VÙNG XÁM | STOP |
|---|---|---|---|---|
| 1 | Số lệnh độc lập (không chồng thời gian) | ≥ 200 | 100–199 | < 100 |
| 2 | **Expectancy E_R** sau chi phí | **≥ +0,30 R** | +0,15 → +0,30 R | **< +0,15 R** |
| 3 | **Profit Factor** = tổng lãi / tổng lỗ | ≥ 1,40 | 1,15 – 1,40 | < 1,15 |
| 4 | Cận dưới khoảng tin cậy 95% (bootstrap) của E_R | **> 0** | chạm 0 | < 0 |
| 5 | **Permutation test** p-value | **< 0,01** | 0,01 – 0,05 | > 0,05 |
| 6 | So với **đường chuẩn ngẫu nhiên** (§3.4d) | ngoài phân vị 99 | phân vị 95–99 | trong phân vị 95 |
| 7 | **Max drawdown** (đơn vị R, peak-to-trough) | ≤ 12 R | 12 – 20 R | > 20 R |
| 8 | Nhất quán walk-forward (§3.6) | E_R > 0 ở ≥ 5/6 fold | 4/6 | ≤ 3/6 |
| 9 | Tập trung theo symbol | không symbol nào > 40% tổng R | 40–60% | > 60% |
| 10 | Độ nhạy slippage (0 / 1 / 3 / 5 bp) | GO ở cả 4 mức | đảo ở 5 bp | đảo ở 3 bp |
| 11 | Tỷ lệ case mơ hồ trong nến (§3.8) | < 1% | 1–2% | > 2% (bắt buộc chuyển 5m) |

**Quy tắc kết hợp:**
- **GO** ⇒ chuyển sang forward-test (§6). Không chuyển thẳng sang tiền thật quy mô lớn.
- **Bất kỳ tiêu chí nào STOP** ⇒ **dừng**, không "chỉnh một chút rồi chạy lại" (§3.6 quy tắc thép).
- **Toàn bộ trong VÙNG XÁM** ⇒ coi như STOP. Vùng xám nghĩa là "dữ liệu không đủ để phân biệt bạn
  với người tung đồng xu" — đó là câu trả lời, không phải sự thiếu quyết đoán.

### 4.4 Hai kiểm định thống kê — giải thích cho dev

**(a) Permutation test (kiểm định hoán vị) — không cần giả định phân phối.**

*Ý tưởng:* nếu chiến lược không có edge, thì việc nó vào LONG hay SHORT tại mỗi thời điểm là vô
nghĩa — đảo ngẫu nhiên hướng của từng lệnh sẽ không làm kết quả tệ đi.

```
1. Tính E_R thật của chiến lược          → E_thật
2. Lặp 10.000 lần:
     - giữ nguyên thời điểm & symbol & khoảng SL/TP của mọi lệnh
     - đảo hướng (long/short) ngẫu nhiên từng lệnh
     - mô phỏng lại, tính E_R              → E_hoán_vị[i]
3. p-value = (số lần E_hoán_vị ≥ E_thật + 1) / (10.001)
```

`p < 0,01` nghĩa là: nếu chiến lược thực sự vô dụng, xác suất thấy kết quả tốt như thế này là dưới
1%. Cài đặt: ~80 dòng C#. **Ưu điểm so với t-test: không giả định phân phối chuẩn**, mà phân phối
R-multiple thì rất lệch (nhiều −1, ít +2).

**(b) Bootstrap khoảng tin cậy cho E_R.**

```
1. Có mảng R[] gồm n R-multiple
2. Lặp 10.000 lần: lấy mẫu có hoàn lại n phần tử từ R[], tính trung bình → M[i]
3. Sắp M[], lấy phân vị 2,5% và 97,5% → khoảng tin cậy 95%
```

Nếu cận dưới ≤ 0 ⇒ **dữ liệu không loại trừ được khả năng expectancy bằng 0**. Cài đặt: ~30 dòng.

**Lưu ý bắt buộc cho cả hai:** nếu có lệnh chồng lấn (cùng symbol, trùng thời gian), phải bootstrap
theo **khối** (block bootstrap, khối = 1 tuần) chứ không theo từng lệnh, nếu không p-value sẽ lạc
quan giả tạo.

### 4.5 Ngưỡng nhạy với quy mô vốn — phần bị bỏ sót nhiều nhất

Chi phí LLM ước tính **$1.078/năm** ở cấu hình hiện tại
[`docs/strategy/03-financial-analyst.md`, dự án nội bộ].
Đây là **chi phí cố định**, không co giãn theo vốn. Vì thế:

| Vốn | LLM cost / vốn / năm | Rủi ro 1%/lệnh | R cần/năm để trả LLM | **Expectancy cần** (500 lệnh/năm) |
|---|---|---|---|---|
| **$2.000** | **53,9%** | $20 | 53,9 R | **+0,108 R/lệnh** |
| $5.000 | 21,6% | $50 | 21,6 R | +0,043 R/lệnh |
| $10.000 | 10,8% | $100 | 10,8 R | +0,022 R/lệnh |
| $20.000 | 5,4% | $200 | 5,4 R | +0,011 R/lệnh |

**Đối chiếu với §4.2 — đây là phát hiện nghiêm trọng:**

> Ở vốn **$2.000**, expectancy chỉ để **hoà vốn hoá đơn LLM** là **+0,108 R/lệnh**.
> Theo bảng §4.2, để **chứng minh** một edge +0,10 R cần **1.391 lệnh ≈ 2,8 năm**.
>
> ⇒ **Ở đầu thấp của dải vốn, ngưỡng hoà vốn chi phí nằm đúng ở vùng không thể đo được.**
> Bạn không thể biết mình có đang trả tiền cho một thứ vô dụng hay không, và câu hỏi đó
> sẽ mất gần 3 năm để trả lời.

**Đây không phải lý do bỏ cuộc — nó là lý do đổi thứ tự ưu tiên.** Ràng buộc ràng buộc nhất không
phải chiến lược, mà là **chi phí LLM**. Cắt chi phí xuống trước, rồi mới đi đo edge.

**Cắt bằng cách nào — con số cụ thể từ codebase:**

| Job | Cron hiện tại | Lượt/ngày | Đề xuất | Lượt/ngày sau | Giảm |
|---|---|---|---|---|---|
| `market-scan` (`Program.cs:129`) | `*/5` | 288 × N symbol = **1.152** (N=4) | Chỉ chạy khi nến 1h vừa đóng (§3.1 P1) | 24 × 4 = **96** | **−91,7%** |
| `trade-advisor` (`Program.cs:142`) | `*/1` (comment nói "mỗi 3 phút" — lệch 3 lần) | **1.440** × số lệnh mở | `*/15` — ở khung 1h, tư vấn mỗi phút là vô nghĩa | **96** | **−93,3%** |
| **Tổng (1 lệnh mở)** | | **2.592** | | **192** | **−92,6%** |

Chi phí LLM tỉ lệ gần tuyến tính với số lượt gọi ⇒ **$1.078/năm → khoảng $80–90/năm (ước lượng,
giả định giá token không đổi và độ dài prompt không đổi)**. Ở vốn $2.000, gánh nặng rơi từ **53,9%
→ khoảng 4,4%/năm**, và expectancy cần để hoà vốn LLM rơi từ +0,108 R → **khoảng +0,009 R/lệnh** —
tức là gần như không còn là ràng buộc.

**Và phần đẹp nhất: thay đổi để cắt 91,7% chi phí market-scan CHÍNH LÀ thay đổi để xoá 19% tín hiệu
ma và làm production trở nên backtest được (§3.1).** Một thay đổi, ba lợi ích lớn nhất.

---

## 5. Đặc tả công cụ — đủ để một dev .NET ngồi vào làm

### 5.1 Nguyên tắc thiết kế

1. **Engine thuần, không HTTP.** Toàn bộ logic phát lại nằm ở `MMW.Application/Backtest/` — nhận
   `IReadOnlyList<Candle>` và `IReadOnlyList<FundingPoint>`, trả kết quả. Test được không cần mạng.
   Tuân Hiến chương V (Application không tham chiếu SDK sàn).
2. **`IDecisionSource` là điểm cắm.** Đường A, Đường B và đường chuẩn ngẫu nhiên là **ba cài đặt của
   cùng một interface**. Không viết ba engine.
3. **Không dùng lại `IMarketDataProvider`** — nó trỏ SPOT (§3.7).
4. **Mọi lần chạy đều được ghi vào DB**, kể cả lần xấu (§3.4a).

### 5.2 Cấu trúc project

```
src/MMW.Application/Backtest/
  IHistoricalKlineProvider.cs      // port: GetAsync(symbol, interval, from, to) → IReadOnlyList<Candle>
  IHistoricalFundingProvider.cs    // port: GetAsync(symbol, from, to)          → IReadOnlyList<FundingPoint>
  IDecisionSource.cs               // Decision? Decide(BacktestBarContext ctx)
  Sources/
    DeterministicDecisionSource.cs // bọc MarketAnalyzer + SignalGenerator  → ĐƯỜNG B
    RecordedAiDecisionSource.cs    // đọc AiSignalScanRecord (Status='Accepted') → ĐƯỜNG A
    RandomDecisionSource.cs        // đường chuẩn null (§3.4d)
  BacktestEngine.cs                // vòng lặp phát lại nến
  CostModel.cs                     // fee + funding + slippage → R
  ExitSimulator.cs                 // mô phỏng chạm SL/TP, gắn cờ Ambiguous
  MetricsCalculator.cs             // E_R, PF, win rate, max DD (R), σ_R, Sharpe/lệnh
  PermutationTester.cs             // §4.4a
  BootstrapCi.cs                   // §4.4b
  Models/ (BacktestConfig, BacktestBarContext, Decision, SimulatedTrade, BacktestReport)

src/MMW.Infrastructure/Exchanges/Binance/
  BinanceHistoricalKlineProvider.cs   // adapter → fapi.binance.com  (KHÔNG dùng data-api spot)
  BinanceFundingProvider.cs           // adapter → fapi/v1/fundingRate
  BinanceVisionArchiveClient.cs       // tải zip từ data.binance.vision (nạp khối lượng lớn)

src/MMW.Domain/Entities/
  KlineBar.cs        // Symbol, Interval, Venue, OpenTime, O,H,L,C,V, CloseTime
  FundingPoint.cs    // Symbol, FundingTime, Rate
  BacktestRun.cs     // Id, RunAt, ConfigJson, GitSha, Notes, + toàn bộ metric
  BacktestTrade.cs   // BacktestRunId, Symbol, Direction, EntryTime, EntryPrice, SL, TP,
                     // ExitTime, ExitPrice, ExitReason, GrossR, FeeR, FundingR, SlippageR, NetR, IsAmbiguous

src/MMW.Backtest/    // console app net8.0, tham chiếu Application + Infrastructure
  Program.cs         // CLI

tests/MMW.RuleEngine.Tests/
  BacktestEngineTests.cs · CostModelTests.cs · ExitSimulatorTests.cs · PermutationTesterTests.cs
```

**Migration EF Core** cho 4 entity mới (Hiến chương: "Mọi thay đổi lược đồ đi qua migration EF Core").

**Chỉ mục bắt buộc:**
```csharp
builder.HasIndex(x => new { x.Symbol, x.Interval, x.Venue, x.OpenTime }).IsUnique();  // KlineBar
builder.HasIndex(x => new { x.Symbol, x.FundingTime }).IsUnique();                     // FundingPoint
builder.HasIndex(x => x.BacktestRunId);                                                // BacktestTrade
```

### 5.3 Lấy klines lịch sử — ĐÚNG endpoint

**Hai nguồn, dùng cả hai:**

**(a) REST — cho lượng nhỏ / cập nhật gần đây**

```
GET https://fapi.binance.com/fapi/v1/klines
    ?symbol=BTCUSDT&interval=1h&startTime={ms}&limit=1500
```

- **`fapi`, KHÔNG phải `api/v3`, KHÔNG phải `data-api.binance.vision/api/v3`.**
- **`limit` tối đa = 1500** — xác nhận: `limit=2000` trả `{"code":-1130,"msg":"Data sent for
  parameter 'limit' is not valid."}` [đo 2026-07-31].
- Phân trang: `startTime = openTime_nến_cuối + interval_ms`, lặp tới khi trả về < 1500 dòng.
- **Loại bỏ nến cuối nếu `closeTime >= serverTime`** (§3.1 quy tắc 2) — nếu quên, bạn nạp một nến
  chưa đóng vào kho lịch sử và mọi backtest sau đó bị nhiễm vĩnh viễn.
- Tôn trọng header `X-MBX-USED-WEIGHT-1M`; gặp HTTP 429/418 phải backoff (418 = đã bị ban tạm thời).
- Định dạng mảng: `[openTime, open, high, low, close, volume, closeTime, ...]` — parser hiện có
  `BinanceParser.ParseKlines` (`BinanceParser.cs:18–35`) **dùng lại được nguyên vẹn**.

**(b) Archive S3 — cho nạp khối lượng lớn và cho symbol đã huỷ niêm yết**

```
https://data.binance.vision/data/futures/um/monthly/klines/{SYMBOL}/{INTERVAL}/{SYMBOL}-{INTERVAL}-{YYYY}-{MM}.zip
ví dụ: .../klines/BTCUSDT/1h/BTCUSDT-1h-2020-01.zip     (37.271 byte)
liệt kê: https://s3-ap-northeast-1.amazonaws.com/data.binance.vision?delimiter=/&prefix=data/futures/um/monthly/klines/
```

- Xác nhận [đo 2026-07-31]: **935 symbol** trong archive (**787** kết thúc `USDT`), có từ **2020-01**,
  **bao gồm cả symbol đã huỷ niêm yết** (SRMUSDT, FTTUSDT, LUNAUSDT, TOMOUSDT, ...). Đây là cách
  **duy nhất** chặn survivorship bias (§3.2).
- Mỗi zip chứa 1 file CSV cùng schema với REST. Có file `.CHECKSUM` đi kèm (SHA256) — **phải kiểm**.
- Ước tính dung lượng: 1h ≈ 37 KB/tháng/symbol ⇒ 6,5 năm ≈ 2,9 MB/symbol nén. 100 symbol ≈ 290 MB.
- Khung 5m nặng hơn ~12 lần — chỉ tải khi cần chế độ `IntraBar5m` (§3.1 P2) hoặc phân định mơ hồ (§3.8).

**(c) Funding lịch sử**

```
GET https://fapi.binance.com/fapi/v1/fundingRate?symbol=BTCUSDT&startTime={ms}&endTime={ms}&limit=1000
```
Trả `{symbol, fundingTime, fundingRate}`. Chu kỳ 8h ⇒ 1.095 điểm/năm/symbol. Rất nhẹ.

**(d) Phí thật của tài khoản**

```
GET https://fapi.binance.com/fapi/v1/commissionRate?symbol=BTCUSDT   (có ký HMAC)
```
Đã xác nhận endpoint tồn tại [đo 2026-07-31]. **Đọc một lần, lưu vào config backtest.**
**Không hardcode 0,05%.** Trang phí công khai yêu cầu đăng nhập nên không thể lấy số chính thức
bằng công cụ tự động — đây là cách đúng.

### 5.4 Vòng lặp engine

```
CHUẨN BỊ
  1. Nạp klines PERP [from, to] cho mọi symbol, sắp theo openTime, LOẠI nến chưa đóng
  2. Nạp funding [from, to]
  3. Nạp commissionRate → CostModel

PHÁT LẠI  (for i = warmup .. n-1, theo thứ tự thời gian TOÀN CỤC, không theo từng symbol)
  4. ctx = { window = candles[i-199 .. i],  // ĐÚNG 200 nến, khớp CandleLimit
             now = candles[i].CloseTime,
             openTrades, tradingDayState }
  5. decision = decisionSource.Decide(ctx)          // null = không vào lệnh
  6. Nếu decision != null:
       a. Áp CÁC CỔNG RỦI RO đúng như production:
          - MaxTradesPerDay (RiskSetting.cs, mặc định 5), ranh giới ngày UTC (§3.9)
          - MaxDailyLossPercent (3%)
          - MinRiskRewardRatio (1.5)
          - RequireStopLoss
          - chống trùng: đang mở cùng symbol+hướng (MarketScanService.cs:477-486)
       b. entryPrice = candles[i+1].Open            // ← CHỐNG LOOKAHEAD (§3.1)
       c. entryPrice += slippage theo hướng lệnh
       d. mở SimulatedTrade với R = 1 (cố định, §3.9)
  7. Với mọi lệnh đang mở: ExitSimulator.Step(candles[i])
       - nếu Low <= SL và High >= TP  → IsAmbiguous = true, thoát tại SL (bi quan, §3.8)
       - nếu gap: Open đã vượt SL/TP  → thoát tại Open, KHÔNG tại mức SL/TP
       - hết horizon (mặc định 48 nến) → thoát tại Close, ExitReason = Timeout
  8. Khi thoát: NetR = GrossR − FeeR − FundingR − SlippageR
       FundingR = Σ(rate của mọi kỳ funding trong [entryTime, exitTime]) × dấu_hướng × notional / R

TỔNG HỢP
  9. MetricsCalculator → E_R, PF, win rate, σ_R, max DD (R), Sharpe/lệnh, số case Ambiguous
 10. PermutationTester (10.000 vòng) → p-value
 11. BootstrapCi (10.000 vòng)       → CI 95% của E_R
 12. RandomDecisionSource × 1.000    → phân bố đường chuẩn null
 13. Ghi BacktestRun + BacktestTrade[] vào DB; xuất CSV + báo cáo Markdown
```

### 5.5 Giao diện dòng lệnh

```bash
# Nạp dữ liệu
dotnet run --project src/MMW.Backtest -- fetch \
    --symbols BTCUSDT,ETHUSDT,BNBUSDT,SOLUSDT --interval 1h \
    --from 2020-01-01 --to 2026-07-31 --source archive

# ĐƯỜNG B
dotnet run --project src/MMW.Backtest -- run \
    --source deterministic --symbols BTCUSDT,ETHUSDT,BNBUSDT,SOLUSDT \
    --from 2025-01-01 --to 2026-07-31 \
    --decision-mode ClosedBar --slippage-bps 1 --horizon 48 \
    --label "B-v1-oos"

# ĐƯỜNG A
dotnet run --project src/MMW.Backtest -- run \
    --source recorded-ai --from 2026-06-05 --to 2026-07-31 --label "A-v1"

# ĐƯỜNG CHUẨN NGẪU NHIÊN
dotnet run --project src/MMW.Backtest -- run --source random --iterations 1000 --label "null"

# ĐỘ NHẠY SLIPPAGE
dotnet run --project src/MMW.Backtest -- sweep --param slippage-bps --values 0,1,3,5
```

### 5.6 Đầu ra

| Đầu ra | Nơi lưu | Nội dung |
|---|---|---|
| `BacktestRun` | SQL Server | 1 dòng/lần chạy: config JSON, git SHA, mọi metric, p-value, CI |
| `BacktestTrade` | SQL Server | 1 dòng/lệnh mô phỏng, tách rõ GrossR / FeeR / FundingR / SlippageR |
| `runs/{label}/trades.csv` | đĩa | để mở bằng Excel |
| `runs/{label}/report.md` | đĩa | bảng metric + **bảng cổng GO/NO-GO §4.3 tự chấm** |
| `runs/{label}/equity.csv` | đĩa | đường vốn theo R để vẽ drawdown |

**Báo cáo PHẢI tự chấm cổng §4.3 và in GO / VÙNG XÁM / STOP.** Nếu để con người tự đọc số rồi tự
kết luận, con người sẽ tự thuyết phục mình. Máy chấm thì không.

### 5.7 Ước lượng người-ngày

*(1 người-ngày = 8 giờ tập trung. Ở 15–20 h/tuần ⇒ ~2 người-ngày/tuần.)*

| Hạng mục | Người-ngày | Ghi chú |
|---|---|---|
| **Bước 0** — kiểm toán độ ổn định tín hiệu (§3.1) | **1,0** | Script độc lập, không cần DB. **Làm trước tiên.** |
| **Bước 0b** — đếm dòng DB (§1.3) | 0,5 | 4 câu SQL + đọc kết quả |
| Entity + migration (KlineBar, FundingPoint, BacktestRun, BacktestTrade) | 1,0 | |
| `BinanceHistoricalKlineProvider` (REST, phân trang, rate limit) | 1,5 | |
| `BinanceVisionArchiveClient` (zip + CHECKSUM) | 1,0 | |
| `BinanceFundingProvider` | 0,5 | |
| `BacktestEngine` + `ExitSimulator` | 2,0 | Phần dễ sai nhất |
| `CostModel` (fee + funding + slippage) | 1,0 | |
| `MetricsCalculator` | 1,0 | |
| `PermutationTester` + `BootstrapCi` + `RandomDecisionSource` | 1,5 | |
| `DeterministicDecisionSource` (Đường B) | 0,5 | Bọc code có sẵn |
| `RecordedAiDecisionSource` (Đường A) | 1,0 | Vá liên kết Signal→Trade (§1.2) |
| CLI + CSV + báo cáo tự chấm cổng | 1,0 | |
| Test (Hiến chương VI: mã ảnh hưởng tiền ⇒ test bắt buộc) | 2,0 | |
| **TỔNG** | **~15,5 người-ngày ≈ 8 tuần lịch** | |
| *Chỉ tới Đường B (bỏ `RecordedAiDecisionSource`)* | *~14,5 pd ≈ 7 tuần* | |
| *Chỉ Bước 0 + 0b* | ***1,5 pd ≈ 1 tuần*** | **Trả về giá trị lớn nhất trên mỗi giờ bỏ ra** |

**Cảnh báo trung thực:** 8 tuần là **dài** với quỹ 15–20 h/tuần và **chưa tính** thời gian sửa các
bug ở §6.1. Nếu bạn chỉ có ngân sách 2 tuần, hãy làm **Bước 0 + Bước 0b + sửa nến-đóng** và dừng
lại — ba việc đó đã thay đổi hệ thống nhiều hơn phần còn lại.

---

## 6. Kế hoạch forward-test

### 6.1 Testnet hiện đang HỎNG — hai bug, không phải một

Brief giao việc nêu một bug (size tính theo số dư mainnet). **Có hai, và bug thứ hai nghiêm trọng hơn.**

**Bug 1 — size tính theo số dư mainnet.**
`BinanceAccountProviderFactory.Create(string apiKey, string apiSecret)`
(`BinanceAccountProviderFactory.cs:19`) **không có tham số `useTestnet`** và luôn truyền
`FuturesApiBaseUrl` (mainnet). `BinanceAccountProvider.cs:40–43` gọi `/fapi/v2/balance` trên base đó.
Số dư này chảy vào `account.CurrentBalance`, và:

```csharp
// src/MMW.Application/Services/TradeService.cs:100-104
var riskAmount = account.CurrentBalance * settings.MaxRiskPerTradePercent / 100m;
quantity = Math.Round(riskAmount / stopDistance, 8, ...);
```

⇒ chạy testnet nhưng size theo ví thật.

**Bug 2 (nặng hơn) — kết quả lệnh đọc từ MAINNET trong khi lệnh đặt lên TESTNET.**

| Hành động | Đi đâu | Neo |
|---|---|---|
| Đặt lệnh | `_orderFactory.Create(..., _options.UseTestnet)` ⇒ **testnet** khi `UseTestnet=true` | `LiveOrderService.cs:97` |
| Đọc fills để đóng lệnh | `_providerFactory.Create(apiKey, apiSecret)` ⇒ `/fapi/v1/userTrades` trên **mainnet** | `TradeResultSyncService.cs:68` → `BinanceAccountProvider.cs:55–59` |
| Đọc vị thế mở | `_orderFactory.Create(..., useTestnet: false)` — **hardcode false** | `TradeResultSyncService.cs:134` |
| Đối soát vị thế (controller) | `useTestnet: false` — hardcode | `TradesController.cs:458` |

⇒ **Trên testnet, lệnh mở ra nhưng KHÔNG BAO GIỜ đóng trong journal.** Không có `ExitPrice`,
không có `RealizedPnl`, không có `RMultiple`. **Forward-test trên testnet không tạo ra một mẫu dữ
liệu nào.** Không phải "sai số lượng" — là **không có dữ liệu**.

**Kết luận: PHẢI sửa trước, không thể chấp nhận.** Không có đường vòng. Sửa gồm:

1. Thêm `bool useTestnet` vào `IExchangeAccountProviderFactory.Create` và truyền
   `FuturesTestnetBaseUrl` khi true (`BinanceOptions.cs:17` đã có sẵn hằng số).
2. Thay ba chỗ hardcode `useTestnet: false` bằng `_options.UseTestnet`.
3. Test chứng minh: `UseTestnet=true` ⇒ mọi lệnh gọi đi tới `testnet.binancefuture.com`.
   (Hiến chương VI: "Mỗi lớp chặn PHẢI có ít nhất một test chứng minh nó thực sự chặn".)

**Ước lượng: 1,0 người-ngày** kể cả test.

### 6.2 Ba giai đoạn — testnet kiểm ống nước, tiền thật kiểm edge

**Điều phải nói thẳng: testnet KHÔNG BAO GIỜ đo được edge**, kể cả sau khi sửa §6.1. Sổ lệnh
testnet mỏng, giá lệch khỏi mainnet, fill không đại diện. Testnet chỉ chứng minh **phần mềm không
hỏng**. Đó là mục đích duy nhất và hợp lệ của nó.

| GĐ | Mục tiêu | Môi trường | Điều kiện vào | Điều kiện ra |
|---|---|---|---|---|
| **F0** | Chứng minh phần mềm không hỏng | **Testnet** (sau khi sửa §6.1) | Backtest đạt GO (§4.3) | ≥ 30 lệnh **mở và ĐÓNG trọn vẹn** trong journal, khớp 1-1 với sàn; 0 vị thế ma; 0 lệnh trùng; SL/TP luôn gắn được |
| **F1** | Đo slippage & fill thật; đo chênh backtest↔live | **Tiền thật, notional tối thiểu** (`MinOrderNotionalUsdt=20`, `MaxNotionalUsdt=50`) | F0 xong | ≥ 50 lệnh đóng; chênh E_R giữa live và backtest ≤ 0,15 R |
| **F2** | Xác nhận edge trên dữ liệu tương lai thật | **Tiền thật, quy mô do chủ dự án quyết** | F1 xong | ≥ 155 lệnh (nếu E_R kỳ vọng ≥ 0,30 R, §4.2) |

**Bao lâu?** Với 4 symbol khung 1h và trần 5 lệnh/ngày (`RiskSetting.cs`), giả định thực tế 1–3
lệnh/ngày:

| Giai đoạn | Số lệnh | Ở 1 lệnh/ngày | Ở 3 lệnh/ngày |
|---|---|---|---|
| F0 | 30 | 30 ngày | 10 ngày |
| F1 | 50 | 50 ngày | 17 ngày |
| F2 | 155 | 155 ngày | 52 ngày |
| **Tổng** | **235** | **~7,8 tháng** | **~2,6 tháng** |

**Chi phí đo của F1 (tính được chính xác):**
50 lệnh × 0,10% × 50 USDT notional = **2,50 USDT phí**. Cộng funding không đáng kể (§3.5).
Với SL 0,85% giá, rủi ro mỗi lệnh = 0,425 USDT ⇒ kịch bản thua liên tiếp 50 lệnh (xác suất gần 0)
mất **21 USDT**. **Chi phí thông tin của F1 nằm dưới $25.** Đây là phép đo rẻ nhất trong toàn bộ dự
án — rẻ hơn 2 tuần chi phí LLM ở cấu hình hiện tại.

> **Lưu ý phạm vi:** tôi đang phân tích **chi phí đo lường của một quy trình kiểm thử phần mềm**,
> không đưa ra khuyến nghị đầu tư. Việc đưa bao nhiêu vốn vào thị trường là quyết định của chủ dự án
> và tôi không tư vấn về nó.

### 6.3 Điều kiện tiên quyết bắt buộc trước F0

| # | Việc | Neo | Người-ngày |
|---|---|---|---|
| 1 | Sửa hai bug testnet (§6.1) | `BinanceAccountProviderFactory.cs:19`, `TradeResultSyncService.cs:68,134`, `TradesController.cs:458` | 1,0 |
| 2 | Sửa vị thế ma: `catch` ở entry set `Status=Cancelled` cho **mọi** exception, kể cả HTTP 503 = **trạng thái không xác định** | `LiveOrderService.cs:271–282` | 1,0 |
| 3 | Thêm `DisableConcurrentExecution` cho mọi recurring job | `Program.cs:126–156` | 0,3 |
| 4 | Chuyển klines sang PERP `fapi` (§3.7) | `BinanceOptions.cs:8`, `BinanceMarketDataProvider.cs:45` | 0,3 |
| 5 | Chỉ quyết định trên nến đã đóng (§3.1 P1) | `MarketScanService.cs:167–178` | 0,5 |
| 6 | Thêm `TradeSignalId` vào `Trade` + `Source = TradeSource.Signal` (§1.2) | `Trade.cs`, `TradeService.cs:113,119` | 0,5 |
| 7 | Test cho 9/18 lớp chặn còn thiếu (Hiến chương VI) | `LiveOrderService.cs:56–344` vs `LiveOrderTests.cs` | 2,0 |
| 8 | Sửa cron `trade-advisor` `*/1` → `*/15` (§4.5) | `Program.cs:142` | 0,1 |
| 9 | Commit 143 mục đang treo; đưa `LiveOrderService.cs`, `spec.md`, `constitution.md` vào git | | 0,3 |
| | **TỔNG** | | **6,0 người-ngày ≈ 3 tuần** |

**Mục 2 và 7 là không thương lượng** theo Hiến chương III + VI. Mục 5 và 8 vừa sửa phương pháp luận
vừa cắt >90% chi phí LLM.

---

## 7. Hiến chương — điều khoản nào cần sửa

Chuyển sang autotrade **có** căng thẳng với Hiến chương v1.0.0. Không lặng lẽ lờ đi.

### 7.1 Nguyên tắc I — Kỷ Luật Hơn Dự Đoán · **CẦN SỬA**

Văn bản hiện tại:

> "Tính năng chỉ nhằm **tăng tần suất vào lệnh**, tăng đòn bẩy, hay **hứa hẹn tỷ lệ thắng**
> KHÔNG ĐƯỢC đưa vào sản phẩm."
> "Sản phẩm KHÔNG hứa hẹn dự đoán đúng thị trường. Sản phẩm hứa hẹn **chặn lệnh sai kỷ luật**."

**Xung đột:** một hệ đo edge tồn tại chính là để trả lời "chiến lược này thắng bao nhiêu" — về hình
thức là "hứa hẹn tỷ lệ thắng".

**Nhưng xung đột này nông hơn nó trông.** Toàn bộ tài liệu này là **cơ chế chống tự lừa dối** — đúng
tinh thần Nguyên tắc I. Nó không hứa tỷ lệ thắng; nó xây dựng cỗ máy **bác bỏ** những tỷ lệ thắng
không có căn cứ, và nó đặt ngưỡng dừng cứng.

**Sửa đề xuất — bổ sung vào cuối Nguyên tắc I:**

> - Tính năng **đo lường** hiệu quả (backtest, thống kê expectancy, kiểm định ý nghĩa) ĐƯỢC PHÉP và
>   ĐƯỢC KHUYẾN KHÍCH, với điều kiện: chúng **luôn báo cáo cả cỡ mẫu và mức ý nghĩa thống kê** bên
>   cạnh mọi con số hiệu suất; và chúng **định nghĩa sẵn ngưỡng DỪNG** trước khi chạy.
> - Một con số hiệu suất **không kèm cỡ mẫu và p-value** bị coi là "hứa hẹn tỷ lệ thắng" và VI PHẠM
>   nguyên tắc này.
> - "Tăng tần suất vào lệnh" được hiểu là **tăng số lệnh THẬT ĐƯỢC KHỚP**. Giảm tần suất **quét**
>   để chỉ quyết định trên nến đã đóng là **củng cố** nguyên tắc này, không vi phạm.

*Loại thay đổi: MINOR (làm rõ + mở rộng, không đảo ngược).*

### 7.2 Nguyên tắc III — An Toàn Mặc Định · **KHÔNG cần sửa**

> "Chế độ testnet là mặc định. Chuyển sang tiền thật PHẢI là hành động cấu hình có chủ ý."

Kế hoạch F1/F2 (§6.2) **tuân thủ**: đổi `LiveTrading.UseTestnet = false` là một hành động cấu hình
tường minh, có `Enabled` làm kill-switch riêng, có cap notional 50 USDT
(`LiveTradingOptions.cs:12,15,27`). Không cần sửa gì.

**Nhưng phải ghi bổ sung một nghĩa vụ** (đây là **sửa MINOR**, vì đang thêm lớp chặn — điều Hiến
chương cho phép: "Thêm lớp chặn được"):

> - Trước khi chuyển `UseTestnet = false`, PHẢI có bằng chứng lưu trong `BacktestRun` rằng chiến
>   lược đã qua cổng GO, và PHẢI hoàn thành giai đoạn F0 trên testnet với ≥ 30 lệnh đóng trọn vẹn.

### 7.3 Nguyên tắc IV — Ghi Vết Toàn Bộ · **KHÔNG cần sửa, cần MỞ RỘNG**

Bảng `BacktestRun`/`BacktestTrade` là hiện thân của nguyên tắc này ở tầng nghiên cứu. Bổ sung:

> - Mỗi lần chạy backtest PHẢI được lưu, **kể cả lần cho kết quả xấu và lần bị bỏ đi**, kèm cấu hình
>   đầy đủ và git SHA. Chạy backtest mà không lưu là vi phạm — vì không tính được hình phạt
>   multiple-testing.

### 7.4 Ràng buộc thời gian UTC · **cần một ngoại lệ hiển thị**

> "Mọi mốc thời gian nghiệp vụ lưu theo UTC; quy đổi sang giờ Việt Nam chỉ ở lớp hiển thị."

Việc **lưu** theo UTC là đúng và giữ nguyên. Vấn đề là **ranh giới ngày giao dịch**
(`TradingDayService.cs:36`) khiến "5 lệnh/ngày" và "lỗ 3%/ngày" reset lúc **07:00 giờ VN**.
Đây là quyết định **nghiệp vụ**, không phải lưu trữ.

**Sửa đề xuất — thêm vào mục "Dữ liệu":**

> - **Ranh giới ngày giao dịch** (dùng cho giới hạn số lệnh/ngày và giới hạn lỗ/ngày) là một tham số
>   cấu hình theo tài khoản (`TradingDayBoundaryUtcOffset`), mặc định `+00:00` để tương thích ngược.
>   Giá trị vẫn **lưu** theo UTC; chỉ phép chia nhóm theo ngày là dùng offset này.

**Lưu ý cho backtest:** engine phải mô phỏng **đúng ranh giới đang chạy production** (UTC), rồi mới
so sánh với phương án +07:00. Đừng sửa ranh giới và backtest cùng lúc — bạn sẽ không biết cái nào
gây ra thay đổi.

---

## 8. Lộ trình — 12 tuần, quyết định ở mọi chặng

*(Quỹ: 15–20 h/tuần ≈ 2 người-ngày/tuần)*

| Tuần | Việc | Người-ngày | **Cổng quyết định cuối chặng** |
|---|---|---|---|
| **1** | Bước 0: kiểm toán độ ổn định tín hiệu (§3.1) + Bước 0b: đếm dòng DB (§1.3) | 1,5 | Xác nhận tỷ lệ tín hiệu ma trên **watchlist thật của bạn**. Nếu > 10% ⇒ bắt buộc chuyển nến-đóng. **Nếu `Accepted` < 30 dòng ⇒ khai tử Đường A ngay.** |
| **2** | Sửa nến-đóng (§3.1 P1) + cron `trade-advisor` (§4.5) + klines sang `fapi` (§3.7) | 1,1 | Đo lại chi phí LLM thực tế sau 7 ngày. Kỳ vọng **−90%** lượt gọi. Nếu không giảm ⇒ sửa sai chỗ. |
| **3–4** | Sửa bug testnet (§6.1) + vị thế ma + `DisableConcurrentExecution` + test lớp chặn | 4,3 | Đủ điều kiện tiên quyết cho F0. |
| **5–6** | Kho klines: entity + migration + REST provider + archive client + funding | 4,0 | Nạp được 6,5 năm klines PERP 1h cho 4 symbol. Kiểm chéo: klines từ archive **khớp từng byte** với klines từ REST ở vùng chồng lấn. Không khớp ⇒ dừng, tìm nguyên nhân. |
| **7–8** | Engine + ExitSimulator + CostModel + Metrics | 4,0 | **Test tính đúng đắn:** chạy trên chuỗi giá tổng hợp có đáp án biết trước. Sai một xu là engine sai. |
| **9** | Permutation + Bootstrap + `RandomDecisionSource` + báo cáo tự chấm cổng | 2,5 | **Đường chuẩn ngẫu nhiên phải cho E_R ≈ −chi_phí_R (≈ −0,118 R).** Nếu ngẫu nhiên cho E_R > 0 ⇒ **engine có lookahead**. Đây là phép thử tự-kiểm quan trọng nhất. |
| **10** | Đường B trên vùng khoá 2020–2024 (in-sample, chỉ để sanity check) | 1,0 | Kết quả có hợp lý không? Số lệnh/năm bao nhiêu? Có bug lộ ra không? **Chưa được kết luận gì.** |
| **11** | **Đường B trên OOS 2025-01 → 2026-07. MỞ ĐÚNG MỘT LẦN.** | 1,0 | **CỔNG CHÍNH — bảng §4.3.** GO ⇒ tuần 12. STOP ⇒ dừng dự án autotrade, giữ MMW là nhật ký + rule engine (vẫn có giá trị thật). |
| **12** | Đường A (nếu tuần 1 cho thấy đủ mẫu) — so sánh trực diện LLM vs deterministic | 2,0 | LLM có cộng thêm gì lên lõi deterministic không? **Nếu không ⇒ bỏ LLM khỏi luồng sinh tín hiệu, tiết kiệm toàn bộ $1.078/năm.** |
| **13+** | F0 testnet → F1 tiền thật tối thiểu → F2 (§6.2) | — | 2,6 – 7,8 tháng nữa |

**Ba điểm thoát sớm — đều là kết quả hợp lệ, không phải thất bại:**

- **Tuần 1:** nếu `AiSignalScanRecord` gần rỗng **và** tín hiệu ma > 20%, bạn đã học được rằng hệ
  thống chưa từng chạy đủ lâu để tạo dữ liệu, và thứ nó sinh ra phần lớn là nhiễu nến-chưa-đóng.
  Chi phí: **1,5 người-ngày**.
- **Tuần 9:** nếu đường chuẩn ngẫu nhiên cho expectancy dương, engine có lookahead. Sửa hoặc dừng.
- **Tuần 11:** nếu OOS cho STOP, **dừng lại**. Đừng chỉnh rồi chạy lại — OOS chỉ dùng được một lần
  (§3.6). Chỉnh xong thì phải đợi dữ liệu tương lai mới, tức là forward-test, tức là nhiều tháng.

---

## 9. Những điều tôi KHÔNG thể trả lời và tại sao

Trung thực về giới hạn của chính tài liệu này:

| Câu hỏi | Vì sao chưa trả lời được |
|---|---|
| MMW có edge không? | **Chưa từng đo.** Đó là toàn bộ lý do tài liệu này tồn tại. Bất kỳ ai đưa ra con số lúc này đều đang bịa. |
| `AiSignalScanRecord` có bao nhiêu dòng? | Không truy cập được database. Câu SQL ở §1.3, mất 5 phút. **Đây là ẩn số lớn nhất còn lại.** |
| Phí taker chính xác của tài khoản? | Trang phí Binance yêu cầu đăng nhập. Lấy bằng `GET /fapi/v1/commissionRate` (có ký) — endpoint đã xác nhận tồn tại [2026-07-31]. |
| Slippage thật là bao nhiêu? | Chỉ đo được bằng lệnh thật. Đó là mục tiêu chính của giai đoạn F1 (§6.2), chi phí < $25. |
| Chiến lược nào nên dùng? | Ngoài phạm vi tài liệu này (là *hệ đo*, không phải *chiến lược*), và tôi không đưa lời khuyên đầu tư cá nhân. |
| Nên bỏ bao nhiêu vốn? | Không tư vấn. Tôi chỉ ghi rõ **kết luận nhạy với vốn thế nào** (§4.5) để chủ dự án tự quyết. |
| LLM có tốt hơn lõi deterministic không? | Chỉ trả lời được sau khi cả Đường A và Đường B cùng chạy trên **cùng một engine, cùng một mô hình chi phí** (tuần 12). |

---

## Phụ lục A — Toàn bộ số liệu ngoài, kèm nguồn

| # | Số liệu | Giá trị | Nguồn & ngày |
|---|---|---|---|
| A1 | `fapi/v1/klines` limit tối đa | **1500** (`limit=2000` → lỗi `-1130`) | Binance fapi, đo 2026-07-31 |
| A2 | Nến cuối do klines trả về là nến **chưa đóng** | serverTime `01:12:59Z` ∈ [openTime `01:00`, closeTime `01:59:59.999`] | Binance fapi, đo 2026-07-31 |
| A3 | Funding BTCUSDT, 500 kỳ (2026-02-14 → 2026-07-31) | TB **+0,0014785%**/8h · TB |rate| **0,0040544%**/8h · **64,2%** kỳ dương | `fapi/v1/fundingRate`, đo 2026-07-31 |
| A4 | Chu kỳ funding | 8h (kỳ kế tiếp 08:00 UTC) | `fapi/v1/premiumIndex`, đo 2026-07-31 |
| A5 | USDT-M perp đang `TRADING` | **529** (652 mọi trạng thái) | `fapi/v1/exchangeInfo`, đo 2026-07-31 |
| A6 | ...có `onboardDate` ≥ 2 năm | **210** (39,7%) | như trên |
| A7 | Symbol trong archive all-time | **935** tổng · **787** kết thúc `USDT` · 39 `USDC` | S3 `data.binance.vision`, đo 2026-07-31 |
| A8 | ⇒ symbol USDT-M đã biến mất | **258 (32,8%)** | A7 − A5 |
| A9 | Delisted xác nhận có trong archive | SRMUSDT, BTCSTUSDT, COCOSUSDT, TOMOUSDT, HNTUSDT, RAYUSDT, CVCUSDT, FTTUSDT, LUNAUSDT, BNXUSDT, SCUSDT, ANTUSDT | S3 listing, đo 2026-07-31 |
| A10 | Klines PERP 1h sớm nhất trong archive | `BTCUSDT-1h-2020-01.zip` (37.271 byte) | S3, đo 2026-07-31 |
| A11 | ATR(14) TB trên 1h, % giá | BTC **0,568%** · SOL **0,876%** | Tự tính, 1.000 nến `fapi`, 2026-07-31 |
| A12 | Chênh close spot↔perp | TB 4,44 / 4,71 / 5,51 bp (BTC/ETH/SOL); râu nến max 120–212 bp | Tự tính, 1.000 nến, 2026-07-31 |
| A13 | Sai số đơn vị R do bug spot/perp | 5,9% / 4,5% / 4,7% | Tự tính, 931 case, 2026-07-31 |
| A14 | % case đảo kết quả do bug spot/perp | 1,9% / 2,1% / 2,3% | Tự tính, 931 case, 2026-07-31 |
| A15 | Case mơ hồ trong nến (SL 1,5ATR / TP 3ATR, 1h) | **0/931** | Tự tính, 2026-07-31 |
| A16 | **Tín hiệu ma / tổng tín hiệu phát ra** | **17,9% / 19,8% / 19,0%** (BTC/ETH/SOL) | Tự tính, 124 giờ × 11 mốc 5', 2026-07-31 |
| A17 | Phí taker USDT-M | **giả định 0,05%/chiều** — trang phí yêu cầu đăng nhập; nguồn thứ cấp tradersunion.com nói maker "khoảng 0,02%" | tradersunion.com, 2025-11-14 — **yếu, phải xác minh bằng A18** |
| A18 | `fapi/v1/commissionRate` tồn tại | trả `-2014 API-key format invalid` (không phải 404) | Binance fapi, đo 2026-07-31 |
| A19 | Deflated Sharpe Ratio | Bailey & López de Prado, *J. Portfolio Management* 40(5) tr. 94; PDF: `davidhbailey.com/dhbpapers/deflated-sharpe.pdf` | truy cập 2026-07-31 |
| A20 | Chi phí LLM hiện tại | **$1.078/năm** | `docs/strategy/03-financial-analyst.md` (nội bộ) |

**Không tìm được dữ liệu công khai cho:** bảng phí VIP chính thức của Binance (yêu cầu đăng nhập);
slippage thực tế của lệnh MARKET quy mô 20–50 USDT (chỉ đo được bằng lệnh thật); bất kỳ nghiên cứu
công khai nào về hiệu quả của chiến lược EMA20/50 + MACD trên perp crypto khung 1h **đã hiệu chỉnh
multiple testing** — nếu tồn tại, tôi không tìm thấy, và điều đó tự nó đáng lưu ý.

## Phụ lục B — Chỉ mục neo mã nguồn

| Khẳng định | Neo |
|---|---|
| Chỉ báo tính trên nến cuối (chưa đóng) | `src/MMW.Application/MarketData/MarketAnalyzer.cs:23-24` |
| Cửa sổ 200 nến | `src/MMW.Application/Services/MarketScanService.cs:22` |
| EMA seed bằng SMA đầu mảng ⇒ phụ thuộc cửa sổ | `src/MMW.Application/Indicators/IndicatorService.cs:119-141` |
| ATR Wilder | `src/MMW.Application/Indicators/IndicatorService.cs:87-116` |
| SL = 1,5 ATR, RR = 2 | `src/MMW.Application/MarketData/SignalGenerator.cs:9,12` |
| Klines lấy từ SPOT | `src/MMW.Infrastructure/.../BinanceOptions.cs:8` + `BinanceMarketDataProvider.cs:44-45` |
| Prompt yêu cầu EMA200 nhưng không gửi | `src/MMW.Application/Services/MarketScanService.cs:47-49` (`grep ema200 src/` = 0) |
| Cron market-scan `*/5` | `src/MMW.Web/Program.cs:126-130` |
| Cron trade-advisor `*/1`, comment nói "3 phút" | `src/MMW.Web/Program.cs:139-143` |
| `MinSignalScore = 2` | `src/MMW.Domain/Entities/AppSetting.cs:18` |
| Watchlist mặc định 4 symbol | `src/MMW.Web/Data/SeedData.cs:61-64` |
| Chống trùng chỉ theo (symbol, hướng, giá xấp xỉ, đang mở) | `src/MMW.Application/Services/MarketScanService.cs:477-486` |
| Size theo `CurrentBalance` | `src/MMW.Application/Services/TradeService.cs:100-104` |
| `Source = Manual` cho lệnh tự tạo | `src/MMW.Application/Services/TradeService.cs:113` |
| Liên kết signal→trade chỉ là chuỗi text | `src/MMW.Application/Services/TradeService.cs:119` |
| Factory tài khoản không có tham số testnet | `src/MMW.Infrastructure/.../BinanceAccountProviderFactory.cs:19` |
| Fills đọc từ mainnet `fapi` | `src/MMW.Infrastructure/.../BinanceAccountProvider.cs:55-59` |
| Result-sync hardcode `useTestnet: false` | `src/MMW.Application/Services/TradeResultSyncService.cs:134` |
| Controller đối soát hardcode `useTestnet: false` | `src/MMW.Web/Controllers/TradesController.cs:458` |
| Đặt lệnh dùng `_options.UseTestnet` | `src/MMW.Application/Services/LiveOrderService.cs:97` |
| Entry MARKET | `src/MMW.Application/Services/LiveOrderService.cs:265` |
| Vị thế ma: catch mọi exception → `Cancelled` | `src/MMW.Application/Services/LiveOrderService.cs:271-282` |
| PnL trừ commission, **không** trừ funding | `src/MMW.Application/Services/TradeResultSyncService.cs:221` (`grep funding src/` = 0) |
| Cap notional 20–50 USDT | `src/MMW.Application/MarketData/LiveTradingOptions.cs:24,27` |
| Ngưỡng rủi ro mặc định | `src/MMW.Domain/Entities/RiskSetting.cs:18-42` |
| `TradingDay` không có expectancy/PF | `src/MMW.Domain/Entities/TradingDay.cs:16-28` |
| Không có FK signal→trade | `grep "SignalId\|TradeSignalId" src/MMW.Domain/Entities/*.cs` = 0 |
| Không có backtest engine | `grep -rli backtest src/` = 0 |
| Không có websocket | `grep -rli "websocket\|wss://" src/` = 0 |

---

*Tài liệu này không chứa lời khuyên đầu tư. Nó phân tích tính khả thi kỹ thuật và cấu trúc chi phí
của một hệ thống phần mềm đo lường. Mọi quyết định về vốn thuộc về chủ dự án.*
