# Adaptive Execution V5 — công thức admission rút từ dữ liệu 2020–2026

Ngày chốt số liệu: **2026-08-06**
Phạm vi: **BTCUSDT, ETHUSDT · 2020-01-01 → 2026-08-04 05:15 UTC** (kho 231.165 nến 15m mỗi mã)
Trạng thái: **IN-SAMPLE HYPOTHESIS — chưa đạt cổng rollout, chưa được phép bật live**
Control: V3 run `#40`/`#43`; run mới `#45`, `#46` parity tuyệt đối với control.

---

## 1. Kết luận điều hành

Câu hỏi đặt ra là "công thức nào cho tỉ lệ thắng và lợi nhuận cao nhất". Dữ liệu 2020–2026 trả lời được, nhưng câu trả lời **không nằm ở execution** — nó nằm ở chỗ **thang điểm bối cảnh đang chọn ngược**.

Ba kết quả, theo thứ tự quan trọng:

1. **Chi phí không dùng làm bộ lọc được.** Decile theo `ActualCostR` có gradient khổng lồ (`+0,959R` → `−1,354R`), nhưng đó là ảo ảnh: `ActualCostR` được TRỪ khỏi `RMultiple` và chỉ đo được sau khi lệnh đóng. Ước lượng chi phí BIẾT TRƯỚC (`ExpectedCostR`) có tương quan với kết quả là **−0,0299** — bằng không. Mọi bộ lọc dựng trên chi phí đều là nhìn trước.

2. **Điểm bối cảnh cao đi kèm kết quả xấu, và cơ chế đọc được.** Ba tiêu chí `htf_alignment=10`, `momentum=7`, `volatility_regime=6` — mỗi tiêu chí riêng lẻ đều có CI 95% hoàn toàn dưới 0 khi được award tối đa. Khi cả ba cùng maxed, đó là chữ ký **exhaustion**: mọi khung, mọi động lượng, mọi biến động cùng báo "hoàn hảo" nghĩa là con sóng đã đi hết.

3. **Công thức V5 nâng win rate `37,21% → 46,58%` và expectancy `−0,1026R → +0,1713R`** trên full range, dương ở 6/7 năm. **Nhưng CI 95% vẫn là `[−0,0582; +0,4028]`, tức chưa loại trừ được 0.** V5 **không đạt** cổng P2 của chính dự án.

4. **Nút thắt lớn nhất không phải CI mà là tần suất:** 161 lệnh / 6,59 năm = **2 lệnh/tháng**, tổng `+4,2R/năm`. Ở risk 1%/lệnh là ~4%/năm. Kể cả khi edge là thật, quy mô này không đủ để V5 đứng một mình. Nguyên nhân cấu trúc là kho chỉ có **2 mã** — xem §6bis.

Đây là giả thuyết đáng kiểm định, **không phải** bản nâng cấp đã chứng minh. Việc đầu tiên nên làm **không** phải implement V5, mà là mở rộng universe rồi kiểm tra edge có sống ngoài BTC/ETH không.

---

## 2. Dữ liệu và cách đo

### 2.1 Nguồn

| Bảng | Nội dung |
|---|---:|
| `KlineArchives` | 496.034 nến — 231.165 × 15m, 14.444 × 4h, 2.408 × 1d mỗi mã |
| `FundingRateArchives` | 14.448 mốc phí vốn |
| `BacktestRuns` | 46 lần chạy, có fingerprint từ `#31` |

### 2.2 Công cụ mới

Thêm `--dump FILE.csv` vào `backtest` CLI. Mỗi lệnh xuất một dòng, **tách rõ hai nhóm cột**: đại lượng biết trước khi gửi lệnh, và đại lượng chỉ biết sau khi lệnh đóng. Đây là điều kiện cần để không tự lừa mình — xem §3.

Telemetry vẫn là observer một chiều. Parity đã xác nhận:

| Run | Lệnh | Win | Net | Decision fingerprint | Trade fingerprint |
|---:|---:|---:|---:|---|---|
| `#40` control | 344 | 37,2093% | −0,10265R | `8f7cbd1e64c2…` | `55f6dd3a6cb4…` |
| `#45` +dump | 344 | 37,2093% | −0,10265R | `8f7cbd1e64c2…` | `55f6dd3a6cb4…` |
| `#46` +dump +criteria | 344 | 37,2093% | −0,10265R | `8f7cbd1e64c2…` | `55f6dd3a6cb4…` |

Bật dump **không đổi một bit hành vi nào**.

---

## 3. Bác bỏ: chi phí không phải nguyên nhân

Đây là kết quả âm nhưng quan trọng nhất, vì nó đóng lại một hướng mà V3/V4 còn để ngỏ.

### 3.1 Gradient theo `ActualCostR` là ảo ảnh

<!-- V5_COST_START -->
| Decile `ActualCostR` | Lệnh | Win rate | Net |
|---|---:|---:|---:|
| D1 `−0,258…0,053` | 34 | **76,47%** | **+0,9586R** |
| D5 `0,106…0,123` | 35 | 54,29% | +0,3143R |
| D8 `0,175…0,209` | 35 | 11,43% | −0,8383R |
| D10 `0,297…1,581` | 35 | 2,86% | **−1,3540R** |

| Decile `ExpectedCostR` (biết trước) | Lệnh | Win rate | Net |
|---|---:|---:|---:|
| D1 `0,011…0,078` | 34 | 35,29% | −0,3549R |
| D2 `0,078…0,096` | 34 | 44,12% | +0,1846R |
| D5 `0,145…0,175` | 35 | 54,29% | +0,2651R |
| D10 `0,385…0,679` | 35 | 40,00% | −0,0983R |
<!-- V5_COST_END -->

Cột trái đơn điệu tuyệt đối. Cột phải **không có trật tự nào**.

### 3.2 Vì sao

| Tương quan | Giá trị |
|---|---:|
| `corr(ActualCostR, RMultiple)` | **−0,4061** |
| `corr(ExpectedCostR, RMultiple)` | **−0,0299** |
| `corr(ActualCostR, ExpectedCostR)` | +0,5680 |
| `corr(ActualCostR, 1/StopBps)` | +0,5444 |
| `corr(ActualCostR, BarsHeld)` | +0,5609 |
| `corr(fundingR/budget, BarsHeld)` | +0,9377 |

Phân rã friction: **phí giao dịch 72,52%**, trượt giá 25,42%, phí vốn 2,06%.

`ActualCostR = (FeeR + FundingR + SlippageR) / FilledRiskBudgetR` (`BacktestTelemetry.cs:363`). Nó bị trừ thẳng khỏi `RMultiple`, nên tương quan âm mạnh là **quan hệ định nghĩa, không phải quan hệ nhân quả**. Thêm nữa, chân limit khớp khi giá đi ngược — và khớp gần stop hơn nên sinh quantity lớn hơn, tức phí trên mỗi R cao hơn. Cùng một nguyên nhân (đường giá bất lợi) đẻ ra cả chi phí cao lẫn kết quả xấu.

**Hệ quả:** siết tiếp `ExecutionCostTooHigh` (đang loại 406 lượt) sẽ không cứu được gì. Kết luận này thay thế hướng "giảm friction" trong V3 §6.4.

---

## 4. Phát hiện chính: exhaustion

### 4.1 Từng tiêu chí riêng lẻ

Award tối đa của gần như mọi tiêu chí đều đi kèm kết quả tệ nhất của chính tiêu chí đó:

<!-- V5_CRITERIA_START -->
| Tiêu chí | Khi MAXED | Khi không maxed |
|---|---:|---:|
| `technical.htf_alignment` = 10 | n=147 · **−0,258R** · CI `[−0,468; −0,036]` | n=197 · +0,013R |
| `technical.momentum` = 7 | n=159 · **−0,242R** · CI `[−0,450; −0,022]` | n=185 · +0,017R |
| `market.volatility_regime` = 6 | n=157 · **−0,239R** · CI `[−0,447; −0,017]` | n=187 · +0,012R |
| `technical.market_structure` = 10 | n=161 · −0,157R | n=127 · +0,029R |
| `market.day_regime_match` = 10 | n=137 · −0,187R | n=194 · −0,017R |
<!-- V5_CRITERIA_END -->

Ba tiêu chí đầu có CI 95% **hoàn toàn dưới 0**, mỗi tiêu chí ~150 mẫu. Đây là ba xác nhận độc lập cùng chiều, không phải một ngưỡng đào được.

`market.day_regime_match` bị loại khỏi chỉ số vì nó cộng tuyến với setup (6 ⇔ Range, 10 ⇔ StrongTrend) — dùng nó là mã hoá lại setup.

### 4.2 Chỉ số exhaustion

```
ExhaustionCount = (technical.htf_alignment    == 10 ? 1 : 0)
                + (technical.momentum          ==  7 ? 1 : 0)
                + (market.volatility_regime    ==  6 ? 1 : 0)
```

| `ExhaustionCount` | Lệnh | Win rate | Net |
|---:|---:|---:|---:|
| 0 | 52 | **57,69%** | **+0,4419R** · CI `[+0,0290; +0,8676]` |
| 1 | 143 | 38,46% | −0,0648R |
| 2 | 127 | 28,35% | **−0,3324R** |
| 3 | 22 | 31,82% | −0,3073R |

Đơn điệu giảm. `corr(ExhaustionCount, RMultiple) = −0,1665`, mạnh hơn `corr(TotalScore, RMultiple) = −0,0692`.

### 4.3 Ý nghĩa từng ngưỡng trong code

| Tiêu chí | Điều kiện đạt max |
|---|---|
| `htf_alignment=10` | Chồng EMA 20/50/200 khung 4h xếp **đủ ba lớp** thuận chiều lệnh |
| `momentum=7` | RSI **trong dải** cấu hình **và** histogram MACD đang nở thuận chiều |
| `volatility_regime=6` | Phân vị ATR **nằm trong dải lý tưởng** của kế hoạch ngày |

Cả ba đều là trạng thái "mọi thứ hoàn hảo". Khi ba điều kiện lý tưởng xuất hiện đồng thời, con sóng đã trưởng thành; vào lệnh lúc đó là mua đúng đoạn cuối.

### 4.4 Kiểm tra bền vững

Gradient giữ nguyên chiều khi tách theo mã và theo giai đoạn:

| Nhóm | c=0 | c=1 | c=2 | c=3 |
|---|---:|---:|---:|---:|
| BTCUSDT | +0,302R (18) | −0,006R (61) | −0,245R (60) | −0,326R (10) |
| ETHUSDT | +0,516R (34) | −0,109R (82) | −0,411R (67) | −0,291R (12) |
| 2020–2022 | +0,801R (23) | −0,041R (76) | −0,466R (78) | −0,359R (12) |
| 2023–2026 | +0,157R (29) | −0,092R (67) | −0,120R (49) | −0,245R (10) |

⚠️ **Hiệu ứng suy yếu rõ ở giai đoạn sau** (`+0,801R → +0,157R` ở c=0). Đây là cảnh báo nghiêm túc: nếu nguyên nhân là crowding thì thị trường có thể đã thích nghi.

### 4.5 Tương tác điểm × exhaustion

Thiệt hại **tập trung vào đúng một ô**:

| | `score ≤ 44` | `score ≥ 45` |
|---|---:|---:|
| `ExhaustionCount ≤ 1` | n=131 · +0,079R | n=64 · +0,052R |
| `ExhaustionCount ≥ 2` | n=29 · +0,075R | **n=120 · −0,426R** |

Ba ô lành, một ô độc. **Điểm tổng không xấu; điểm tổng cao KHI ĐÃ exhaustion mới xấu.** Trong nhóm `ExhaustionCount ≤ 1`, thêm điều kiện `score ≤ 44` làm kết quả **tệ đi** (`+0,136R` so với `+0,268R` của nhóm điểm cao) — nên V5 **không** có điều kiện nào về điểm tổng.

---

## 5. Công thức V5

```
ADMIT khi và chỉ khi mọi điều kiện sau đúng:

  (1) SetupTriggerState == Confirmed                       // giữ nguyên V3
  (2) SetupType != TrendPullback                           // shadow-only, theo V4 §5.6
  (3) ExhaustionCount <= 1                                 // MỚI ở V5
  (4) DayOfWeek(entryUtc) != Sunday                         // MỚI ở V5
  (5) toàn bộ safety/discipline gate hiện có vẫn áp dụng    // không được override

trong đó
  ExhaustionCount = [technical.htf_alignment   == 10]
                  + [technical.momentum        ==  7]
                  + [market.volatility_regime  ==  6]
```

Không có tham số nào khác. Không thêm indicator. Không đổi execution, sizing, stop hay target — **V5 chỉ là một cổng admission**.

### 5.1 Kết quả in-sample

<!-- V5_RESULT_START -->
| Chỉ số | V3 control `#40` | **V5** | Thay đổi |
|---|---:|---:|---:|
| Số lệnh | 344 | 161 | −53,2% |
| Win rate | 37,21% | **46,58%** | **+9,37 điểm %** |
| Net expectancy | −0,1026R | **+0,1713R** | **+0,2739R** |
| CI 95% net | `[−0,2550; +0,0504]` | `[−0,0582; +0,4028]` | vẫn chứa 0 |
| Gross expectancy | +0,0530R | **+0,3194R** | +0,2664R |
| Friction | 0,1556R | 0,1481R | −4,8% |
| Số năm dương | 2/7 | **6/7** | +4 |
<!-- V5_RESULT_END -->

Phân bố theo setup: `RangeRejection` 114 lệnh, `StrongTrendBreakout` 47 lệnh.

### 5.2 Walk-forward theo năm

| Rule | 2020 | 2021 | 2022 | 2023 | 2024 | 2025 | 2026 | Dương |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| V3 control | −0,052 (39) | +0,010 (81) | −0,349 (69) | −0,338 (18) | +0,067 (70) | −0,073 (45) | −0,240 (22) | 2/7 |
| **V5** | **+0,381** (22) | **+0,637** (31) | −0,231 (31) | +0,011 (12) | +0,003 (37) | +0,096 (23) | **+0,830** (5) | **6/7** |

V5 tốt hơn control ở **cả 7 năm**. Năm âm duy nhất là 2022 (`−0,231R`), vẫn cải thiện so với `−0,349R`.

### 5.3 Cơ chế cải thiện — không phải may mắn về fill

| Nhóm | Control | V5 |
|---|---:|---:|
| Exit `Stop` | 210 | 100 |
| Exit `Target` | 97 | 66 |
| Exit `TimeStop` | 37 | 21 |
| `MarketOnly` | n=175 · +0,276R · 51,4% | n=99 · **+0,519R** · **59,6%** |
| `MarketPlusLimit` | n=169 · −0,495R · 22,5% | n=88 · **−0,251R** · **29,5%** |

Hai điểm đáng giá:

- Rule cắt `Stop` mạnh hơn nhiều so với cắt `Target` (−52,4% so với −32,0%).
- **Cả hai cohort fill đều tốt lên.** Nếu V5 chỉ ăn may nhờ tránh đường giá bất lợi thì `MarketPlusLimit` sẽ không cải thiện. Nó cải thiện `+0,244R`. Đây là bằng chứng V5 lọc **chất lượng tín hiệu**, không phải lọc hộ `fill state` — thứ mà V4 §3 đã cảnh báo là không đo được bằng attribution quan sát.

### 5.4 Biến thể giữ nhiều mẫu hơn

Nếu ưu tiên số mẫu, dùng dạng tương tác thay cho ngưỡng cứng:

```
LOẠI khi (ExhaustionCount >= 2 AND TotalScore >= 45)
```

→ 187 lệnh, win `45,45%`, net `+0,1568R`, CI `[−0,0587; +0,3792]`, 5/7 năm dương, `RangeRejection`=137, `StrongTrendBreakout`=50.

Kém hơn một chút về expectancy nhưng **giữ thêm 26 lệnh** và bám sát ô thiệt hại thật (§4.5). Chọn dạng nào là quyết định đánh đổi giữa parsimony và sample; §9 áp dụng cho cả hai.

---

## 6. Trung thực thống kê — đọc kỹ trước khi tin

Đây là phần quan trọng nhất của tài liệu.

### 6.1 Không đạt cổng của chính dự án

V4 §8 P2 yêu cầu **"Full net expectancy > 0 và CI 95% lower > 0"**. V5 có `CI lower = −0,0582`. **FAIL.**

Cũng FAIL cổng "mỗi setup active ≥100 lệnh": `StrongTrendBreakout` chỉ có 47.

Chỉ một tập con duy nhất trong toàn bộ nghiên cứu có CI hoàn toàn dương — `ExhaustionCount = 0`, n=52, `+0,4419R`, CI `[+0,0290; +0,8676]`. Với ~70 lần thử, kỳ vọng có ~3,5 phát hiện giả ở mức 5%. **Một ô CI dương sau 70 lần thử không phải bằng chứng mạnh.**

### 6.2 Số lần thử

| Giai đoạn | Số rule đã đánh giá |
|---|---:|
| Sàng biến đơn (a3) | 31 |
| Kết hợp (a4) | 10 |
| Chỉ số exhaustion + ngưỡng (a6) | 19 |
| Dạng cuối (a8/a9) | ~12 |
| **Cộng dồn** | **~72** |

Chưa tính PBO/CSCV và Deflated Sharpe Ratio như V4 §7.3 yêu cầu. **Phải tính trước khi coi V5 là candidate rollout.**

### 6.3 Toàn bộ dữ liệu đã bị nhìn

2020–2026 đã dùng để chọn V2 và V3. V4 §7.1 gọi 2022–2023 là `legacy OOS already observed`. V5 được rút ra từ **cùng tập đó**, nên **không còn holdout lịch sử thật**. Mọi con số ở §5 là in-sample theo đúng nghĩa đen.

Bằng chứng chưa bị nhìn chỉ có thể đến từ forward shadow.

### 6.4 Lọc ngoại tuyến ≠ chạy engine

Kết quả §5 tính bằng cách **lọc 344 lệnh đã có**, không phải chạy lại engine. Hai khác biệt sẽ xuất hiện khi implement thật:

- `PositionAlreadyOpen` bị veto **6.368 lượt** trong full run. Bỏ 183 lệnh sẽ giải phóng slot và **nạp thêm tín hiệu mới chưa từng xuất hiện** trong tập 344 này.
- Drawdown thật phải lấy từ đường vốn của engine (có `SizeR` thay đổi theo discipline gate), không lấy từ tổng R-multiple.

Số DD trong các bảng phân tích nội bộ là đại lượng đơn vị-R xếp theo thời gian vào lệnh, **không so sánh được** với `11,0426R` của engine.

### 6.5 Hiệu ứng đang yếu đi

§4.4: c=0 cho `+0,801R` ở 2020–2022 nhưng chỉ `+0,157R` ở 2023–2026. Nếu exhaustion là hiện tượng crowding, thị trường có thể đã học. Forward shadow phải theo dõi riêng chỉ số này.

---

## 6bis. Nút thắt thật: tần suất lệnh

Đây là hạn chế nghiêm trọng hơn cả §6.1, và nó **có trước V5**.

### 6bis.1 Số liệu

| | Lệnh | /năm | /tháng | Tổng R | R/năm |
|---|---:|---:|---:|---:|---:|
| V3 control | 344 | 52,2 | 4,4 | −35,3R | −5,4R |
| **V5** | 161 | **24,4** | **2,0** | +27,6R | **+4,2R** |

Khoảng cách giữa hai lệnh V5: trung vị **7 ngày**, p90 **41 ngày**, dài nhất **188 ngày**.

Số lệnh theo năm: 2020=22, 2021=31, 2022=31, 2023=12, 2024=37, 2025=23, 2026=5. **Kết luận theo từng năm ở §5.2 dựng trên 5–37 mẫu — chỉ đủ làm chẩn đoán, không đủ làm bằng chứng.**

### 6bis.2 Gốc rễ không nằm ở V5

```
V3 baseline          344
  − TrendPullback    295   (−49)
  − exhaustion       171   (−124)
  − Sunday           161   (−10)
```

Phễu quyết định: **462.186 lượt đánh giá → 344 lệnh = 0,074%**. `BelowThreshold` loại 208.912, `NotAtRangeEdge` loại 101.234. V3 tự nó đã chỉ có 4,4 lệnh/tháng **trên cả hai mã cộng lại**.

Nguyên nhân cấu trúc: **kho chỉ có 2 mã** (BTCUSDT, ETHUSDT).

### 6bis.3 Hệ quả kinh tế

+4,2R/năm. Ở mức risk 1%/lệnh là ~4%/năm. **Kể cả khi edge là thật, quy mô này không đủ để V5 đứng một mình như một hệ thống giao dịch.**

### 6bis.4 Hướng xử lý đúng: mở rộng universe, KHÔNG nới logic

| Số mã | Lệnh/năm | R/năm nếu expectancy giữ nguyên |
|---:|---:|---:|
| 2 | 24 | +4,2R |
| 5 | 61 | +10,5R |
| 10 | 122 | +20,9R |
| 20 | 244 | +41,9R |

Lý do ưu tiên hướng này: nó tăng mẫu mà **không đụng vào thứ tạo ra edge**. Nới ngưỡng để lấy thêm lệnh sẽ đi ngược — chính việc siết V2→V3 mới kéo gross expectancy từ âm sang dương.

Về thời gian kiểm định: 244 lệnh/năm thì ~1 năm là đủ mẫu để CI có cơ hội dương thật; 24 lệnh/năm thì phải chờ hàng thập kỷ.

⚠️ Bảng trên là **ngoại suy tuyến tính ngây thơ**, là trần lạc quan chứ không phải dự báo:

- BTC/ETH tương quan cao; thêm alt không cho mẫu độc lập tương ứng. Mẫu **hiệu dụng** tăng chậm hơn số mã, và correlation gate hiện có sẽ tự chặn bớt.
- Friction đang là `0,1481R` so với gross `0,3194R` — phí ăn **46%** edge. Trên alt thanh khoản kém, tỉ lệ này xấu đi nhanh và có thể đổi dấu expectancy.

**Khuyến nghị thứ tự:** backfill thêm mã và chạy lại V3 control **trước**, để biết edge có sống ngoài BTC/ETH không, rồi mới đầu tư implement V5. Nếu edge chỉ tồn tại trên 2 mã thì V5 không đáng implement.

---

## 7. Những gì V5 **không** làm

| Không làm | Lý do |
|---|---|
| Đổi tranche market/limit | V4 §5.2 đã quyết: phải đo bằng paired lab, chưa có |
| Bỏ limit vào 100% market | D2 là attribution quan sát, không phải counterfactual |
| Siết `ExecutionCostTooHigh` | §3 bác bỏ — chi phí biết trước không dự báo được gì |
| Thêm điều kiện điểm tổng | §4.5 — trong nhóm không exhaustion, điểm cao **tốt hơn** |
| Thêm indicator mới | Chưa chứng minh causal edge, tăng overfit |
| Đổi exit/partial/time-stop | Thuộc exit lab V4 §5.5, độc lập với V5 |
| Bật live | Không đạt §6.1 |

---

## 8. Cổng chấp nhận V5

### P0 — implement và parity

- [ ] Implement `ExhaustionCount` như một gate admission; **không** đưa vào thang điểm.
- [ ] Toàn bộ test hiện có pass; thêm test cho `ExhaustionCount` ở cả ba mức 0/1/2/3.
- [ ] Chạy lại full range với V5 bật; ghi fingerprint mới.
- [ ] Chạy lại V3 control cùng binary; fingerprint **phải** vẫn là `8f7cbd1e64c2…` / `55f6dd3a6cb4…`.
- [ ] Báo cáo trade count thật sau khi tính hiệu ứng giải phóng `PositionAlreadyOpen`.

### P1 — historical

- [ ] Net expectancy > 0 **và CI 95% lower > 0** trên full range engine-run.
- [ ] Conservative và optimistic cùng dấu, chênh ≤ `0,05R`.
- [ ] Max drawdown engine ≤ `11,0426R`.
- [ ] Mỗi setup active ≥100 lệnh, hoặc chuyển setup thiếu mẫu sang shadow-only.
- [ ] PBO/CSCV và Deflated Sharpe Ratio có tính ~72 lần thử của §6.2.
- [ ] Walk-forward theo năm có purge/embargo; đa số fold dương.

### P2 — forward shadow

- [ ] ≥8 tuần và ≥100 decision event sau code freeze.
- [ ] Reconciliation fill/maker/commission/slippage ≥99% orders.
- [ ] Theo dõi riêng gradient `ExhaustionCount` để phát hiện tiếp tục suy yếu (§6.5).
- [ ] Không safety veto nào bị bypass.

Chỉ khi **P0–P2 đều đạt** mới lập proposal bật live với risk nhỏ.

---

## 9. Thứ tự triển khai

0. **Backfill thêm mã** (xem §6bis.4) và chạy lại **V3 control** trên universe rộng. Nếu edge exhaustion không sống ngoài BTC/ETH thì dừng — không implement V5.
1. Implement `ExhaustionCount` + gate, giữ V3 làm control chạy song song.
2. Chạy lại full range; đối chiếu trade count thật với 161 lệnh ước lượng ngoại tuyến.
3. Tính PBO/DSR trên toàn bộ trial registry.
4. Walk-forward có purge/embargo.
5. Quyết định giữ dạng ngưỡng cứng (§5) hay dạng tương tác (§5.4) dựa trên P1.
6. Code freeze → forward shadow 8 tuần.
7. Chỉ lập rollout proposal nếu §8 đạt đủ.

---

## 10. Tái lập

```bash
dotnet run --project src/MMW.Web/MMW.Web.csproj -c Release -- backtest --symbols BTCUSDT,ETHUSDT --from 2020-01-01 --to "2026-08-04 05:15" --version v3 --fill conservative --dump trades.csv
```

Cột `CriterionPoints` chứa `key=points;…`. `ExhaustionCount` dựng lại được từ ba key ở §4.2. Các cột trước/sau khi vào lệnh được tách theo đúng thứ tự khai báo trong `TelemetryTradeRow` (`BacktestTelemetry.cs`).

Run tham chiếu: `#45` (dump), `#46` (dump + criteria). Cả hai parity tuyệt đối với `#40`.

---

## 11. Phán quyết cuối

V5 là **giả thuyết tốt nhất mà dữ liệu 2020–2026 nâng đỡ được**, và nó chỉ ra một chỗ hỏng có ý nghĩa: hệ thống đang thưởng điểm cho trạng thái mà chính nó nên tránh. Cơ chế đọc được, hiệu ứng lặp lại trên hai mã, hai giai đoạn và ba tiêu chí độc lập.

Nhưng V5 **chưa chứng minh được lợi thế ròng**. CI vẫn chứa 0, dữ liệu đã bị nhìn hết, và con số đẹp được chọn sau ~72 lần thử. Đúng như V4 §12: tiêu chuẩn thành công là lợi thế ròng dương **bền theo thời gian**, không phải một win rate đẹp trên tập đã biết.

Việc cần làm không phải tin V5, mà là implement nó và để forward shadow phán quyết.
