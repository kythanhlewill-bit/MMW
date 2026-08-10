# Adaptive scoring & execution V2

Kế thừa [adaptive-execution-v1.md](adaptive-execution-v1.md). V1 đúng về *triết lý* (hợp lưu mềm,
gate cứng hẹp, tranche không tăng tổng rủi ro) nhưng phần lớn nội dung của nó chưa chạm tới thứ
quyết định tỉ lệ thắng thật sự: **giá của một lệnh sai và khoảng cách tới nơi setup bị phủ định**.

V2 không thêm mẫu hình mới. V2 sửa những chỗ mà một hệ thống đúng-về-lý-thuyết vẫn mất tiền:
chi phí trên mỗi R, dừng lỗ đặt sai chỗ, tín hiệu price action quá dễ kích hoạt, và chiều lệnh
được chọn bằng một phép so sánh không phù hợp với ngày range.

Live trading vẫn TẮT. Mọi thay đổi phải qua backtest và phải giữ nguyên tính tất định.

---

## §0 — Toán chi phí: lý do của mọi thay đổi bên dưới

Đây là phần phải đọc trước. Nó không phải nhận định, nó là số học rút ra từ chính mã hiện tại.

Mô hình phí đang dùng ([SimulatedTradePosition.cs:240](../../src/MMW.Application/Backtest/SimulatedTradePosition.cs)):

```
feeR = price × (takerFee% / 100) / unitRisk × sizeR × weight
unitRisk = StopAtrMultiple × ATR = 1.5 × ATR      (EngineSetting.cs:50)
```

Đặt `a = ATR / price`. Với `a = 0,18%` (giá trị điển hình của ATR(14) khung 15m trên BTCUSDT) và
phí taker 0,05%/chiều:

| Khoản | Công thức | Giá trị (R) |
|---|---|---|
| Phí mỗi chiều | `0,0005 / (1,5 × 0,0018)` | **0,185** |
| Trượt giá vào lệnh (1 bps) | `0,0001 / 0,0027` | 0,037 |
| Trượt giá dừng lỗ (3 bps) | `0,0003 / 0,0027` | 0,111 |

Suy ra:

- Một lệnh **thua** tốn `1 + 0,185 + 0,037 + 0,185 + 0,111 = 1,52R`.
- Một lệnh **thắng** tại mục tiêu `T` chỉ thu về `T − 0,41R`.

Tỉ lệ thắng hoà vốn `p = 1,52 / (T + 1,11)`:

| Mục tiêu | p hoà vốn (a = 0,18%) | p hoà vốn (a = 0,12%, thị trường yên) |
|---|---|---|
| **1,0R** — chính là chế độ `Range` của V1 | **72%** | **82%** |
| 1,5R — chế độ `Standard` của V1 | 58% | 67% |
| 2,0R | 49% | 57% |
| 3,0R | 37% | 44% |

**Kết luận thứ nhất: quy tắc "ngày range chốt toàn bộ tại 1R" của V1 gần như không thể thắng.**
Nó đòi hỏi 72–82% win rate chỉ để hoà vốn. Không có bộ chấm điểm nào trên khung 15m làm được
điều đó một cách bền vững. Quy tắc này phải bị gỡ bỏ, không phải tinh chỉnh.

**Kết luận thứ hai: dừng lỗ 1,5 ATR đang quá hẹp so với phí.** Chi phí tính theo R tỉ lệ NGHỊCH
với độ rộng dừng lỗ. Nới dừng lỗ theo cấu trúc (trung bình ~2,5 ATR) và chuyển chân vào lệnh +
chốt lời sang lệnh maker (0,02%) cho kết quả:

| | Dừng 1,5 ATR, taker cả hai chân | Dừng ~2,5 ATR (cấu trúc), maker vào + maker TP |
|---|---|---|
| Chi phí một lệnh thua | 0,52R | **0,23R** |
| Chi phí một lệnh thắng | 0,41R | **0,10R** |
| p hoà vốn tại 1,5R | 58% | **47%** |
| p hoà vốn tại 2,0R | 49% | **39%** |

**Ngưỡng hoà vốn giảm 11 điểm phần trăm mà chưa cần cải thiện tín hiệu một chút nào.** Đây là
đòn bẩy lớn nhất trong toàn bộ tài liệu này, và nó nằm ở §3 + §8 chứ không nằm ở §2.

### 0.1 ⚠️ Đính chính: các con số trên là ƯỚC LƯỢNG, không phải số đo

Bảng trên dùng phí taker **giả định** 0,05%/chiều. Baseline V1.4 cho phép giải ngược ra con số
thật, và nó khác:

> Giờ 08 UTC: 85 lệnh, win rate **64,71%**, expectancy **−0,0047R** (gần đúng hoà vốn).

Giải ngược với lệnh thua tốn `L` và lệnh thắng thu `W`:
`0,6471·W − 0,3529·L = −0,0047`. Với `L ≈ 1,30R` thì `W ≈ 0,70R` — đúng chân dung của "chốt toàn
bộ tại 1R rồi trả phí hai chiều". Điểm hoà vốn thật tại mục tiêu 1R là:

```
p = L / (W + L) = 1,30 / 2,00 ≈ 65%
```

**Không phải 72%.** §0 bi quan quá bảy điểm phần trăm vì đã giả định phí rộng hơn thực tế.

Kết luận không đổi — thắng 65% mà vẫn lỗ vẫn là bản án tử cho mục tiêu 1R, và cột 82% ở thị
trường yên chỉ càng tệ hơn theo cùng một hướng. Nhưng bảng này phải được **tính lại từ số đo**
sau lần chạy đầu tiên có `TotalFeeR` và `TotalFundingR` (xem §8a), chứ không giữ nguyên con số
giả định. `BacktestCli` đã in sẵn chi phí trung bình mỗi lệnh cho đúng mục đích đó.

Bài học chung, quan trọng hơn con số: **lập luận này đã đúng về hướng nhưng sai về độ lớn suốt
một thời gian, vì thước đo chưa tồn tại.** Đó là lý do §8a được đưa lên làm việc đầu tiên.

---

## §1 — Sửa lỗi tất định (P0, không thêm tính năng)

Những mục dưới đây là lỗi, không phải khẩu vị. Sửa trước, chạy lại backtest #7 để có baseline
sạch, rồi mới làm §2 trở đi.

### 1.1 `InFibonacciRetracement` thoát sớm sai

[PriceActionAnalyzer.cs:157](../../src/MMW.Application/Trading/Scoring/PriceActionAnalyzer.cs) —
`if (impulse <= 0m) return false;` nằm **trong** vòng lặp quét ngược. Một nhịp suy biến duy nhất
(hai pivot cùng giá) huỷ toàn bộ phần quét còn lại. Phải là `continue`.

### 1.2 Mẫu hình dùng `currentPrice` để xác nhận phá neckline

[PriceActionAnalyzer.cs:125](../../src/MMW.Application/Trading/Scoring/PriceActionAnalyzer.cs) —
`currentPrice` ở chạy thật là giá ticker (chạy trong nến), ở backtest là giá đóng nến cuối
([ArchiveMarketDataProvider.cs:60](../../src/MMW.Application/Backtest/ArchiveMarketDataProvider.cs)).
Cùng một chuỗi nến cho hai kết quả khác nhau giữa hai môi trường, và ở chạy thật tín hiệu nhấp
nháy trong nến.

**Quy tắc V2:** hoàn thành mẫu hình xác nhận bằng `window[^1].Close`. `currentPrice` chỉ dùng để
đo khoảng cách (§2.5, Fibonacci), không bao giờ dùng để quyết định một mẫu hình đã hình thành hay
chưa.

### 1.3 ATR tính bằng hằng số RSI

[PriceActionAnalyzer.cs:63](../../src/MMW.Application/Trading/Scoring/PriceActionAnalyzer.cs) —
`_indicators.Atr(window, RsiPeriod)`. Trùng số 14 nên hiện tại vô hại, nhưng đổi `RsiPeriod` sẽ
âm thầm đổi ATR. Thêm `private const int AtrPeriod = 14;` và dùng nó.

### 1.4 `Supports()` che khuất `Opposes()`

[PriceActionAnalyzer.cs:19–25](../../src/MMW.Application/Trading/Scoring/PriceActionAnalyzer.cs) —
hai hàm có thể cùng đúng (hai đáy + phân kỳ RSI giảm). `MomentumCriterion` kiểm `Supports` trước
([TechnicalCriteria.cs:309](../../src/MMW.Application/Trading/Scoring/Criteria/TechnicalCriteria.cs))
nên bằng chứng ngược chiều bị vứt trong im lặng. Bằng chứng mâu thuẫn phải LÀM GIẢM độ tin cậy,
không phải biến mất.

**Thay bằng:**

```csharp
public int NetConfluence(TradeDirection d) => SupportCount(d) - OpposeCount(d);
```

`MomentumCriterion` cộng `Math.Clamp(NetConfluence, -2, +2)`. `MarketStructureCriterion` chỉ được
cộng điểm hợp lưu khi `NetConfluence > 0`.

### 1.5 `LeaderCorrelation` luôn null ⟹ mọi mã không phải BTC bị trừ 4 điểm vĩnh viễn

[SignalEvalService.cs:396](../../src/MMW.Application/Services/SignalEvalService.cs) — gán cứng
`LeaderCorrelation = null`. `LeaderCorrelationCriterion` trả `Missing` ⟹ 0/4 điểm. BTCUSDT được
4/4 ([MarketCriteria.cs:149](../../src/MMW.Application/Trading/Scoring/Criteria/MarketCriteria.cs)).

ETHUSDT vì vậy luôn khởi điểm thấp hơn BTCUSDT 4 điểm với ngưỡng vào lệnh 55 — một thiên lệch có
hệ thống mà không ai chọn. Hoặc tính tương quan thật (Pearson trên 96 nến 15m log-return so với
BTCUSDT), hoặc trả `CriterionResult(2, ...)` trung tính. Không được để nguyên.

### 1.6 Không có gate chặn mở vị thế thứ hai trên cùng mã

[BacktestEngine.cs:205–216](../../src/MMW.Application/Backtest/BacktestEngine.cs) — mỗi nến 15m,
nếu phiếu `Entered` thì `open.Add(...)` vô điều kiện. `MaxTradesGate` chỉ đếm `TradesToday`, không
đếm vị thế đang mở. Ba lệnh BTCUSDT long chồng nhau trong một ngày là hợp lệ với mã hiện tại.

Chính V1 đã nêu đây là điều kiện đánh giá ("một vị thế đang mở không được tạo thêm setup độc lập
cùng symbol") nhưng chưa có gate nào cưỡng chế. Xem §6.1.

---

## §2 — Chất lượng tín hiệu price action (P1)

`PriceActionAnalyzer` hiện tại **quá dễ kích hoạt**. Trong một hệ thống mà mẫu hình chỉ là hợp
lưu mềm thì false positive không giết ai, nhưng nó đẩy điểm `technical.market_structure` từ 3 lên
8 (chênh 5/85 điểm — đủ để vượt ngưỡng 55) và cộng 2 điểm momentum. Đủ để biến một setup tầm
thường thành một lệnh.

### 2.1 Hình học vai-đầu-vai đang mâu thuẫn với chính nó

[PriceActionAnalyzer.cs:36–38](../../src/MMW.Application/Trading/Scoring/PriceActionAnalyzer.cs):

```
ShoulderToleranceAtr = 0.6      // hai vai được lệch nhau tới 0,6 ATR
HeadProminenceAtr    = 0.25     // đầu chỉ cần nhô hơn vai cao nhất 0,25 ATR
```

Hai vai được phép lệch nhau **gấp 2,4 lần** mức mà cái đầu phải nhô lên. Nghĩa là một đường zigzag
bất kỳ với ba pivot đều thoả. Đây là nguồn false positive lớn nhất trong file.

**V2:**

| Tham số | V1 | V2 | Lý do |
|---|---|---|---|
| `HeadProminenceAtr` | 0,25 | **0,80** | Đầu phải nhìn thấy được, không phải nhiễu |
| `ShoulderToleranceAtr` | 0,60 | **0,50** | Hai vai phải thật sự cân |
| *(mới)* ràng buộc chéo | — | `\|LS − RS\| < headProminence` | Vai gần nhau hơn khoảng đầu nhô lên |
| *(mới)* `MinShoulderGapBars` | — | **6** | Ba pivot dính nhau không phải mẫu hình |
| *(mới)* độ nghiêng neckline | — | ≤ 0,5 ATR | Neckline dốc = không phải H&S |

### 2.2 Neckline của vai-đầu-vai chỉ lấy nửa phải

[PriceActionAnalyzer.cs:77–80](../../src/MMW.Application/Trading/Scoring/PriceActionAnalyzer.cs)
truyền `lows.TakeLast(3)` nhưng `NecklineBroken` chỉ đọc `pivots[^2]` và `pivots[^1]` — tức đoạn
đầu→vai phải. Neckline thật là mức nối **cả hai** đáy trung gian. Lấy một nửa thường cho mức dễ
phá hơn ⟹ xác nhận sớm hơn thực tế.

**V2:** neckline = `max/min` trên **hợp** hai đoạn (vai trái→đầu) và (đầu→vai phải).

### 2.3 Hai đáy/hai đỉnh thiếu ràng buộc thời gian và độ nảy

[PriceActionAnalyzer.cs:95–99](../../src/MMW.Application/Trading/Scoring/PriceActionAnalyzer.cs) —
`EqualLastTwo` chỉ kiểm `|P1 − P2| ≤ 0,4 ATR`. Hai đáy cách nhau 3 nến trên một vùng đi ngang
phẳng thoả điều kiện này. Đó không phải hai đáy, đó là một cái nền.

**V2 — thêm hai ràng buộc:**

- `MinPivotGapBars = 8` (2 giờ trên khung 15m) giữa hai đáy.
- Đỉnh trung gian giữa hai đáy phải cao hơn đáy cao nhất **≥ 1,0 ATR**. Không có cú nảy thật thì
  không có hai đáy — chỉ có một vùng tích luỹ.

### 2.4 Phân kỳ RSI đang ở mức nhiễu

[PriceActionAnalyzer.cs:39, 128–140](../../src/MMW.Application/Trading/Scoring/PriceActionAnalyzer.cs):

- `MinRsiDivergence = 2` điểm RSI — nằm trong sai số làm tròn của chính chỉ báo.
- Không ràng buộc khoảng cách giữa hai pivot: phân kỳ giữa hai pivot cách nhau 3 nến vô nghĩa.
- Không yêu cầu pivot đầu nằm trong vùng cực trị. Phân kỳ ở RSI 50 không nói lên điều gì; phân kỳ
  từ RSI 28 mới là kiệt sức.
- RSI tính lại trên `candles.Take(a.Index + 1)` trong cửa sổ 100 nến. Với `a.Index < 40`, làm trơn
  Wilder chưa hội tụ ⟹ giá trị lệch đáng kể so với RSI cuộn thật.

**V2:**

| Tham số | V1 | V2 |
|---|---|---|
| `MinRsiDivergence` | 2 | **5** |
| Khoảng cách hai pivot | không giới hạn | **5 ≤ gap ≤ 50 nến** |
| Vùng cực trị pivot đầu | không yêu cầu | **RSI ≤ 35** (tăng) / **≥ 65** (giảm) |
| Chỉ số pivot tối thiểu | 0 | **≥ 3 × RsiPeriod = 42** trong cửa sổ |

### 2.5 Mẫu hình không có tuổi ⟹ không bao giờ hết hạn

Không có khái niệm "mẫu hình này hoàn thành cách đây bao lâu". Một cú hai đáy phá neckline lúc
09:00 vẫn trả `true` lúc 17:00 miễn là giá còn trên neckline. Bộ chấm điểm vì vậy cho 8/10 điểm
cấu trúc cho một setup đã cũ 8 tiếng — đúng loại lệnh vào muộn mà `EntryLocationCriterion` sinh ra
để chặn.

**V2:** đổi `PriceActionSignals` từ `bool` sang `int? AgeBars` cho từng tín hiệu. Tín hiệu chỉ
được tính khi `AgeBars <= PatternMaxAgeBars` (mặc định **12** nến = 3 giờ trên 15m). Điểm hợp lưu
giảm tuyến tính theo tuổi thay vì bật/tắt:

```
confluenceWeight = 1 - AgeBars / PatternMaxAgeBars
```

### 2.6 Fibonacci: nhịp quá nhỏ vẫn sinh vùng hồi

Không có sàn cho `impulse`. Một nhịp 0,3 ATR sinh ra "vùng hồi 38,2–61,8%" rộng 0,07 ATR — giá
chạm ngẫu nhiên.

**V2:** yêu cầu `impulse ≥ 1,5 ATR`. Thu hẹp vùng cộng điểm về **0,5–0,618** (golden pocket);
vùng 0,382–0,5 và 0,618–0,786 được ghi nhận nhưng không cộng điểm.

### 2.7 `PriceActionAnalyzer` bị gọi ba lần với cùng đầu vào

[TechnicalCriteria.cs:111, 200, 265](../../src/MMW.Application/Trading/Scoring/Criteria/TechnicalCriteria.cs) —
ba tiêu chí mỗi cái tự `new PriceActionAnalyzer(new SwingDetector(), indicators)` rồi gọi
`Analyze` với **đúng cùng bộ tham số**. Mỗi lần gọi phát hiện lại pivot và tính lại RSI hai lần.
Trên 70.000 mốc backtest × 2 mã, đó là ~420.000 lần chạy thừa hai phần ba.

Ngoài chi phí, nó còn đi ngược thiết kế đã tuyên bố ở
[ScoringContext.cs:36–39](../../src/MMW.Application/Trading/Scoring/ScoringContext.cs): "mọi dữ
liệu đã nằm sẵn trong ScoringContext".

**V2:** tính một lần trong `BuildContextAsync`, đặt `PriceActionSignals` vào `ScoringContext`.
Tiêu chí chỉ đọc.

---

## §3 — Dừng lỗ và mục tiêu theo cấu trúc (P0 — đòn bẩy lớn nhất)

### 3.1 Vấn đề

[SignalEvalService.cs:430–441](../../src/MMW.Application/Services/SignalEvalService.cs):

```csharp
var risk = atr * setting.StopAtrMultiple;   // 1,5 × ATR, luôn luôn
return (price - risk, price + reward);
```

Dừng lỗ **không hề biết** đáy xoay gần nhất nằm ở đâu. Nếu đáy đó nằm cách giá 1,2 ATR thì dừng
lỗ 1,5 ATR nằm ngay dưới một cụm lệnh dừng — nơi giá bị hút tới.

Điều oái oăm: hệ thống **đã phát hiện được** tình huống này. `LiquidityZoneCriterion` trả đúng 0
điểm khi có cụm thanh khoản ngay ngoài dừng lỗ
([LiquidityCriteria.cs:104–108](../../src/MMW.Application/Trading/Scoring/Criteria/LiquidityCriteria.cs)).
Nhưng phản ứng là **trừ 5 điểm rồi vào lệnh với đúng cái dừng lỗ đó**. Phản ứng đúng là **dời dừng
lỗ**.

Đây là nguồn thua trực tiếp nhất trong toàn hệ thống: setup đúng, hướng đúng, chết vì dừng lỗ đặt
sai 20 điểm giá.

### 3.2 Quy tắc V2

**Dừng lỗ cấu trúc:**

```
long:  stop = min(swingLow_gần_nhất − 0,30 × ATR,  entry − StopAtrMultipleMin × ATR)
short: stop = max(swingHigh_gần_nhất + 0,30 × ATR, entry + StopAtrMultipleMin × ATR)
```

- `swingLow_gần_nhất` = pivot đáy **đã xác nhận** gần nhất dưới giá vào lệnh, trong cửa sổ 40 nến.
- `StopAtrMultipleMin = 1,0` — sàn, chống dừng lỗ dính sát khi cấu trúc quá gần.
- `StopAtrMultipleMax = 3,0` — **trần cứng**. Vượt trần ⟹ **không vào lệnh**, không phải vào với
  size nhỏ. Một setup mà điểm phủ định cách 3,5 ATR là một setup mà bạn không đọc được cấu trúc.
- Không có pivot hợp lệ ⟹ quay về `1,5 × ATR` như hiện tại và ghi `IsApproximation = true`.

**Mục tiêu cấu trúc:**

```
target = mức cấu trúc đối diện gần nhất − 0,20 × ATR (long)
```

lấy từ pivot khung **15m + 4h + 1d**, không chỉ 15m (§3.3).

**Gate room (mới, veto cứng `InsufficientData` → đổi tên `InsufficientRoom` nếu thêm được enum):**

> Nếu `|target − entry| / |entry − stop| < 1,6` thì **không vào lệnh**.

1,6R là ngưỡng rút thẳng từ bảng §0: dưới mức đó, ngay cả 55% win rate cũng không đủ trả phí.
Đây là gate cứng đúng nghĩa — không phải "điểm thấp", mà là "cấu trúc không có chỗ cho lệnh này".

### 3.3 Mở rộng tầm nhìn sang khung lớn

[LiquidityCriteria.cs:86](../../src/MMW.Application/Trading/Scoring/Criteria/LiquidityCriteria.cs) —
`_swings.Detect(context.EntryCandles, ...)` chỉ nhìn 15m. Kháng cự 4h và mức ngày **vô hình** với
engine.

Vào long ngay dưới đỉnh 4h là kịch bản thua kinh điển, và hiện tại không tiêu chí nào thấy được
nó. `ScoringContext` đã mang sẵn `BiasCandles` (4h) và `DailyCandles` (1d) — dữ liệu có rồi, chỉ
là không ai dùng.

**V2:** `LiquidityZoneCriterion` gộp pivot từ cả ba khung, gán trọng số theo khung (1d = 3, 4h = 2,
15m = 1) khi đếm cụm chắn đường.

### 3.4 Ảnh hưởng lên `StopAtrMultiple`

`EngineSetting.StopAtrMultiple` giữ nguyên tên nhưng đổi vai: từ "khoảng cách dừng lỗ" thành
"khoảng dự phòng khi không có cấu trúc". Thêm `StopAtrMultipleMin = 1,0`,
`StopAtrMultipleMax = 3,0`, `StopStructureBufferAtr = 0,30`, `MinStructuralRr = 1,6`.

---

## §4 — Chọn chiều lệnh (P1)

### 4.1 Ngày range đang được giao dịch thuận xu hướng

[SignalEvalService.cs:416–428](../../src/MMW.Application/Services/SignalEvalService.cs):

```csharp
return fast < mid ? TradeDirection.Short : TradeDirection.Long;
```

Trên ngày `AllowedDirections.Both`, engine chọn **một** chiều theo EMA 4h rồi không bao giờ xét
chiều kia. Nhưng chính bộ chấm điểm nói:

> `DayRegime.Range` → "Ngày đi ngang — chỉ hợp setup đảo chiều tại biên"
> ([MarketCriteria.cs:45](../../src/MMW.Application/Trading/Scoring/Criteria/MarketCriteria.cs))

Hai câu này mâu thuẫn: một bên khai báo cần setup đảo chiều, bên kia chọn chiều thuận EMA. Trên
ngày range, EMA 20/50 khung 4h đan xen và gần như ngẫu nhiên — chiều lệnh về bản chất là tung
đồng xu. `AdaptiveRegimePolicy` cho phép tới 3 lệnh/ngày range
([AdaptiveRegimePolicy.cs:16](../../src/MMW.Application/Trading/DailyPlanning/AdaptiveRegimePolicy.cs)),
nên đây không phải một trường hợp hiếm.

**Đây nhiều khả năng là rò rỉ win rate lớn thứ hai sau §3.**

### 4.2 Quy tắc V2

Khi `AllowedDirections.Both`:

1. Chấm điểm **cả hai chiều** (bộ chấm là hàm thuần, chi phí chỉ là CPU trong bộ nhớ, và §2.7 đã
   cắt 2/3 chi phí price action).
2. Chọn chiều có điểm cao hơn.
3. **Yêu cầu biên độ:** chênh lệch phải `≥ DirectionMarginPoints` (mặc định **8**). Hai chiều
   chấm gần bằng nhau nghĩa là thị trường không nói gì — **không vào lệnh**.
4. Ghi cả hai điểm vào phiếu (`TotalScore`, `OppositeScore`) để trả lời được câu "vì sao chọn
   chiều này".

**Thêm ràng buộc riêng cho ngày range** — vị trí trong biên độ ngày:

| Vị trí giá trong biên độ | Chiều được phép |
|---|---|
| ≥ 75% (gần biên trên) | Chỉ Short |
| ≤ 25% (gần biên dưới) | Chỉ Long |
| 25–75% (giữa biên) | **Không chiều nào** |
| < 0% hoặc > 100% (đã ra ngoài) | **Không chiều nào** |

Vào lệnh giữa vùng range là cách nhanh nhất để thua trên ngày range, và hiện tại không có gì ngăn.

### 4.2a Định nghĩa "biên độ" — chốt tại đây

Bản nháp viết "biên độ 20 phiên" mà không nói phiên nào, khung nào, tính lúc nào. Ba khoảng
trống, mỗi cái đủ để sinh ra một lỗi nhìn trước. Định nghĩa đã chốt (`DirectionPolicy.Locate`):

| | |
|---|---|
| Nguồn | Nến **khung thiên hướng** (4h) đã đóng của chính mã đang xét |
| Cửa sổ | **30 nến cuối = 5 ngày giao dịch** (`RangeLookbackBars`) |
| Biên trên | Đỉnh xoay cao nhất trong cửa sổ, chỉ tính pivot **đã xác nhận** |
| Biên dưới | Đáy xoay thấp nhất, cùng điều kiện |
| Thời điểm | Ngay lúc chấm, so với giá hiện tại |
| Không dựng được | Ngày range mà thiếu pivot hai phía ⟹ **veto `InsufficientData`** |

**Vì sao không dùng 20 phiên NGÀY.** Đó là cửa sổ `DayRegimeClassifier` dùng để gọi tên cấu trúc
BTC, và nó đúng cho việc đó. Nhưng 20 phiên ngày là ba tuần: biên độ dựng từ đó rộng tới mức "sát
biên" gần như không bao giờ xảy ra với một lệnh giữ 1–4 tiếng, nên ràng buộc sẽ không lọc gì cả
ngoài việc xoá sổ toàn bộ lệnh ngày range — vi phạm thẳng điều kiện chấp nhận số 8. Năm ngày giao
dịch là biên độ mà giá **đang** ở trong.

**Phần trăm KHÔNG bị kẹp về [0, 100].** "Sát biên trên" và "đã phá lên khỏi biên" là hai kết luận
trái ngược nhau; kẹp lại sẽ biến một cú phá vỡ thành tín hiệu fade — đúng lệnh tệ nhất có thể vào.

**Pivot đã xác nhận là bắt buộc.** `SwingDetector` mang sẵn độ trễ N nến của R-007, nên mọi điểm
xoay nó trả về đều đã biết được tại thời điểm chấm. Dùng cực trị của phần nến chưa xác nhận sẽ cho
một biên độ tự vẽ lại về quá khứ.

**Giá dùng để đo là `CurrentPrice`,** và điều đó nhất quán với §1.2: `currentPrice` được phép ĐO
KHOẢNG CÁCH, chỉ không được phép hoàn thành một mẫu hình.

### 4.3 Dải RSI không được soi gương theo chiều

[TechnicalCriteria.cs:285–287](../../src/MMW.Application/Trading/Scoring/Criteria/TechnicalCriteria.cs) —
`RsiLowerBound = 45`, `RsiUpperBound = 65` áp **giống nhau** cho long và short. Với một lệnh short,
động lượng lành nằm ở RSI 35–55, không phải 45–65. Short hiện đang bị chấm bằng một dải thiên
long, nên short tốt bị trừ điểm và short xấu được cộng.

**V2:** với `TradeDirection.Short`, dùng `[100 − upper, 100 − lower] = [35, 55]`.

### 4.4 Xác nhận khối lượng chấp nhận nến doji

[TechnicalCriteria.cs:356–358](../../src/MMW.Application/Trading/Scoring/Criteria/TechnicalCriteria.cs) —
`DirectionConfirmed = candle.Close > candle.Open`. Một nến doji đóng cao hơn mở 0,01 với khối
lượng gấp 3 lần trung bình được tính là xác nhận đầy đủ 5/5. Nến đó thực chất là **do dự trên khối
lượng lớn** — dấu hiệu phân phối, không phải xác nhận.

V1 tự nói "thân nến đóng thuận chiều" nhưng mã chỉ kiểm dấu.

**V2:** thêm `MinBodyRatio = 0,5`:

```csharp
DirectionConfirmed = SameSign && Math.Abs(Close - Open) / (High - Low) >= MinBodyRatio
```

---

## §5 — Chuẩn hoá điểm theo dữ liệu sẵn có (P1)

### 5.1 Backtest đang KHẮT KHE HƠN chạy thật

Tổng điểm tối đa là 85 (`40 + 30 + 15`, nhóm kỷ luật chỉ trừ). Trong backtest, 10 điểm **cấu trúc
không thể có**: `liquidity.open_interest` và `liquidity.spread_depth`
([BacktestReport.cs:48–53](../../src/MMW.Application/Backtest/Models/BacktestReport.cs)) — trần
thực tế là 75.

`MinScoreToEnter = 55` không được điều chỉnh ở đâu cả
([ScoreBasedPositionSizer.cs:54](../../src/MMW.Application/Trading/Sizing/ScoreBasedPositionSizer.cs),
[BacktestEngine.cs:126](../../src/MMW.Application/Backtest/BacktestEngine.cs)):

| Môi trường | Trần điểm | Ngưỡng | Yêu cầu tương đối |
|---|---|---|---|
| Chạy thật | 85 | 55 | **64,7%** |
| Backtest | 75 | 55 | **73,3%** |

**Backtest lọc gắt hơn chạy thật gần 9 điểm phần trăm.** Nghĩa là mọi expectancy đã đo được là
kết quả của một bộ lọc chặt hơn bộ lọc sẽ chạy bằng tiền thật. Chạy thật sẽ nhận thêm một nhóm
lệnh điểm 55–62 mà **backtest chưa bao giờ nhìn thấy**.

Đây là loại lệch nguy hiểm nhất: nó làm kết quả kiểm thử trông tốt hơn thực tế, theo một chiều,
một cách âm thầm.

### 5.2 Quy tắc V2

Bổ sung vào `ScoringOutcome`:

```csharp
int AvailableMaxPoints   // tổng MaxPoints của các tiêu chí non-discipline có DataAvailable = true
```

Phép so ngưỡng đổi từ tuyệt đối sang tỉ lệ:

```
vào lệnh khi   TotalScore × 85 ≥ MinScoreToEnter × AvailableMaxPoints
```

Áp cùng công thức cho `ScoreThresholdFull` và `ScoreThresholdMax`.

**Hai chốt chặn kèm theo** — chuẩn hoá không được biến "mù dữ liệu" thành lợi thế:

1. Tỉ lệ phủ dưới `MinDataCoveragePercent` ⟹ **veto cứng** `InsufficientData`.
2. Kích thước lệnh nhân thêm `AvailableMaxPoints / TotalMaxPoints` (cột `DataMultiplier` trên
   phiếu). Cùng setup, thiếu dữ liệu thì vào nhỏ hơn.

**Hai chi tiết đã đổi so với bản nháp đầu, phát hiện lúc triển khai:**

- Ngưỡng là **tỉ lệ phần trăm**, không phải số điểm tuyệt đối. Thang điểm được suy ra từ chính bộ
  tiêu chí đang đăng ký (`_totalMaxPoints`), nên một ngưỡng "70 điểm" sẽ tự sai đi mỗi lần
  thêm/bớt tiêu chí — và sai theo hướng không ai để ý: thêm tiêu chí làm ngưỡng dễ hơn, bớt tiêu
  chí làm mọi lệnh bị veto.

- Mặc định là **75%**, không phải 82%. Sàn của kiểm thử lịch sử là 75/85 = **88%**. Đặt ngưỡng
  82% thì chỉ cần mất thêm một tiêu chí 6 điểm nữa (`market.session_quality` chẳng hạn) là cả
  lượt chấm bị veto — và khi đó kiểm thử lại lệch với chạy thật ở một chỗ khác, đúng thứ mà việc
  chuẩn hoá này sinh ra để xoá. 75% cho biên an toàn: mất trọn nhóm thanh khoản (15 điểm) vẫn
  chấm được, mất thêm cả `technical.htf_alignment` (10 điểm) thì dừng.

Điều này giữ đúng tinh thần FR-006 (nguồn chết phải gây đau) nhưng chuyển hình phạt từ "khó vào
lệnh hơn một cách không kiểm soát" sang "vào lệnh nhỏ hơn một cách tường minh" — và xoá bỏ hoàn
toàn lệch backtest ↔ chạy thật.

---

## §6 — Gate kỷ luật mới (P0)

### 6.1 `discipline.open_position`

Chặn setup mới trên mã **đã có vị thế mở**. Đây là điều kiện V1 đã tuyên bố nhưng chưa cưỡng chế
(§1.6).

Kèm trần đồng thời: `MaxConcurrentPositions = 2`.

### 6.2 `discipline.correlated_exposure`

`Symbols = "BTCUSDT,ETHUSDT"` — tương quan thường xuyên trên 0,85. Hai lệnh long full size không
phải hai lệnh, là một lệnh 2R.

Trớ trêu là `LeaderCorrelationCriterion` **thưởng** 4 điểm cho tương quan cao
([MarketCriteria.cs:159–161](../../src/MMW.Application/Trading/Scoring/Criteria/MarketCriteria.cs)).
Đúng ở tầng một lệnh (đi cùng bối cảnh chung), sai ở tầng danh mục.

**Gate:** tổng `FinalSizeR` của các vị thế mở có `|corr| ≥ 0,7` **cùng chiều** không được vượt
`MaxCorrelatedR = 1,0`. Vượt thì co size, không veto.

### 6.3 Lỗ hổng biến động CAO (phân vị 75–90)

`RegimeTable` đã xử lý hai đầu cực đoan
([RegimeTable.cs:55–59](../../src/MMW.Application/Trading/DailyPlanning/RegimeTable.cs)):
`VolatilityRegime.Extreme` → 0,3 / 2 lệnh; ngày có tin tác động cao → 0,4 / 2 lệnh.
`AdaptiveRegimePolicy` phủ nốt `Range` và cuối tuần. Cả bốn đều ổn.

**Lỗ hổng nằm ở khoảng giữa.** `VolatilityBands`
([RegimeTable.cs:109–128](../../src/MMW.Application/Trading/DailyPlanning/RegimeTable.cs)) định
nghĩa `High` là phân vị **75–90**, nhưng không dòng nào của `Rows` khớp `VolatilityRegime.High` —
chỉ có dòng `v == Extreme`. Ngày phân vị 88 vì vậy rơi vào `BaseRow`: **rủi ro 1,0, tối đa 5 lệnh**,
đúng bằng một ngày yên bình.

Hai tầng khác cũng không bù được:

- `DayRegimeClassifier.Label` chỉ trả `DayRegime.HighVolatility` khi `Extreme`. Ở phân vị 88, ngày
  được gán nhãn `TrendUp`/`TrendDown`/`Range` ⟹ `market.day_regime_match` vẫn có thể cho **10/10**.
- `market.volatility_regime` là tầng duy nhất phản ứng, và nó chỉ trừ: 2/6 thay vì 6/6 — **4 điểm
  trên 85**.

Với khung giữ lệnh 1–4 tiếng và dừng lỗ theo ATR, phân vị 75–90 chính là vùng dừng lỗ bị quét
nhiều nhất: ATR đủ cao để nến chọc thủng mọi mức, chưa đủ cao để hệ thống coi là bất thường.

**V2 — thêm một dòng vào `RegimeTable.Rows`:**

```csharp
((_, v, _) => v == VolatilityRegime.High,
    new RegimeParameters(AllowedDirections.Both, 0.6m, 3)),
```

Đặt ở `RegimeTable` chứ không ở `AdaptiveRegimePolicy`: đây là tham số của bảng FR-019 (theo
biến động), không phải cap nhịp giao dịch theo lịch. `Resolve` dùng `Math.Min` nên dòng mới chỉ có
thể siết, không thể nới.

Kèm theo: `DayRegimeClassifier.Label` trả `DayRegime.HighVolatility` từ `High` trở lên, để
`market.day_regime_match` thôi cho 10/10 trên một ngày biến động cao.

---

## §7 — Kế hoạch thực thi V2

Thay toàn bộ mục "Kế hoạch thực thi" của V1.

### 7.1 Range — fade tại biên

- Vào lệnh: **lệnh limit** tại biên ± 0,25 ATR, hết hạn sau 8 nến. Không vào market.
- Dừng lỗ: ngoài pivot biên + 0,30 ATR.
- Mục tiêu: mức giữa range hoặc biên đối diện, chọn cái cho **≥ 1,6R gộp**. Không đạt ⟹ bỏ qua.
- Chốt toàn bộ tại mục tiêu. Không runner.
- **Bỏ hoàn toàn quy tắc "chốt toàn bộ tại 1R" của V1** (§0).

### 7.2 Standard — tiếp diễn xu hướng

- Vào lệnh: **limit** tại retest mức đã phá hoặc EMA20 khung vào lệnh, hết hạn sau 6 nến.
- Chốt 50% tại mức cấu trúc gần nhất ≥ 1,2R.
- Phần còn lại: dời dừng lỗ về **hoà vốn + phí** (§7.5), trailing sau đáy/đỉnh xoay 3 nến.
- Trần 3R.

### 7.3 Trend mạnh — hai tranche, neo vào cấu trúc

Điều kiện giữ nguyên V1: regime ngày thuận chiều, structure ≥ 8/10, volume = 5/5.

> **Đính chính.** Bản nháp ghi thêm "điểm ≥ 70". Code chưa từng kiểm điều đó — xem
> [TradeExecutionPlanner.cs:64](../../src/MMW.Application/Trading/Execution/TradeExecutionPlanner.cs)
> và test `Trend_co_structure_va_volume_manh_khong_bat_buoc_phai_dat_70_diem` chạy ở điểm 55.
> Đây là chủ ý của V1: structure 8/10 kèm volume 5/5 là bằng chứng vào lệnh trực tiếp, còn tổng
> điểm gộp cả những tiêu chí không nói gì về chân vào lệnh. Tài liệu đã sai, không phải code.

Khác V1 ở hai điểm:

- **Hai tranche 60/40**, không phải ba đều nhau.

  > **Đính chính lý do.** Bản nháp viết tranche thứ ba "có tỉ lệ rủi ro/phần thưởng tệ hơn". Sai:
  > entry gần dừng lỗ hơn thì RR hình học **tốt hơn**, không tệ đi. Hai lý do thật là:
  >
  > 1. **Chi phí.** Khối lượng tỉ lệ nghịch với khoảng cách tới stop, nên tranche vào sâu cõng
  >    nhiều hợp đồng hơn và **tốn nhiều phí tính theo R hơn**. Đo được: cùng ngân sách 1R,
  >    hai tranche tốn 0,028R phí so với 0,019R của một điểm vào — đắt hơn 47%
  >    (`Scale_in_ton_nhieu_phi_hon_lenh_mot_diem_vao_cung_ngan_sach`).
  > 2. **Bất đối xứng khớp lệnh.** Với dữ liệu OHLC, tranche pullback nằm giữa giá vào và dừng lỗ
  >    KHÔNG THỂ bị bỏ qua trên đường giá rơi xuống stop — nhưng lệnh thắng chạy thẳng thì không
  >    bao giờ khớp nó. Nghĩa là **thua đủ ngân sách, thắng chỉ một phần**
  >    (`Lenh_thua_luon_trien_khai_du_ngan_sach_con_lenh_thang_thi_khong`).
  >
  > Điểm 2 là lập luận định lượng cho việc dồn trọng số về tranche đầu. Nhưng chỉ nên đổi trọng
  > số **cùng lúc** với state machine ở §7 — dồn 40/35/25 khi tranche sâu vẫn là lệnh limit đặt mù
  > là làm nửa vời. Hiện tại giữ chia đều **rủi ro**, không phải chia đều số lượng.
- Tranche 2 đặt tại **retest cấu trúc** (mức đã phá / EMA20), không phải mốc −0,25R cứng
  ([TradeExecutionPlanner.cs:33](../../src/MMW.Application/Trading/Execution/TradeExecutionPlanner.cs)).
  Giá hồi về đúng 0,25R là một sự trùng hợp, không phải một mức.
- Chốt 50% tại 1,5R, runner trailing sau cấu trúc, không cap cứng 3R.
- Tranche chưa khớp bị huỷ khi TP1 hoặc stop xảy ra — giữ nguyên V1, đã đúng.

### 7.4 Dừng theo thời gian (mới, áp cho mọi chế độ)

> Sau `TimeStopBars = 16` nến (4 giờ trên 15m) mà lệnh chưa từng chạm +0,5R ⟹ đóng tại giá thị
> trường.

Setup intraday không chạy trong 4 tiếng thường không chạy. Giá trị thật của quy tắc này không nằm
ở expectancy trực tiếp mà ở việc **giải phóng ngân sách rủi ro** cho setup tốt hơn. Phải đo riêng:
`ByExitReason` trong `BacktestReport`.

> **Đính chính.** Bản nháp còn ghi "giải phóng ngân sách số lệnh/ngày". Không đúng:
> `TradesToday` đếm theo `OpenedAtUtc`
> ([BacktestEngine.cs:322](../../src/MMW.Application/Backtest/BacktestEngine.cs)), nên đóng lệnh
> sớm KHÔNG trả lại quota. Đó là thiết kế đúng — quota số lệnh/ngày là hạn mức về **số lần ra
> quyết định**, không phải hạn mức về số vị thế đang giữ; trả lại quota cho lệnh đóng sớm sẽ
> thưởng cho việc vào lệnh vội. Giữ nguyên hành vi, sửa mô tả.

### 7.5 Hoà vốn phải tính cả phí

[SimulatedTradePosition.cs:155](../../src/MMW.Application/Backtest/SimulatedTradePosition.cs) —
`Stop = WeightedEntry()`. Dừng lỗ tại đúng giá vào lệnh **không phải hoà vốn**: nó vẫn lỗ đúng
bằng phí hai chiều (0,1–0,4R theo §0).

**V2:** `Stop = WeightedEntry() ± (roundTripFeeInPrice + 0,05 × unitRisk)`.

### 7.6 Bảng tóm tắt thay đổi

| | V1 | V2 |
|---|---|---|
| Range — mục tiêu | 1R, chốt toàn bộ | ≥ 1,6R cấu trúc, bỏ qua nếu không đủ |
| Range — vào lệnh | market | limit tại biên, hết hạn 8 nến |
| Range — vị trí | bất kỳ | chỉ ≤ 25% / ≥ 75% biên độ |
| Standard — vào lệnh | market | limit tại retest, hết hạn 6 nến |
| Standard — mục tiêu | 1,5R cố định | mức cấu trúc, 50% + trailing |
| Trend mạnh — tranche | 3 × 1/3 tại 0/−0,25R/−0,5R | 2 × (60/40) tại cấu trúc |
| Runner | cap 3R | trailing theo cấu trúc |
| Dừng lỗ | 1,5 ATR cố định | cấu trúc, kẹp [1,0 – 3,0] ATR |
| Hoà vốn | giá vào lệnh | giá vào lệnh + phí |
| Dừng theo thời gian | không có | 16 nến chưa đạt 0,5R |

---

## §8 — Mô hình chi phí backtest (P0 — không có mục này thì §3 và §7 không đo được)

Mô hình hiện tại chỉ có **một** loại phí (`BacktestTakerFeePercent`) và áp trượt giá bất lợi cho
**mọi** lần khớp, kể cả lệnh limit
([SimulatedTradePosition.cs:175–181](../../src/MMW.Application/Backtest/SimulatedTradePosition.cs)).
V2 chuyển phần lớn chân vào lệnh sang limit, nên nếu không sửa mô hình thì backtest sẽ **phạt
đúng cái cải tiến mà nó cần đo**.

**Bổ sung `EngineSetting`:**

| Trường | Mặc định | Ghi chú |
|---|---|---|
| `BacktestMakerFeePercent` | 0,02 | Binance USDⓈ-M VIP0 |
| `BacktestLimitFillRequiresThrough` | **true** | Mô hình hàng đợi, xem 8.2 |

### 8.1 Phí và trượt giá theo LOẠI LỆNH của từng chân

Không phải một mức chung, và cũng không phải một giả định — loại lệnh quyết định cả hai:

| Chân | Loại lệnh | Phí | Trượt giá |
|---|---|---|---|
| Vào lệnh, chân đầu | thị trường | taker | có (`BacktestEntrySlippageBps`) |
| Vào lệnh, chân bổ sung | limit chờ | **maker** | **không** |
| Dừng lỗ | stop-market | taker | có (`BacktestStopSlippageBps`) |
| Chốt lời (TP1 và runner) | limit chờ | **maker** | **không** |
| Đóng cưỡng bức cuối kỳ | thị trường | taker | có |

Lệnh limit đang chờ sẵn trong sổ khớp **đúng mức đã đặt hoặc tốt hơn, không bao giờ tệ hơn**. V1
áp trượt giá bất lợi cho mọi chân, tức là phạt một chuyện về nguyên tắc không xảy ra — và phạt
đúng vào cái cải tiến mà V2 cần đo.

Cái giá thật của lệnh limit không phải trượt giá mà là **rủi ro không khớp**. Cái giá đó được
tính ở 8.2 và 8.3.

`PlannedEntryTranche` mang thêm cờ `IsLimit`. Chân đầu **bắt buộc** là lệnh thị trường: vị thế
được mở bằng cách khớp nó ngay, nên chân đầu là limit nghĩa là lệnh có thể không bao giờ tồn tại
— đó là một *lệnh chờ*, thuộc tầng khác. `SimulatedTradePosition.Open` ném lỗi nếu gặp.

### 8.2 Hai mô hình hàng đợi, và kết quả phải đứng vững ở cả hai

Nến không có sổ lệnh, nên vị trí hàng đợi phải là giả định tường minh:

| `RequiresThrough` | Quy tắc | Ý nghĩa |
|---|---|---|
| `false` — **lạc quan** | chạm là khớp (`Low <= price`) | lệnh luôn đứng đầu hàng đợi — biên TRÊN |
| `true` — **thận trọng** | phải xuyên qua (`Low < price`) | luôn phải đợi hết phần xếp trước |

Hai điểm dễ làm sai và cả hai đều đã có test:

- **Chốt lời cũng là lệnh limit**, nên chịu đúng quy tắc này. Thận trọng ở chân vào mà lạc quan ở
  chân ra là tự nghiêng kết quả: lệnh khó vào hơn nhưng thoát dễ hơn.
- **Dừng lỗ KHÔNG chịu quy tắc này.** Nó là stop-market, chạm mức là kích hoạt. Áp "phải xuyên
  qua" cho dừng lỗ sẽ cho lệnh sống sót những cây nến mà ngoài đời nó đã bị quét — kiểu lạc quan
  nguy hiểm nhất, vì nó làm đẹp đúng phần rủi ro.

Điều kiện chấp nhận: **một cải tiến chỉ tồn tại ở mô hình lạc quan là cải tiến của giả định,
không phải của chiến lược.**

### 8.3 Hết hạn lệnh chờ

`LimitEntryExpiryBars` được thực thi: chân limit chưa khớp bị huỷ sau ngần ấy nến, và huỷ **trước**
khi xét khớp của cây nến đó. Một nhịp hồi mất hơn 6 nến để chạm mức thì không còn là nhịp hồi, nó
là một cú khựng — khớp lúc đó là gia tăng vị thế đúng lúc động lượng đã tắt.

Huỷ vì **hết hạn** được đếm tách khỏi huỷ vì **lệnh đã chốt lời/dừng lỗ**. Gộp chung sẽ che mất
trường hợp "mức đặt sai chỗ" — trường hợp duy nhất cần sửa.

Không có hết hạn thì hai mô hình ở 8.2 hầu như chỉ dời **thời điểm** khớp chứ không đổi **tỉ lệ**
khớp, và phép so sánh mất phần lớn ý nghĩa.

### 8.4 Số liệu khớp lệnh trong báo cáo

`BacktestReport` bổ sung `MakerFeeR`, `TakerFeeR`, số chân limit đặt/khớp/hết hạn, cùng ba tỉ lệ
dẫn xuất. `BacktestLimitations` ghi rõ mô hình hàng đợi đã dùng và tỉ lệ khớp — và **tự cảnh báo
khi tỉ lệ khớp dưới 60%**: dưới mức đó, so sánh với phiên bản vào lệnh bằng lệnh thị trường không
còn công bằng, vì hai bên đang chạy hai kế hoạch khác nhau chứ không phải cùng một kế hoạch với
chi phí khác nhau.

---

## §8a — Thước đo phải đúng trước khi tối ưu (P0, làm TRƯỚC §8)

Hai lỗi dưới đây không làm backtest báo lỗi. Chúng làm nó báo một con số hợp lý mà sai — và con
số đó chính là `ExpectancyR`, thứ mọi quyết định tối ưu về sau dựa vào. Vì vậy mục này đứng trước
toàn bộ phần còn lại.

### 8c.1 Trọng số tranche: rủi ro ≠ số lượng

V1 chia đều **số lượng** giữa các tranche dùng chung một dừng lỗ. Tranche vào sâu nằm gần stop
hơn nên rủi ro ít hơn với cùng số hợp đồng:

| Khớp | Rủi ro thật (V1) | Rủi ro thật (V2) |
|---|---|---|
| Cả ba tranche tại 0 / −0,25R / −0,5R | (1 + 0,75 + 0,5)/3 = **0,75R** | **1,00R** |
| Chỉ tranche đầu | **0,33R** | 0,33R (= trọng số của nó) |

Hệ quả không phải "vào lệnh nhẹ tay" mà là **`RealizedR` của lệnh scale-in không cùng đơn vị với
lệnh một điểm vào**. Gộp expectancy của hai loại là cộng táo với cam.

**V2:**

```
quantity[i] = SizeR × riskWeight[i] / |plannedPrice[i] − initialStop|
```

`PlannedEntryTranche.SizeFraction` đổi tên thành `RiskWeight` để đơn vị không còn mập mờ. Khối
lượng chốt theo giá **dự kiến** — đúng như chạy thật tính khối lượng trước khi gửi lệnh — nên
trượt giá hiện ra thành khoản lỗ lớn hơn ngân sách một chút thay vì bị giấu đi.

Hai hệ quả chỉ lộ ra sau khi tính đúng, cả hai đều bất lợi cho scale-in và cả hai đều đã có test:

- **Tranche sâu đắt hơn.** Cùng ngân sách 1R: hai tranche tốn 0,028R phí, một điểm vào tốn
  0,019R — **đắt hơn 47%**.
- **Thua đủ, thắng thiếu.** Tranche pullback nằm giữa giá vào và stop luôn khớp trên đường rơi
  xuống stop, nhưng lệnh thắng chạy thẳng thì không bao giờ khớp nó.

Bất biến được chốt bằng test và bằng kiểm tra ở cổng vào: mọi tranche phải nằm đúng phía dừng lỗ
và cách nó tối thiểu **0,25 × UnitRisk**. Khối lượng tỉ lệ nghịch với khoảng cách này, nên một
tranche đặt sát stop sinh đòn bẩy ngoài ý muốn mà báo cáo không bao giờ tố giác.

### 8c.2 Phí vốn

`FundingRateArchive` đã có sẵn từ T001, nhưng chỉ được dùng để **chấm điểm**
`market.funding_crowding`. Nó chưa từng bị trừ khỏi P&L — chính `BacktestLimitations` cũng thừa
nhận điều đó bằng một dòng "Bỏ qua phí vốn phải trả khi giữ vị thế qua mốc thanh toán".

Với dừng lỗ ~0,27% giá, một mốc 0,01% tốn **~0,037R**. Baseline có expectancy −0,04R: phí vốn
cùng bậc độ lớn với **toàn bộ** khoảng cách từ chiến lược tới hoà vốn.

**V2:**

```
fundingR = markPrice × fundingRate × openQuantity        (Long trả khi rate dương)
```

Thời điểm tính là điểm mấu chốt về tính đúng đắn, và nó **chính xác** chứ không phải xấp xỉ:

> Thanh toán SAU khi đã xử lý stop/target của cây nến, cho các vị thế còn mở.

Mốc funding (00:00/08:00/16:00 UTC) rơi đúng biên nến trên lưới 15 phút. Vị thế sống sót trọn cây
nến thì thật sự còn mở tại mốc đó và phải trả; vị thế bị dừng trong cây nến đã thoát trước mốc
nên không phải trả; vị thế mở tại cây nến đó vào lệnh sau mốc nên cũng không. Không có tình huống
nào phải đoán.

Chỉ tính trên phần khối lượng **đang** mở: tranche chưa khớp không nắm giữ gì, tranche đã chốt
một nửa chỉ còn nửa kia phải trả.

### 8c.3 Ba cột chi phí quy ra R

`TotalFees` (% khối lượng) và `TotalSlippage` (đơn vị giá) không cộng được ngang các mã: 0,04% của
BTC và 0,04% của một mã giá thấp là hai khoản tiền khác hẳn nhau, và cùng một khoản phí ăn vào R
nhiều hay ít còn tuỳ dừng lỗ rộng bao nhiêu.

Thêm `TotalFeeR`, `TotalFundingR`, `TotalSlippageR` vào `BacktestReport` và `BacktestRun`
(migration `AddBacktestCostInR`). **Hai cột cũ giữ nguyên nghĩa** — đổi nghĩa một cột đã có dữ
liệu sẽ làm mọi lần chạy trước im lặng nói dối.

⚠️ Các lần chạy cũ có `TotalFundingR = 0`, và 0 ở đây nghĩa là **chưa đo**, không phải "không tốn
phí vốn". Muốn so sánh có nghĩa thì phải chạy lại.

---

## §8b — Trạng thái triển khai

| Bước | Nội dung | Trạng thái |
|---|---|---|
| 0a | §1 — sửa lỗi tất định `PriceActionAnalyzer` | ✅ xong |
| 0b | Bốn điểm nhỏ (tương quan, dải RSI, biến động cao, thân nến) | ✅ xong |
| 0c | §5 — chuẩn hoá điểm theo dữ liệu đo được | ✅ xong |
| 2a | §6.1 + §6.2 — gate vị thế mở và rủi ro tương quan | ✅ xong |
| 2b | §3 — dừng lỗ/mục tiêu theo cấu trúc + rào 1,6R | ✅ xong |
| 1 | §8a — trọng số rủi ro tranche, phí vốn, ba cột chi phí theo R | ✅ xong |
| — | Migration `AddAdaptiveExecutionV2` (18 cột) | ✅ đã áp vào DB 2026-08-04 |
| — | Migration `AddBacktestCostInR` (3 cột) | ✅ đã áp vào DB 2026-08-04 |
| 2 | §8 — phí theo loại lệnh, hai mô hình hàng đợi, hết hạn lệnh chờ | ✅ xong |
| — | Migration `AddLimitFillModel` (1 cột) | ✅ đã áp vào DB 2026-08-04 |
| 4 | §2 — siết chất lượng price action, tuổi mẫu hình, quét một lần | ✅ xong |
| 3 | §4 — chấm hai chiều, ràng buộc vị trí range, biên chọn chiều | ✅ xong |
| — | Migration `AddDirectionSelection` (4 cột) | ✅ đã áp vào DB 2026-08-04 |
| 5 | §7 kế hoạch thực thi V2 (limit vào lệnh, tranche cấu trúc, dừng theo thời gian) | ⬜ một phần (mục tiêu Range đã sửa) |

Bộ kiểm thử: **826 test xanh**, tăng từ 705 trước khi bắt đầu và từ 777 trước bước 3–4. Cả bốn
project biên dịch 0 lỗi.

⚠️ **Bước 4 làm TRƯỚC bước 3, và hai bước cùng nằm trong một working tree.** Lý do kỹ thuật: §4
chấm hai chiều, và nếu `PriceActionSignals` còn phụ thuộc chiều thì mỗi lượt chấm phải quét lại
điểm xoay và tính lại RSI hai lần. §2.7 (quét một lần, đặt vào `ScoringContext`) vì vậy phải xong
trước — và làm nó đúng nghĩa là làm luôn phần còn lại của §2.

Hệ quả cho việc đo: xem lại thứ tự backtest ở §9.

### Những chỗ triển khai lệch khỏi bản nháp

- **§6.3 viết sai ban đầu.** Bản nháp nói `EventDay` và `HighVolatility` "không bị cap gì cả".
  Sai — `RegimeTable` đã cap cả hai (0,4/2 và 0,3/2). Lỗ hổng thật nằm ở vùng biến động **CAO
  (phân vị 75–90)**, vùng duy nhất không khớp dòng nào và rơi vào dòng nền với rủi ro 1,0 và
  5 lệnh. Đã sửa doc và vá bằng một dòng mới trong `RegimeTable.Rows`.

- **`MinDataCoveragePercent` là tỉ lệ, mặc định 75% chứ không phải 70 điểm/82%.** Lý do ở §5.

- **Rào chỗ chạy là một tiêu chí 0 điểm**, `technical.structural_room`, chứ không nhét vào
  `liquidity.zone_position`. Giữ nguyên tổng 85 nên ba ngưỡng 55/70/85 không phải tính lại, và
  nó bật ra thành một dòng phiếu riêng trả lời đúng một câu hỏi.

- **Mục tiêu chế độ `Range` đã phải sửa ngay trong bước 2b.** Rào 1,6R và mục tiêu cứng 1R của
  V1 mâu thuẫn trực tiếp: hệ thống sẽ loại một setup vì thiếu chỗ chạy, rồi với setup được nhận
  lại tự vứt đi phần chỗ chạy đó. Không thể để mâu thuẫn này tồn tại qua một lần chạy backtest.

- **Thứ tự triển khai đã đổi sau bản review ngoài.** Bản nháp xếp tối ưu trước, đo sau. Sai về
  quy trình: mọi so sánh trước khi có §8a đều chạy trên một thước hỏng. §8a được đưa lên đầu và
  các bước còn lại đánh số lại theo đó.

- **Bản nháp §8 chỉ nói tới quy tắc "phải xuyên qua" cho chân VÀO lệnh.** Thiếu: chốt lời cũng là
  lệnh limit và phải chịu cùng giả định, còn dừng lỗ là stop-market và **không** được chịu. Áp
  nhầm theo hướng ngược lại sẽ làm đẹp đúng phần rủi ro.

- **Hết hạn lệnh chờ được kéo từ §7 lên làm cùng §8.** Không có nó, hai mô hình hàng đợi hầu như
  chỉ dời thời điểm khớp chứ không đổi tỉ lệ khớp — mà tỉ lệ khớp mới là thứ cần so.

- **EF lại sinh sai giá trị mặc định, lần thứ hai.** `BacktestLimitFillRequiresThrough` nhận
  `defaultValue: false` vì đó là CLR default của `bool`, trong khi mặc định của thực thể là
  `true`. Để nguyên thì mọi hàng cấu hình cũ lặng lẽ chuyển sang mô hình **lạc quan** — đúng cái
  giả định làm đẹp kết quả. Quy tắc từ đây: **giá trị mặc định do EF sinh cho cột mới không bao
  giờ được tin**, phải đối chiếu với mặc định của thực thể trước khi commit.

- **`HomeController` không build được từ bước 2a tới giờ.** `DisciplineContext` nhận thêm hai
  trường `required`, nhưng `MMW.Web` đang chạy nên khoá DLL và không lần nào build được để lộ ra.
  Đã sửa: bảng kỷ luật trên dashboard truyền mã **rỗng**, và đó là câu trả lời đúng ngữ nghĩa —
  bảng đó hỏi "hiện giờ tôi có được phép giao dịch không", chưa có mã và chiều cụ thể. Bài học:
  build đủ bốn project sau mỗi bước, kể cả khi phải build ra thư mục khác để né khoá DLL.

- **Migration phải điền `defaultValue` bằng tay.** EF sinh mặc định 0 cho mọi cột, và ở đây 0 là
  một cấu hình hỏng nhưng chạy được: `MaxConcurrentPositions = 0` chặn mọi lệnh,
  `StopAtrMultipleMax = 0` làm mọi dừng lỗ vượt trần. Hàng `EngineSettings` hiện có sẽ nhận đúng
  những giá trị đó mà không có ngoại lệ nào để lần theo.

  > **Bổ sung sau `AddDirectionSelection`.** Lần này mặc định EF sinh ra ĐÚNG và được giữ nguyên.
  > Quy tắc là không bao giờ **tin** chúng, không phải luôn phải **sửa** chúng: bốn cột mới nằm
  > trên `EntryScorecards` — một bảng kiểm toán, không phải bảng cấu hình — nên `DirectionalScore
  > = 0` đọc đúng là "phiếu lập trước §4, chưa từng có phép so chiều nào", giống hệt vai trò của
  > `TotalMaxPoints = 0`. Ba cột còn lại nullable và NULL nói đúng điều cần nói.

### Bước 3 và 4 — những chỗ lệch khỏi bản nháp

- **`DirectionalScore` không đổi được kết quả của phép so, và đó là một phát hiện chứ không phải
  một lỗi.** Bản review nói so tổng 85 điểm là "làm loãng đúng phần cần so". Về mặt số học thì
  không: các tiêu chí không đổi theo chiều cho hai bên **đúng cùng** số điểm, nên chúng triệt tiêu
  trong phép trừ — `DirectionalScore(L) − DirectionalScore(S)` bằng đúng `Total(L) − Total(S)`.
  Con số riêng vẫn được tính và ghi lại, vì hai lý do khác: nó làm ngưỡng đọc được (8 trên thang
  **59** điểm đổi-theo-chiều, không phải 8 trên 85 mà phần lớn không liên quan tới chiều), và nó
  là dữ liệu để về sau chỉnh biên có căn cứ.

- **Cờ `IsDirectional` là khai báo BẮT BUỘC trên `IScoreCriterion`, không có giá trị mặc định.**
  Khai báo sai theo hướng "nói không nhưng thật ra có" là lỗi im lặng một chiều: phần điểm bị bỏ
  quên làm biên hai chiều nhỏ đi, tức engine chọn chiều dựa trên ít bằng chứng hơn nó tưởng, mà
  tổng điểm vẫn đúng nên không có gì báo. Có bộ gác chấm mọi tiêu chí ở cả hai chiều trên ba hình
  dạng chuỗi giá, cộng một khẳng định chống-rỗng để bộ gác không xanh vì không kiểm gì.

  Kết quả kiểm kê: **9 tiêu chí đổi theo chiều — 59 điểm** (`htf_alignment` 10,
  `market_structure` 10, `entry_location` 8, `momentum` 7, `volume_confirmation` 5,
  `day_regime_match` 10, `funding_crowding` 4, `zone_position` 5, và `structural_room` 0 điểm
  nhưng có quyền LOẠI hẳn một chiều) và **5 không đổi — 26 điểm** (`volatility_regime` 6,
  `session_quality` 6, `leader_correlation` 4, `open_interest` 5, `spread_depth` 5).

  `liquidity.open_interest` nằm ở nhóm "không đổi" vì mã hiện tại chỉ đo lượng hợp đồng mở tăng
  hay giảm mà **không ghép với hướng giá** — trái với chính chú thích của nó. Ghép thêm hướng là
  một thay đổi nghiệp vụ, không phải một dòng khai báo; khi nào làm thì đổi cờ cùng lúc.

- **Chiều bị veto cứng bị LOẠI khỏi cuộc so, không bị coi là 0 điểm.** Veto làm vòng chấm dừng
  giữa chừng nên điểm của chiều đó là một tổng dở dang; đem nó vào phép trừ là so hai con số khác
  đơn vị, và nó sẽ tặng chiều còn lại một biên rộng bịa đặt. Hệ quả cố ý: **khi chỉ còn một ứng
  viên thì không có biên nào để đòi hỏi** — chiều kia không thua điểm, nó bị cấm.

- **Trên ngày đi ngang, vị trí chốt chiều TRƯỚC khi chấm, nên biên 8 điểm không áp.** Đây là chỉ
  dẫn tường minh của §11.3 ("xác định chiều từ vị trí trong range trước, rồi mới chấm điểm") và nó
  mâu thuẫn nhẹ với §4.2 vốn xếp ràng buộc vị trí thành "thêm vào" phép so hai chiều. Chọn theo
  §11.3: vị trí là một sự kiện ĐO ĐƯỢC, còn chênh lệch vài điểm giữa hai bảng chấm thì không.

  Để mâu thuẫn này kiểm chứng được thay vì phải tranh luận tiếp, **chiều bị vị trí loại vẫn được
  chấm và điểm của nó vẫn ghi vào phiếu** (`OppositeScore`, `OppositeDirectionalScore`). Sau lần
  backtest tới, câu "quy tắc biên độ có đang chọn nhầm bên không" trả lời được bằng truy vấn.

- **Hai lý do từ chối mới, không phải ba.** `NotAtRangeEdge` (314) và `DirectionUnclear` (315).
  Trường hợp "ngày range mà không dựng được biên độ" dùng lại `InsufficientData` (309): nó đúng
  nghĩa là thiếu dữ liệu, và nó gom đúng chỗ mà người đọc thống kê sẽ tìm.

- **`NotAtRangeEdge` chặn TRƯỚC khi chấm điểm**, nên phiếu đó không có dòng tiêu chí nào. Đó là
  chủ ý: chấm rồi vứt là tốn CPU cho một kết luận đã có, và một phiếu đầy dòng điểm kèm kết cục
  "bị từ chối vì vị trí" đọc như thể điểm số có tham gia quyết định.

- **Biên chọn chiều triệt tiêu KÍCH THƯỚC, không chỉ đổi nhãn kết cục.** Một phiếu ghi
  `FinalSizeR > 0` kèm `Outcome = Vetoed` là mâu thuẫn nội tại, và mọi thống kê đọc cột kích thước
  sẽ đọc phải nó mà không có gì báo.

- **`PriceActionSignals` trở thành KHÔNG phụ thuộc chiều lệnh.** Cả hai nhánh Fibonacci được tính
  sẵn, nên một lần quét dùng cho cả hai chiều. Nhờ vậy chấm hai chiều vẫn **rẻ hơn** một lần chấm
  của phiên bản trước V2, vốn quét price action ba lần cho một chiều.

- **Tuổi mẫu hình ăn vào điểm số ở hai chỗ, không phải một.** `technical.momentum` cộng
  `clamp(round(hợp lưu ròng), −2, +2)` như cũ; `technical.market_structure` **nội suy** 3→8 (chưa
  có BOS) và 6→8 (đã phá, chưa retest) theo trọng số. Nếu chỉ chặn ở mốc hết hạn thì một mẫu hình
  11 nến tuổi vẫn được đủ 8/10 điểm rồi rơi thẳng xuống 3 ở nến thứ 12 — vẫn là công tắc, chỉ
  dịch chỗ.

- **`BreakoutAge` đo từ đầu DẢI LIÊN TỤC đang có hiệu lực, không phải từ lần phá đầu tiên trong
  cửa sổ.** Một cú phá thất bại rồi phá lại là mẫu hình mới, không phải mẫu hình cũ già đi; đo từ
  lần phá đầu sẽ khai tử đúng những tín hiệu vừa hình thành. Kèm theo: giá đóng hiện tại phải CÒN
  ở bên kia neckline — phá rồi tụt lại là cú phá hỏng, không phải bằng chứng thuận chiều còn trẻ.

- **Ràng buộc chéo của vai-đầu-vai hiện đang THỪA, và được giữ lại có ý thức.** Với
  `ShoulderToleranceAtr = 0,5` và `HeadProminenceAtr = 0,8`, điều kiện `|LS − RS| < prominence`
  luôn đúng vì `0,5·ATR < 0,8·ATR`. Giữ lại vì nó neo Ý ĐỊNH độc lập với hai con số — đúng thứ V1
  đánh mất khi để dung sai vai (0,60) lớn gấp 2,4 lần độ nhô của đầu (0,25). Cái giá phải nói
  thẳng: **hiện chưa có test nào bắt được nó**, vì không bộ dữ liệu nào chạm tới được nhánh đó.

- **Fibonacci: hồi sâu hơn nghĩa là giá THẤP hơn.** Dễ đọc ngược. Với nhịp tăng 90 → 120, golden
  pocket 0,5–0,618 là khoảng giá **101,46 – 105**, còn mức 0,382 nằm ở giá 108,54 — cao hơn cả
  vùng. Ghi ra đây vì bản nháp đầu của chính test này đã sai đúng chỗ đó.

- **Hai vùng hồi ngoài golden pocket KHÔNG được ghi nhận.** §2.6 nói 0,382–0,5 và 0,618–0,786
  "được ghi nhận nhưng không cộng điểm". Bỏ phần ghi nhận: thêm hai trường không ai đọc vào một
  bản ghi đang được tính cho mỗi mốc chấm là chi phí có thật đổi lấy một khả năng giả định. Khi
  nào có tiêu chí cần chúng thì thêm.

- **Hết hạn ở `age == PatternMaxAgeBars`, không phải sau đó.** §2.5 viết `AgeBars <= max`; mã cho
  trọng số `1 − age/max`, tức bằng 0 đúng tại mốc. Chênh lệch là một nến và nó nghiêng về phía
  chặt hơn, nhưng quan trọng hơn là nó xoá được khe hở giữa hai định nghĩa "hết hạn" và "vừa đủ
  hạn" — hai thứ mà nếu tách rời sẽ lệch nhau ở lần chỉnh đầu tiên.

---

## §9 — Thứ tự triển khai và điều kiện chấp nhận

Tuần tự, một bước một backtest. **Không gộp.** Gộp hai thay đổi rồi thấy expectancy tăng thì không
biết cái nào tăng, cái nào che lỗ của cái kia.

| # | Nội dung | Kỳ vọng đo được | |
|---|---|---|---|
| 0a–0c | §1 (sửa lỗi) + bốn điểm nhỏ + §5 (chuẩn hoá điểm) | Baseline sạch. **Số lệnh sẽ tăng** vì backtest hết khắt khe hơn thật — điều chỉnh đúng, không phải hồi quy | ✅ |
| 2a | §6.1 + §6.2 (gate vị thế mở, rủi ro tương quan) | Max drawdown và chuỗi thua giảm; expectancy gần như không đổi | ✅ |
| 2b | §3 (dừng lỗ + mục tiêu cấu trúc) | **Đòn bẩy lớn nhất.** Expectancy tăng rõ; số lệnh giảm vì rào 1,6R | ✅ |
| 1 | §8a (rủi ro tranche, phí vốn, chi phí theo R) | **Không phải cải tiến — là sửa thước đo.** Expectancy sẽ XẤU ĐI, và con số xấu đi đó mới là con số đúng | ✅ |
| 2 | §8 (phí theo loại lệnh, hai mô hình hàng đợi, hết hạn) | Chi phí giảm nhờ maker; số lệnh giảm nhẹ vì chân limit không phải lúc nào cũng khớp | ✅ |
| 4 | §2 (chất lượng price action) | Số lệnh giảm, win rate tăng; expectancy có thể đi ngang | ✅ đã đo — số lệnh giảm mạnh, win rate **giảm** |
| 3 | §4 (chọn chiều) | Win rate ngày `Range` tăng; số lệnh ngày range giảm mạnh | ✅ đã đo — số lệnh range giảm mạnh, biên chọn chiều **không loại lệnh nào** |
| 5 | §7 (kế hoạch thực thi) | Expectancy tăng; max drawdown không được xấu hơn | ⬜ |
| 6 | §6.3 + regime override trong ngày | Bắt được ngày chuyển sang trend mà không dao động qua lại | ⬜ |

⚠️ **Bước 3 và 4 đã viết xong nhưng CHƯA bước nào được đo, và chúng nằm chung một working tree.**
Quy tắc "một bước một backtest" vẫn còn hiệu lực và cách tách chúng ra như sau:

1. **Lượt A — `DirectionMarginPoints = 0`.** Biên chọn chiều không loại lệnh nào nữa. So với mốc
   của bước 2 trên **các regime KHÁC `Range`** (`ByRegime` trong `BacktestReport` đã tách sẵn):
   ngày không phải range không chịu ràng buộc vị trí, nên phần chênh lệch ở đó là của §2 cộng với
   phép chọn-bên-cao-điểm.

2. **Lượt B — trả `DirectionMarginPoints` về 8.** Chênh lệch giữa B và A là tác dụng của riêng
   biên chọn chiều.

3. **Ngày `Range`** đọc riêng ở cả hai lượt: đó là nhóm duy nhất chịu ràng buộc vị trí, và số lệnh
   ở nhóm này được kỳ vọng giảm mạnh.

> **Không tách sạch được tuyệt đối, và phải nói thẳng.** Ràng buộc vị trí trong biên độ KHÔNG có
> tham số nào tắt được (`RangeEdgePercent` bị chặn trong khoảng mở (0, 50), nên mọi giá trị hợp lệ
> vẫn thu về đúng một chiều ở mỗi biên). Việc chấm hai chiều rồi chọn bên cao điểm hơn cũng không
> tắt được. Cách bù là **tách theo regime** như trên, chứ không phải thêm một cờ chỉ phục vụ việc
> đo — một nhánh mã không ai chạy khi giao dịch thật là một nhánh sẽ mục ruỗng trong im lặng.

Cả hai lượt đều phải chạy hai lần theo `BacktestLimitFillRequiresThrough` (điều kiện chấp nhận 6).

### Kết quả đo — 2026-08-04, BTCUSDT + ETHUSDT, 2024-01-01 → 2025-12-31

Bốn lần chạy, tài khoản 1, sau khi áp migration `AddDirectionSelection`:

| Lần chạy | `DirectionMarginPoints` | Khớp limit | Số lệnh | Win rate | Kỳ vọng | Sụt giảm tối đa |
|---|---|---|---|---|---|---|
| #12 (mốc V1.4) | — | — | 1324 | 49,6% | −0,040R | — |
| #14 — lượt A | 0 | thận trọng | 73 | 26,0% | −0,0426R | 3,70R |
| #15 — lượt B | 8 | thận trọng | 73 | 26,0% | −0,0426R | 3,70R |
| #16 — lượt A | 0 | lạc quan | 73 | 26,0% | −0,0426R | 3,70R |
| #17 — lượt B | 8 | lạc quan | 73 | 26,0% | −0,0426R | 3,70R |

Theo regime (giống hệt nhau ở cả bốn lượt):

| Regime | Số lệnh #12 → #14 | Win rate | Kỳ vọng |
|---|---|---|---|
| `TrendUp` | 312 → 23 | 43,5% | **+0,032R** |
| `HighVolatility` | 60 → 26 | 19,2% | −0,070R |
| `TrendDown` | 270 → 17 | 11,8% | −0,087R |
| `Range` | 682 → **7** | 28,6% | −0,080R |

**Ba điều rút ra, và điều thứ ba là điều đáng lo nhất.**

1. **Biên chọn chiều không loại lệnh nào.** A và B trùng nhau đến từng chữ số. Bảng quyết định cho
   biết vì sao: B ghi `Veto:DirectionUnclear = 8`, còn A ghi thêm đúng 8 lượt `BelowThreshold`
   (2416 so với 2408). Tám lượt đó vốn đã dưới ngưỡng điểm — biên chỉ đổi *nhãn* lý do, không đổi
   *kết cục*. Nói cách khác `DirectionMarginPoints = 8` hiện là quy tắc chết: nó nằm sau ngưỡng
   điểm, và ngưỡng điểm đã chặn hết. **Chưa có bằng chứng nào ủng hộ việc giữ nó**, cũng chưa có
   bằng chứng nào chống lại — nó chỉ chưa bao giờ được kích hoạt trong hai năm dữ liệu.

2. **Mô hình khớp limit không ảnh hưởng kết quả** ở quy mô này: toàn kỳ chỉ có 6 chân limit được
   đặt (khớp 3, hết hạn 3). Cờ vẫn được đọc thật (`SimulatedTradePosition` đổi `<` thành `<=`),
   nhưng 6 mẫu thì chênh lệch "chạm" với "xuyên qua" gần như không bao giờ xảy ra. Điều kiện chấp
   nhận 6 coi như **thoả một cách rỗng** — đừng đọc nó là "kết quả bền vững trước giả định khớp".

3. **⚠️ Số lệnh sụp từ 1324 xuống 73 (−94,5%) và win rate giảm 49,6% → 26,0%.** Đây KHÔNG phải
   hình dạng đã kỳ vọng ở bước 4. Thủ phạm chính không phải §2 hay §4 mà là **§3**:
   `Veto:InsufficientRoom = 83 856` — nhiều gấp 2,3 lần mọi lý do chặn khác cộng lại, và gấp 1149
   lần số lệnh vào được. Rào 1,6R theo cấu trúc đang loại gần như toàn bộ cơ hội, và 73 lệnh còn
   lại là phần đuôi hiếm chứ không phải phần tinh tuý. Kỳ vọng −0,0426R **xấu hơn** mốc −0,040R,
   trên cỡ mẫu quá nhỏ để phân biệt với nhiễu (73 lệnh, sai số chuẩn của kỳ vọng ≈ 0,1R).

   `Veto:NotAtRangeEdge = 36 449` là của §4 và nó làm đúng việc được giao — ngày `Range` từ 682
   lệnh còn 7. Nhưng "giảm mạnh" mà còn 7 lệnh trong hai năm thì không đo được gì cả: 28,6% win
   rate trên 7 mẫu không nói lên điều gì về việc fade tại biên có ăn hay không.

**Việc phải làm trước bước 5**, theo thứ tự:

- Xem lại §3: rào `MinStructuralRr = 1,6` áp lên *mọi* chế độ. Cần biết phân phối R:R cấu trúc
  thực tế trước khi chọn ngưỡng, thay vì chọn ngưỡng rồi xem còn lại bao nhiêu.
- Mở rộng mẫu cho ngày `Range` bằng **thêm mã và kéo dài khoảng thời gian** (kho đã có 2020→2026),
  KHÔNG bằng nới `RangeEdgePercent` — §10 đã chốt: số lệnh ít không phải lý do để hạ chuẩn. Việc
  duy nhất được phép xem lại là **định nghĩa** biên độ 30 nến 4h ở §4.2a, và chỉ khi lập luận cho
  thấy định nghĩa đó sai, không phải khi nó cho ít mẫu.
- Không diễn giải bất cứ con số nào trong bảng trên như bằng chứng ủng hộ hay phản đối §2/§4 cho
  tới khi số lệnh trở lại mức đo được.

**Thứ tự này đã đổi so với bản nháp**, sau bản review ngoài: bản nháp xếp tối ưu trước, đo sau.
Mọi so sánh trước khi có §8a đều chạy trên một thước hỏng, nên hai bước sửa thước đo (§8a, §8)
được kéo lên trước ba bước tối ưu còn lại.

⚠️ **Bước 1 và 2 làm kết quả tệ đi, và đó là dấu hiệu tốt.** Trước đó backtest bỏ qua phí vốn và
tính rủi ro scale-in thấp hơn thực tế. Nếu chạy lại mà expectancy **không** giảm thì phải nghi ngờ
việc trừ chi phí đã không thực sự chạy.

### Điều kiện chấp nhận (kế thừa V1, siết thêm)

Trên BTCUSDT + ETHUSDT, 2024-01-01 → 2025-12-31:

1. Expectancy sau phí/trượt giá phải **tăng** so với bước liền trước và ưu tiên dương.
2. Max drawdown và chuỗi thua **không được xấu hơn** chỉ để đổi lấy win rate đẹp.
3. Báo cáo phải phân rã đúng regime — không gán mặc định mọi lệnh thành `Range`.
4. Gate số lệnh/ngày và gate vị thế mở phải dùng **trạng thái mô phỏng**, không đọc bảng production.
5. **(Mới)** Phải phân rã theo `Mode` **và** `ExitReason` (target / stop / time-stop / cuối kỳ).
   Mỗi chế độ cần **tối thiểu 100 lệnh**, ưu tiên 200.

   > **Sửa từ 20 lên 100 sau bản review.** Ngưỡng 20 là vô nghĩa về mặt thống kê: ở win rate
   > quanh 50%, sai số chuẩn là `√(0,25/20) ≈ 11,2%`, tức khoảng tin cậy 95% rộng **±22 điểm phần
   > trăm**. Một chế độ đo được 60% với 20 lệnh thật ra nằm đâu đó trong khoảng 38–82%. Với 100
   > lệnh khoảng đó co về ±10 điểm, vẫn rộng nhưng đã đủ để loại những khác biệt do may rủi.
   >
   > Hệ quả phải chấp nhận: **đây là lý do KHÔNG tách bảng điểm riêng cho từng setup.** Chia ba
   > chế độ rồi fit ba bộ trọng số trên phần mẫu còn lại là công thức sinh ra kết quả đẹp trong
   > mẫu và chết ngoài mẫu. Cổng riêng theo chế độ thì được — cổng là boolean, không có tham số
   > để khớp; trọng số thì không.

6. **(Mới)** Kết quả phải đứng vững ở **cả hai** mô hình khớp limit (`BacktestLimitFillRequiresThrough`
   bật và tắt). Một cải tiến chỉ tồn tại ở mô hình lạc quan là cải tiến của giả định, không phải
   của chiến lược.

7. **(Mới)** Báo cáo khoảng tin cậy cho win rate và expectancy, không kết luận từ một con số điểm.
   Với thay đổi nhằm giảm drawdown, dùng kiểm định **không kém hơn** (non-inferiority) cho
   expectancy thay vì đòi nó tăng — nếu không sẽ loại nhầm đúng những thay đổi làm hệ thống bền hơn.
8. **(Mới)** Số lệnh sau mỗi bước phải được ghi lại. Bước nào cắt trên 60% số lệnh thì mẫu còn
   lại quá nhỏ để tin — phải nới tham số hoặc mở rộng khoảng thời gian trước khi kết luận.
9. **(Mới)** Chạy lại nguyên bộ tham số trên **2022-01-01 → 2023-12-31** (out-of-sample). Nếu
   expectancy đảo dấu thì bộ tham số đã khớp quá mức khoảng 2024–2025 — quay lại V1.

   > Khoảng 2024–2025 đã được nhìn và chỉnh nhiều lần nên **không còn độc lập**. Nó là tập phát
   > triển, không phải tập kiểm tra. Phải ghi lại **số lần đã thử tham số** trên nó — không ghi
   > thì không có cách nào biết một kết quả đẹp là thật hay là lần thử thứ ba mươi.
   >
   > Cảnh báo cụ thể từ baseline: Trend Down thắng 42,59% còn Trend Up thắng 51,60% trên một mẫu
   > phần lớn là thị trường tăng. Đó không phải phát hiện về chiến lược, đó là đặc tính của mẫu.
   > Mọi tinh chỉnh riêng cho chiều bán trên dữ liệu này là khớp regime, không phải cải tiến.

10. Kết quả không cải thiện ⟹ **giữ bằng chứng âm và bỏ thay đổi**, không tối ưu tiếp trên cùng một
    mẫu cho tới khi đẹp.

---

## §10 — Những thứ CỐ Ý không làm

Ghi ra để lần sau không phải cân nhắc lại:

- **Không thêm mẫu hình mới** (cờ, tam giác, wedge, order block, FVG). `PriceActionAnalyzer` hiện
  tại chưa dùng hết giá trị của 9 tín hiệu đang có; thêm tín hiệu thứ 10 khi 9 cái đầu còn nhiễu
  chỉ làm nhiễu nhanh hơn.
- **Không đưa AI vào vòng quyết định.** Giữ nguyên SC-001: vòng quyết định phải chạy trọn vẹn khi
  lớp AI chết.
- **Không tối ưu tham số bằng grid search.** Mọi con số trong tài liệu này đến từ một lập luận
  (hình học, chi phí, hoặc cấu trúc thị trường). Con số đến từ grid search trên hai năm dữ liệu là
  con số sẽ không lặp lại.
- **Không nới ngưỡng vào lệnh để có thêm mẫu.** Nếu §2 + §3 cắt số lệnh xuống quá thấp, câu trả
  lời là thêm mã hoặc thêm khung thời gian, không phải hạ chuẩn.
- **Không bật live trading ở vòng này.** V2 kết thúc ở một báo cáo backtest, đúng như V1.

- **Không tách bảng điểm riêng cho từng setup** (`RangeFade` / `TrendPullback` / `StrongTrend`).
  Bản review ngoài đề xuất việc này ở mức P0. Từ chối, vì nó mâu thuẫn với chính điều kiện chấp
  nhận số 5 của bản review: chia ba chế độ thì nhân ba bề mặt tham số trên một mẫu vốn đã khó đạt
  100 lệnh mỗi chế độ. **Cổng riêng theo chế độ thì làm** — cổng là boolean, không có tham số để
  khớp. Trọng số riêng thì để dành tới khi có đủ số lệnh mỗi chế độ để nói.

- **Không nới trần dừng lỗ lên 3,5 ATR kèm giảm size.** Bản review đề xuất cho `StrongTrend`.
  Giảm size không sửa được dừng lỗ đặt sai chỗ — nó chỉ làm khoản lỗ nhỏ đi trong khi *xác suất*
  thua giữ nguyên. Với mục tiêu là tỉ lệ thắng, đó là sai đòn bẩy. Cấu trúc xa quá 3 ATR nghĩa là
  không đọc được điểm phủ định của setup, và câu trả lời đúng vẫn là **không vào**. (Ý "trần khác
  nhau theo chế độ", Range chặt hơn Trend, thì hợp lý và được giữ lại cho bước 5.)

---

## §11 — Bàn giao

> **Lưu ý 2026-08-04:** trạng thái trong §11.1–§11.5 là ảnh chụp giữa quá trình và đã được
> thay thế bởi kết quả cuối ở **§12**. Giữ nguyên phần cũ để bảo toàn lịch sử quyết định.

Chốt tại **2026-08-04**, sau bước 1, 2, 4 và 3. Mục này để mở ra là làm tiếp được ngay, không
phải đọc lại toàn bộ tài liệu.

### 11.1 Đang ở đâu

- **826 test xanh**, cả bốn project biên dịch 0 lỗi.
- Bước `0a` → `2b` → `1` → `2` → `4` → `3` đã xong (bảng §8b).
- **Chưa commit gì.** Toàn bộ nằm ở working tree.
- **Chưa lần backtest nào chạy trên mã của bước 1 trở đi.** Đây là món nợ lớn nhất đang treo:
  bốn bước đã viết xong mà chưa bước nào được đo.
- **Bốn migration đã tạo, chưa chạy** — phải chạy trước lần backtest tiếp theo, nếu không
  `EngineSettings` thiếu cột và mọi thứ ở bước 1–4 không có hiệu lực:

```bash
dotnet ef database update --project src/MMW.Infrastructure --startup-project src/MMW.Infrastructure
```

| Migration | Nội dung |
|---|---|
| `AddAdaptiveExecutionV2` | 18 cột cấu hình V2 (defaultValue điền tay) |
| `AddBacktestCostInR` | `TotalFeeR`, `TotalFundingR`, `TotalSlippageR` trên `BacktestRuns` |
| `AddLimitFillModel` | `BacktestLimitFillRequiresThrough` (defaultValue sửa tay thành `true`) |
| `AddDirectionSelection` | `DirectionalScore`, `OppositeScore`, `OppositeDirectionalScore`, `RangePositionPercent` trên `EntryScorecards` |

**Xây dựng khi `MMW.Web` đang chạy.** Web khoá DLL nên `dotnet build MMW.sln` đổ ở bước sao chép
và che luôn lỗi biên dịch thật — đúng cái đã giấu lỗi `HomeController` suốt hai bước. Đổi hướng
thư mục ra là đủ, không cần tắt web:

```bash
dotnet build MMW.sln -c Debug -p:BaseOutputPath=D:/Temp/mmw-build/
```

### 11.2 Việc đầu tiên nên làm khi quay lại

Chạy backtest lại để có **baseline V2 mới**. Kết quả sẽ **xấu hơn** V1.4 — đó là dấu hiệu đúng,
vì trước đó phí vốn bị bỏ qua và rủi ro scale-in bị tính thấp hơn thực tế. Nếu expectancy **không**
giảm thì phải nghi ngờ việc trừ chi phí chưa thực sự chạy.

Chạy **hai lần**, khác nhau đúng một cờ `BacktestLimitFillRequiresThrough`, để có luôn cặp
lạc quan/thận trọng làm mốc so cho mọi bước sau. `BacktestCli` đã in sẵn chi phí mỗi lệnh và
tỉ lệ khớp chân limit.

Rồi **tính lại bảng §0** từ `TotalFeeR` + `TotalFundingR` thật, thay cho con số giả định.

Sau đó chạy tiếp lượt A và lượt B của bước 3–4 theo đúng công thức tách ở §9.

### 11.3 Ba câu hỏi mà lần chạy tới phải trả lời

Bước 3 và 4 đã xong về mã. Điều chưa biết là chúng có đúng không, và ba câu dưới đây là những chỗ
mà một quyết định đã được đưa ra dựa trên lập luận chứ chưa dựa trên số đo. Cột cần đọc đã có sẵn
trong `EntryScorecards` — không phải chạy lại backtest lần thứ hai để lấy.

1. **Cửa sổ biên độ 30 nến 4h có đúng độ rộng không?** Đọc `RangePositionPercent` trên các phiếu
   của ngày `Range`. Phân bố dồn hết vào giữa (25–75%) nghĩa là cửa sổ quá RỘNG — biên độ dựng ra
   không phải biên độ giá đang ở trong. Phân bố dồn ra ngoài [0, 100] nghĩa là quá HẸP — giá liên
   tục "phá vỡ" một biên độ chỉ là nhiễu của vài phiên.

2. **Ràng buộc vị trí có chọn nhầm bên không?** Trên ngày `Range`, so `DirectionalScore` với
   `OppositeDirectionalScore`. Nếu chiều bị vị trí loại thường xuyên chấm CAO HƠN chiều được chọn
   *và* các lệnh đó thua nhiều hơn, thì quy tắc vị trí đang thắng bảng chấm — đúng như thiết kế.
   Nếu ngược lại thì biên `DirectionMarginPoints` cũng phải áp cho ngày range, và §4.2 (không phải
   §11.3) mới là bản đúng.

3. **Biên 8 điểm có phải con số đúng không?** Đếm phiếu `DirectionUnclear` và xem phân bố
   `|DirectionalScore − OppositeDirectionalScore|`. 8 là con số suy ra từ lập luận
   ("59 điểm đổi theo chiều, chênh dưới 8 là chưa nói gì"), chưa phải từ dữ liệu. **Chỉnh nó thì
   phải theo phân bố, không theo expectancy** — chỉnh theo expectancy trên đúng khoảng dữ liệu đã
   nhìn nhiều lần là grid search trá hình, thứ §10 đã từ chối.

### 11.4 Đã chốt — không bàn lại

| Quyết định | Lý do ngắn |
|---|---|
| `MinDataCoveragePercent` là **tỉ lệ 75%**, không phải số điểm tuyệt đối | §5 |
| Rào chỗ chạy là tiêu chí **0 điểm** `technical.structural_room` | Giữ tổng 85, không phải tính lại ngưỡng |
| Dừng lỗ đọc pivot **khung vào lệnh**; mục tiêu đọc cả ba khung | §3 |
| Vượt `StopAtrMultipleMax` ⟹ **không vào**, không bao giờ "vào nhỏ hơn" | §10 |
| `OpenPositionGate` chặn cùng mã **bất kể chiều** | Đảo chiều không phải setup độc lập |
| `CorrelatedExposureGate` chỉ **giảm size**, không chặn | §6.2 |
| Chân vào lệnh **đầu tiên bắt buộc là lệnh thị trường** | Chân đầu là limit ⟹ lệnh chờ, tầng khác |
| Chia đều **rủi ro**, chưa đổi sang 40/35/25 | Đợi state machine ở bước 5 |
| **Một** bảng điểm, cổng riêng theo chế độ | §10 |
| Biên độ = **30 nến 4h**, pivot đã xác nhận, phần trăm KHÔNG kẹp | §4.2a |
| Chiều bị **veto cứng** bị loại khỏi phép so, không coi là 0 điểm | §8b |
| Một ứng viên duy nhất ⟹ **không đòi biên** | Chiều kia bị cấm, không phải thua điểm |
| Ngày range: **vị trí chốt chiều trước khi chấm** | §11.3 thắng §4.2, và đã có cách kiểm chứng |
| `IsDirectional` là khai báo **bắt buộc** trên mỗi tiêu chí | Mặc định im lặng là mặc định sai |
| Ngưỡng hình học của mẫu hình nằm trong **mã**, không phải `EngineSetting` | Chúng là định nghĩa, không phải khẩu vị |

### 11.5 Còn treo

- **Chưa kiểm chứng backtest run #12** mà bản review trích (1.324 lệnh, win rate 49,62%,
  expectancy −0,0404R). Các con số nhất quán về mặt số học — giải ngược hàng giờ-08 ra đúng mô
  hình chốt-tại-1R với phí taker — nhưng chưa ai truy vấn lại `BacktestRuns` để xác nhận nó tồn
  tại. Toàn bộ phần định lượng của bản review dựa vào đó, nên nên kiểm trước khi để nó lái quyết định.

- **`CorrelatedExposureGate` chỉ cộng dồn cùng chiều.** Tương quan âm + ngược chiều cũng là rủi ro
  chồng nhau và đang bị bỏ sót. Bản review nói đúng, nhưng tác động gần bằng 0: tương quan với BTC
  trong nhóm crypto lớn gần như luôn nằm trong [+0,6; +0,95], nên nhánh này sẽ không kích hoạt lần
  nào trong hai năm dữ liệu. Sửa cho đối xứng thì tốt, đừng xếp P0.

- **Regime override trong ngày** (range → trend giữa phiên) là lỗ hổng thật, để bước 6. Hysteresis
  và cooldown là state có nhớ — chỗ dễ lọt lỗi nhìn trước nhất trong toàn hệ thống, và nó làm
  **tăng** số lệnh trên đúng loại ngày mà baseline cho thấy tệ nhất. Làm sau cùng, khi thước đo
  đã sạch hẳn.

- **Ràng buộc chéo của vai-đầu-vai chưa có test.** `|LS − RS| < prominence` hiện luôn đúng vì
  `ShoulderToleranceAtr` (0,5) nhỏ hơn `HeadProminenceAtr` (0,8), nên không bộ dữ liệu nào chạm
  tới nhánh đó. Giữ lại vì nó neo ý định độc lập với hai con số, nhưng phải biết rằng nếu ai đó
  chỉnh hai hằng số kia cho chồng lấn thì hiện KHÔNG có gì đỏ.

- **Chấm hai chiều làm việc chấm điểm nặng gấp đôi trên ngày `Both`.** Bù lại, §2.7 cắt price
  action từ ba lượt quét xuống một, nên tổng chi phí một mốc chấm xấp xỉ như cũ. Chưa đo trên
  70.000 mốc thật; nếu backtest chậm hẳn thì chỗ tối ưu tiếp theo là `MarketStructureAnalyzer` và
  `Atr`, cả hai đang chạy lại một lần cho mỗi chiều với cùng đầu vào.

- **`liquidity.open_interest` khai báo `IsDirectional = false` trong khi chú thích của nó nói về
  "lượng hợp đồng mở tăng CÙNG với giá đi thuận chiều".** Mã và chú thích đang lệch nhau: mã chỉ
  đo dấu của thay đổi OI. Cờ đang nói đúng về MÃ. Khi nào ghép hướng giá vào thì đổi cờ cùng lúc,
  và khi đó thang điểm đổi-theo-chiều lên 64.

---

## §12 — Kết quả xử lý pending và quyết định cuối

Chốt tại **2026-08-04** sau khi triển khai bước 5, bước 6, chạy full-history và OOS. Mục này
thay thế trạng thái bàn giao cũ ở §11 nhưng không xoá lịch sử ở đó.

### 12.1 Trạng thái từng blocker

| Hạng mục | Trạng thái | Bằng chứng / quyết định |
|---|---|---|
| Telemetry `InsufficientRoom` | ✅ Xong | Lưu phân phối R:R của mọi lượt dựng được cấu trúc và observation thô của từng veto, không chỉ đếm. |
| Rào `MinStructuralRr = 1,6` | ✅ Không còn cắt sai mẫu | Full #29 giữ **276.978/300.096 = 92,3%**; OOS #28 giữ **85.902/92.056 = 93,3%**. Nguyên nhân cũ là planner dùng vật cản gần làm target cuối; nay TP1 và runner target được tách đúng nghĩa. |
| Mẫu tối thiểu 100/mode | ⚠️ Đạt full, chưa đạt riêng OOS StrongTrend | Full #29: Range 835, Standard 3.664, StrongTrend 300. OOS #28: 276, 1.157, **91**. Không dùng mẫu full để che thiếu mẫu OOS. |
| CI win rate / expectancy | ✅ Xong | Báo Wilson cho win rate và CI trung bình cho expectancy ở toàn kỳ và từng mode. |
| Phân rã `ExitReason` | ✅ Xong | Có Target / Stop / TimeStop / EndOfPeriod trong report, DB và UI. |
| Sổ số lần thử | ✅ Xong | `ComparableTrialNumber` đếm run hoàn tất trên đúng khoảng + tập mã. OOS #30 là lần thứ **5**; không tiếp tục tune trên mẫu này. |
| `DirectionMarginPoints` | ✅ Đã bỏ | A/B dev #23/#24 chỉ có 5 setup đủ điểm bị ảnh hưởng, số lệnh không đổi; nhánh margin không tạo bằng chứng có ích. Giữ các cột chẩn đoán lịch sử, bỏ gate khỏi quyết định theo §9.10. |
| §7 thực thi V2 | ✅ Đã triển khai | Pending limit, tranche theo rủi ro/cấu trúc, TP1 + runner, time-stop, fee-adjusted breakeven và trailing pivot xác nhận từ nến kế tiếp. |
| §6.3 high-vol + intraday override | ✅ Mã và test xong; hiệu quả chưa được chứng minh riêng | Range chỉ được override một chiều sang TrendUp/TrendDown sau breakout + volume xác nhận, có release/cooldown và chỉ dùng nến đã đóng. Không tăng risk/quota. Chưa có A/B cô lập nên không tuyên bố nó cải thiện expectancy. |
| Hai mô hình fill limit | ✅ Xong | OOS #28 bảo thủ và #30 lạc quan lệch đúng 1 lệnh; expectancy lần lượt −0,361R và −0,362R. Kết luận không phụ thuộc queue assumption. |
| H&S cross-constraint chết | ✅ Đã bỏ | Xoá nhánh `shoulderGap >= prominence` vốn không thể chạm với 0,5 ATR < 0,8 ATR; không giữ mã chết để “neo ý định”. |
| DLL bị web khoá | ✅ Có đường build ổn định | Build sang `-p:BaseOutputPath=D:/Temp/...`; không cần dừng web đang phục vụ. |

### 12.2 P0 mới phát hiện trong lúc đo

Run #27 làm lộ hai lỗi thước đo liên quan nhau:

1. `RMultiple` trả PnL theo size đã chọn thay vì chia cho ngân sách rủi ro thực sự đã khớp.
   Cùng một stop, lệnh 0,25R bị báo −0,25R còn lệnh 1R báo −1R, làm expectancy phụ thuộc sizing.
2. `OversizedGate` lấy trung bình từ size **sau** khi chính gate giảm. Qua lịch sử dài, mốc tham
   chiếu tự thấp dần và size co gần về 0; vì vậy chi phí và expectancy của các năm cuối trong
   run full trông tốt giả tạo.

Đã sửa như sau:

- `RMultiple = RealizedR / FilledRiskBudgetR`; partial fill chỉ đưa phần risk weight đã khớp vào
  mẫu số.
- Expectancy, CI và chi phí mỗi lệnh dùng R đã chuẩn hoá; daily-loss và đường drawdown vẫn dùng
  `RealizedR` theo size thật.
- Oversize dùng mức dự kiến trước discipline. Backtest truyền trực tiếp mức này; live khôi phục
  từ `RiskPercent / DisciplineMultiplier` trên scorecard liên kết. Lệnh tay vẫn dùng risk thật.
- Có regression test khẳng định size 0,25R và 1R cùng diễn biến đều cho −1R, trong khi tác động
  đường vốn vẫn lần lượt −0,25R và −1R.

Vì vậy **expectancy/chi phí R của run #18–#27 không còn hợp lệ**. Giữ các row để audit; số đếm,
win rate và phân phối structural R:R vẫn hữu ích cho chẩn đoán nhưng không được dùng để chấp nhận
chiến lược. Baseline hợp lệ bắt đầu từ #28.

### 12.3 Backtest hợp lệ cuối

| Run | Khoảng / fill | Lệnh | Win rate | Expectancy (CI95%) | Max DD theo size thật |
|---:|---|---:|---:|---:|---:|
| #28 | OOS 2022–2023, bảo thủ | 1.524 | 30,05% | **−0,361R** [−0,449; −0,274] | 76,87R |
| #30 | OOS 2022–2023, lạc quan | 1.525 | 30,03% | **−0,362R** [−0,450; −0,275] | 77,14R |
| #29 | 2020–2026-08, bảo thủ | 4.799 | 29,03% | **−0,312R** [−0,362; −0,261] | 196,93R |

Phân rã mode của hai mốc bảo thủ:

| Mode | OOS #28 | Full #29 | Kết luận |
|---|---:|---:|---|
| RangeQuick | 276 lệnh, −0,351R [−0,549; −0,153] | 835 lệnh, −0,225R [−0,342; −0,109] | Bị bác bỏ rõ ở cả hai mẫu. |
| Standard | 1.157 lệnh, −0,394R [−0,497; −0,291] | 3.664 lệnh, −0,347R [−0,407; −0,287] | Nguồn lỗ chính; bị bác bỏ rõ. |
| StrongTrendRunner | 91 lệnh, +0,028R [−0,250; +0,307] | 300 lệnh, −0,121R [−0,271; +0,029] | Chưa phân biệt được với 0; OOS còn thiếu mẫu. Không được bật live. |

Full #29 có 1.053 lệnh thoát target trung bình +2,584R, 3.628 lệnh thoát stop trung bình
−1,151R và 118 time-stop trung bình −0,333R. Chi phí trung bình là **0,2218R/lệnh**; OOS là
**0,2550R/lệnh**. Tỉ lệ stop quá lớn, không phải time-stop hay fill model, là nguồn âm chính.

### 12.4 Quyết định

- **V2 không đạt acceptance và live trading tiếp tục tắt.** Kết quả âm có ý nghĩa thống kê trên
  full và OOS, ở cả fill bảo thủ lẫn lạc quan.
- Không hạ `MinStructuralRr`: rào hiện giữ trên 92% mẫu, nên nó không còn là bottleneck.
- Không tiếp tục tune RangeQuick/Standard trên 2022–2025. Theo §9.10, giữ bằng chứng âm và bỏ
  hướng thay đổi hiện tại thay vì tối ưu tới khi đẹp.
- Nếu nghiên cứu tiếp, tạo một vòng **mới** cho chất lượng trigger/confirmation và chi phí theo
  khoảng stop; StrongTrendRunner chỉ được chạy shadow riêng để thu thêm mẫu, không ghép kết quả
  full vào OOS và không bật lệnh thật.
