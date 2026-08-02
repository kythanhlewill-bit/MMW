# MMW — Hướng Chiến Lược cho Autotrade Cá Nhân

> **Tài liệu số 02 của tuyến autotrade.** Tài liệu 01 xây *hệ đo*. Tài liệu này trả lời câu hỏi
> mà hệ đo đó sẽ đo: **đi hướng chiến lược nào thì khả thi nhất và sinh lợi tốt nhất có thể?**
>
> Ngày lập: **2026-07-31** · Repo: `D:/KYLT/MMW` · Người dùng: 1 · Quỹ thời gian: 15–20 h/tuần
>
> **CẢNH BÁO ĐỌC TRƯỚC — giống tài liệu 01 và nghiêm hơn:**
> Tài liệu này **không chứa kết quả backtest đã kiểm định của MMW**. Nó chứa **phép đo sàng lọc
> (screening measurement)** mà tôi tự chạy trên dữ liệu Binance public API trong lúc soạn.
> Phép đo sàng lọc **KHÁC** backtest đã kiểm định ở bốn điểm, và bốn điểm này quyết định
> cách bạn được phép dùng mọi con số bên dưới:
>
> | | Phép đo sàng lọc (tài liệu này) | Backtest đã kiểm định (tài liệu 01 §5) |
> |---|---|---|
> | Mục đích | **Loại trừ** hướng đi vô vọng | **Xác nhận** một hướng đi |
> | Out-of-sample | **Không có.** Toàn bộ là in-sample | Vùng OOS mở đúng một lần |
> | Hiệu chỉnh multiple testing | **Không.** Tôi thử ~40 biến thể | Bonferroni + DSR bắt buộc |
> | Mô hình chi phí | Fee phẳng 0,10% khứ hồi, **không** funding, **không** slippage | Fee thật + funding từng kỳ + độ nhạy slippage |
>
> ⇒ **Con số trong tài liệu này đủ mạnh để nói "KHÔNG", không đủ mạnh để nói "CÓ".**
> Đó chính xác là cách tôi dùng chúng. Mọi con số đều thuộc một trong ba loại:
> **(a)** tự đo từ Binance public API, ghi `[đo 2026-07-31]`;
> **(b)** trích mã nguồn `file.cs:dòng`;
> **(c)** trích nguồn công khai có ngày.
> Không có loại (d) "chiến lược này sẽ kiếm X%/tháng".

---

## 0. Tóm tắt điều hành — 7 kết luận, đọc cái này là đủ

| # | Kết luận | Bằng chứng |
|---|---|---|
| 1 | **Ở khung 1h–1 tuần, 6 symbol thanh khoản nhất gần như là martingale.** Variance Ratio ≈ 1,00 ở mọi horizon từ 2h đến 168h; `|z|` lớn nhất trong 36 phép thử là **1,14** — không cái nào đạt mức ý nghĩa 5%. Không có tự tương quan tuyến tính để khai thác. | 11.999 nến 1h × 6 symbol, [đo 2026-07-31]. §2.1 |
| 2 | **Lõi deterministic hiện tại của MMW có edge gộp +0,06 R — nhỏ hơn chi phí giao dịch 0,118 R.** Net = **−0,06 R**, khoảng tin cậy 95% `[−0,104; −0,015]` **nằm trọn dưới 0**. Đường chuẩn ngẫu nhiên cho +0,00 R, xác nhận engine đo không có lookahead. | 4.016 lệnh mô phỏng, [đo 2026-07-31]. §2.2 |
| 3 | **Chi phí/R chênh nhau 35 lần và nó là BIẾN THIẾT KẾ, không phải biến thị trường.** SOL khung 1d với SL 4 ATR: **0,0034 R/lệnh**. BTC khung 1h với SL 1,5 ATR: **0,1129 R/lệnh**. Đây là đòn bẩy lớn nhất trong toàn bộ bài toán — và nó tất định 100%. | ATR(14) đo trên 3 khung × 3 symbol, [đo 2026-07-31]. §2.3 |
| 4 | **Không archetype nào tôi đo được đạt ngưỡng +0,20 R.** Trend, breakout, mean-reversion, funding carry, cross-sectional funding, ở 1h/4h/1d, với SL/TP cố định hoặc trailing: **tất cả rơi vào dải +0,01 đến +0,11 R net**. Ngoại lệ duy nhất (+0,33 R) có n=79 và **95,9% lợi nhuận đến từ 2 tháng trên 16**. | ~40 biến thể, [đo 2026-07-31]. §2.4–2.7 |
| 5 | **Đường chuẩn ngẫu nhiên của chiến lược có trailing stop KHÔNG bằng 0 — nó bằng +0,039 R.** Trailing stop tự nó tạo độ lệch dương kể cả khi hướng lệnh hoàn toàn ngẫu nhiên. Biến thể tốt nhất của tôi (+0,086 R) nằm ở **phân vị 98,7** của phân bố null — tức **VÙNG XÁM**, không phải GO, theo chính bảng cổng §4.3 của tài liệu 01. | 300 lần chạy null, [đo 2026-07-31]. §2.7 |
| 6 | **Phán quyết LLM: bỏ hẳn khỏi vòng lặp giao dịch, không phải hạ xuống làm veto.** Lý do không phải "LLM tệ" mà là số học: để chứng minh một veto cộng thêm +0,05 R cần ~5.500 lệnh; chiến lược khung ngày sinh ~100 lệnh/năm ⇒ **55 năm**. Giá trị của veto là **không thể đo được về mặt cấu trúc** ⇒ theo đúng chuẩn của chính tài liệu 01, nó phải bị loại. | §4.2 tài liệu 01 + đo tần suất lệnh. §4 |
| 7 | **Bây giờ là thời điểm tệ để CHẠY và tốt để XÂY.** BTC −48,7% từ đỉnh, EMA50 < EMA200 (downtrend), biến động 30 ngày ở **phân vị 12** của 1.438 ngày. Trend-following chết trong chop biến động thấp. Funding hiện tại 4,8–11,0%/năm — **cách rất xa** ngưỡng 82%/năm mà tín hiệu funding cần. Không có setup nào đang bật. | [đo 2026-07-31]. §7 |

### Kết luận một câu

> **Không có archetype nào vượt ngưỡng +0,20 R một cách đáng tin ở quy mô này; hướng khả thi nhất
> không phải là tìm tín hiệu tốt hơn mà là hạ chi phí xuống dưới edge — nghĩa là chuyển từ khung 1h
> sang khung ngày, bỏ LLM khỏi vòng lặp, và chấp nhận rằng kết quả sẽ mất nhiều năm mới biết.**

---

## 1. Loại trừ bằng ràng buộc

### 1.1 Bốn ràng buộc cứng, và cái gì chết vì cái nào

| Mã | Ràng buộc | Neo |
|---|---|---|
| **R1** | **Không có WebSocket.** Chỉ REST polling theo lịch Hangfire `*/5` | `grep -rli "websocket\|wss://" src/` = 0 · `Program.cs:126–129` |
| **R2** | **Không có order book, không có tick data.** `IMarketDataProvider` chỉ trả klines | `BinanceMarketDataProvider.cs:44–45` |
| **R3** | **Vốn $2.000–$20.000, notional cap 20–50 USDT** | `LiveTradingOptions.cs:24,27` |
| **R4** | **1 người, 15–20 h/tuần, không trực máy** | Brief |
| **R5** | **Chỉ Binance USDT-M Futures. Không có spot.** ⇒ **không dựng được vị thế delta-neutral** | `BinanceOptions.cs` |

**R5 ít được nhắc nhưng nó giết cả một nhóm archetype.** Cash-and-carry / funding arbitrage kinh điển
là *long spot + short perp* — thu funding mà không chịu rủi ro hướng. MMW **không có adapter spot**,
nên mọi chiến lược "ăn funding" ở đây bắt buộc là **vị thế hướng trần trụi**. Đó là một chiến lược
hoàn toàn khác, với rủi ro hoàn toàn khác, và nó **không** phải là arbitrage.

### 1.2 Bảng loại trừ archetype

| Archetype | Khả thi? | Vì sao — neo vào ràng buộc |
|---|---|---|
| **Market making** | ❌ **CHẾT** | Cần order book realtime + huỷ/đặt lệnh dưới giây (R1, R2). Cần rebate maker mà tài khoản bán lẻ không có. Cạnh tranh với HFT co-located. Không cần bàn thêm. |
| **Scalping** | ❌ **CHẾT** | Chu kỳ quyết định 5 phút (R1) dài hơn toàn bộ vòng đời một lệnh scalp. Chi phí/R ở khung phút vượt 0,5 R. |
| **Latency / statistical arbitrage** | ❌ **CHẾT** | Cần đo lệch giá giữa các sàn theo mili-giây (R1). Chỉ có 1 adapter sàn (R5). |
| **Funding-rate arbitrage (cash & carry)** | ❌ **CHẾT** | **Cần spot để hedge (R5).** Không có spot ⇒ không delta-neutral ⇒ không phải arbitrage. |
| **Grid trading** | ❌ **CHẾT về mặt kỳ vọng** | Grid là short-gamma: nhiều lãi nhỏ, một lỗ thảm. Kỳ vọng gộp = 0 trên martingale (§2.1), và phí ăn mỗi mắt lưới. Ở 529 symbol có 258 symbol đã huỷ niêm yết (§3.2 tài liệu 01), grid trên alt = rủi ro về 0. **Không đo được edge vì edge không tồn tại về mặt lý thuyết.** |
| **Mean-reversion nhanh (dưới 1h)** | ❌ **CHẾT** | Cần phản ứng dưới phút (R1). Và đo được: **nghịch đảo lõi MMW cho −0,03 R gộp** (§2.2) — chiều mean-reversion còn tệ hơn chiều trend. |
| **DCA / Martingale gia tăng** | ❌ **CHẾT** | Không phải chiến lược mà là biến đổi phân bố: đổi nhiều lãi nhỏ lấy một lỗ vô hạn. Vi phạm Hiến chương I (tăng tần suất/đòn bẩy) và III. |
| **Pairs trading (cointegration)** | ⚠️ **Về lý thuyết được, thực tế không** | Không cần websocket (khung ngày ổn). **Nhưng:** cần ≥ 2 chân/lệnh ⇒ chi phí ×2; notional cap 50 USDT (R3) ⇒ mỗi chân 25 USDT, sát mức tối thiểu của sàn; cointegration trên crypto vỡ liên tục vì tokenomics thay đổi. **Chi phí/R gấp đôi mà edge không gấp đôi.** |
| **Cross-sectional momentum** | ⚠️ **Được, nhưng** | Khung ngày, không cần websocket. Có bằng chứng công khai (§3). **Nhưng** cần vũ trụ ≥ 30–50 symbol để xếp hạng có nghĩa; ở notional 50 USDT × 10 vị thế = 500 USDT tổng, và mỗi symbol alt có slippage cao. Xem §6. |
| **Carry / funding làm TÍN HIỆU** (không phải arbitrage) | ⚠️ **Được, hiếm** | Đo được: có gradient đúng chiều (§2.5). **Nhưng** chỉ có tín hiệu ở đuôi cực đoan, ~5 lệnh/tháng, và bằng chứng tập trung vào 2 tháng (§2.6). |
| **Breakout (Donchian) khung ngày** | ✅ **KHẢ THI** | Quyết định 1 lần/ngày ⇒ REST polling thừa sức (R1). Chi phí/R thấp nhất (§2.3). Tần suất ~10 lệnh/symbol/năm ⇒ hợp R4. |
| **Trend-following khung ngày** | ✅ **KHẢ THI** | Như trên. Có bằng chứng công khai mạnh nhất trong nhóm (§3). **Là archetype duy nhất tự nhiên cho R lớn/lệnh** — vì trailing stop không cắt đuôi lãi (§2.7, phân bố R có max +8,29 R). |

### 1.3 Vì sao trend-following là archetype duy nhất "tự nhiên cho edge lớn/lệnh"

Brief yêu cầu nói rõ archetype nào tự nhiên cho R lớn. Câu trả lời có cơ sở cấu trúc, không phải sở thích:

- **Edge/lệnh = (biên độ move) / (khoảng SL).** Muốn R lớn, hoặc move phải lớn, hoặc SL phải hẹp.
- SL hẹp ⇒ bị quét bởi nhiễu ⇒ win-rate sụp. Đây là ngõ cụt.
- ⇒ Chỉ còn cách **để move lớn**. Mà move lớn cần **thời gian**. Không có cách nào bắt một move
  4 R trong 3 giờ mà không dùng đòn bẩy phi lý.
- **Trend-following là archetype duy nhất mà cấu trúc payoff của nó KHÔNG chặn trên.** Mean-reversion
  có mục tiêu cố định (giá về trung bình) ⇒ R chặn trên theo thiết kế. Market-making chặn ở spread.
  Grid chặn ở bước lưới. Chỉ trend-following để ngỏ đuôi phải.
- **Đo được điều này:** phân bố R của biến thể trailing khung ngày là p50 = **−0,26**, p90 = **+1,31**,
  p99 = **+2,98**, max = **+8,29** [đo 2026-07-31, §2.7]. Trung vị âm, đuôi phải dài. Đó là chữ ký
  của trend-following, và đó là lý do nó là ứng viên duy nhất còn lại.

**Hệ quả cho MMW ngay lập tức:** `SignalGenerator.cs:12` đặt `RewardRisk = 2m` — **TP cố định ở 2R
cắt cụt chính cái đuôi phải là nguồn edge duy nhất của archetype này.** Đây không phải chi tiết nhỏ;
nó là mâu thuẫn thiết kế ở tầng gốc. Đo được: cùng entry, TP cố định RR=2 cho +0,069 R gộp, trailing
cho +0,094 R gộp (§2.7).

---

## 2. Phép đo — cái gì thật sự đo được

### 2.0 Phương pháp

Toàn bộ đo bằng PowerShell gọi thẳng `https://fapi.binance.com/fapi/v1/klines` (**PERP, không phải
spot** — tránh bug §3.7 tài liệu 01). Quy tắc chống lookahead áp đúng theo tài liệu 01 §3.1:

1. Loại nến chưa đóng (`closeTime >= now`) khỏi mọi chuỗi.
2. Quyết định trên nến `i` đã đóng, **vào lệnh tại `Open[i+1]`**.
3. Không cho lệnh chồng lấn trên cùng symbol.
4. Nến chạm cả SL lẫn TP ⇒ tính **SL** (bi quan).
5. Mọi biến thể đã thử **đều được báo cáo**, kể cả biến thể xấu (§3.4a tài liệu 01).

**Kiểm tra tự-kiểm bắt buộc (§8 tuần 9 tài liệu 01):** đường chuẩn ngẫu nhiên với SL/TP cố định cho
**E[R] gộp = −0,00** trên 4.219 lệnh [đo 2026-07-31]. Đúng bằng 0 như lý thuyết đòi hỏi ⇒ **engine đo
không có lookahead**. Mọi con số sau đây đứng trên nền đã kiểm này.

### 2.1 Thị trường có tự tương quan không? — Variance Ratio

Đây là phép thử nền tảng. Nếu VR ≈ 1 thì chuỗi giá là martingale, và **định lý dừng tối ưu nói mọi
chiến lược chỉ dùng giá đều có kỳ vọng gộp bằng 0** — bất kể SL/TP đặt ở đâu.

VR(q) = Var(lợi suất q kỳ) / (q × Var(lợi suất 1 kỳ)). VR > 1 = xu hướng dai dẳng; VR < 1 = hồi quy
trung bình. `z` là thống kê Lo–MacKinlay bền với phương sai thay đổi.

| Symbol | VR(2) | z | VR(4) | z | VR(8) | z | VR(24) | z | VR(48) | z | VR(168) | z |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| BTCUSDT | 0,989 | −0,75 | 0,973 | −0,97 | 0,965 | −0,85 | 0,944 | −0,80 | 0,919 | −0,84 | 0,885 | −0,69 |
| ETHUSDT | 1,012 | 0,83 | 1,016 | 0,62 | 1,037 | 0,97 | 1,071 | 1,09 | 1,099 | 1,11 | 1,093 | 0,59 |
| SOLUSDT | 1,004 | 0,28 | 1,001 | 0,05 | 1,010 | 0,26 | 1,007 | 0,11 | 0,997 | −0,04 | 0,899 | −0,64 |
| BNBUSDT | 1,003 | 0,19 | 0,976 | −0,79 | 0,972 | −0,62 | 0,979 | −0,27 | 0,985 | −0,14 | 0,900 | −0,53 |
| XRPUSDT | 0,993 | −0,41 | 0,986 | −0,47 | 1,004 | 0,08 | 0,978 | −0,29 | 0,898 | −1,00 | 0,806 | −1,14 |
| DOGEUSDT | 0,995 | −0,27 | 0,982 | −0,59 | 1,006 | 0,13 | 1,015 | 0,21 | 1,010 | 0,10 | 0,873 | −0,79 |

*(11.999 nến 1h/symbol, 2025-03-18 → 2026-07-31, `fapi/v1/klines`, [đo 2026-07-31])*

**Đọc bảng này cho đúng:**

- **36 phép thử, `|z|` lớn nhất = 1,14.** Cần `|z| > 1,96` mới có ý nghĩa ở mức 5%. **Không một
  phép thử nào đạt.** Không thể bác bỏ giả thuyết martingale ở bất kỳ horizon nào từ 2 giờ đến 1 tuần.
- **Đây KHÔNG phải bằng chứng "không có edge".** VR chỉ bắt tự tương quan **tuyến tính**. Cấu trúc
  phi tuyến, có điều kiện, hoặc phụ thuộc chế độ vẫn có thể tồn tại — và §2.5 cho thấy có thật.
- **Nhưng nó là bằng chứng mạnh rằng trend-following/momentum KHÔNG ĐIỀU KIỆN ở khung 1h không có
  nguồn edge.** Nếu bạn định xây một chiến lược "EMA cắt nhau, vào lệnh", bảng này nói trước kết quả.
- Có một mẫu hình nhẹ đáng ghi nhận: VR(168) < 1 ở 5/6 symbol (0,806–1,093) — gợi ý hồi quy trung bình
  yếu ở thang tuần. Nhưng `|z| ≤ 1,14` ⇒ **không kết luận được**, chỉ ghi nhận.

### 2.2 Lõi deterministic hiện tại của MMW đáng giá bao nhiêu?

Tái cài đặt đúng `MarketAnalyzer.cs:35–53` (score = [giá > EMA20 > EMA50] + [MACD histogram > 0]),
ngưỡng `|score| ≥ 2` (`AppSetting.cs:18`), SL = 1,5 ATR và TP = 2R (`SignalGenerator.cs:9,12`),
horizon 48 nến.

**Khung 1h, 6 symbol, chi phí 0,118 R** [đo 2026-07-31]:

| Chiến lược | N | Gộp | **Net** | σ_R | Win% | PF | CI 95% của Net | Mơ hồ | Trung vị nến |
|---|---|---|---|---|---|---|---|---|---|
| **Lõi MMW** (EMA20/50 + MACD) | 4.016 | +0,06 | **−0,06** | 1,41 | 36,1 | 1,09 | **[−0,104; −0,015]** | 0,10% | 8 |
| Donchian-20 breakout | 2.743 | +0,05 | −0,07 | 1,41 | 35,9 | 1,08 | [−0,120; −0,015] | 0,11% | 7 |
| Nghịch đảo lõi (mean-reversion) | 4.466 | −0,03 | −0,15 | 1,37 | 33,8 | 0,96 | [−0,185; −0,106] | 0,07% | 8 |
| **NULL: hướng ngẫu nhiên** | 4.219 | **−0,00** | −0,12 | 1,39 | 34,2 | 1,00 | [−0,163; −0,079] | 0,05% | 8 |

**Khung 4h, 6 symbol** [đo 2026-07-31]:

| Chiến lược | N | Gộp | Net | Win% | PF |
|---|---|---|---|---|---|
| Lõi MMW | 2.042 | +0,09 | −0,03 | 37,1 | 1,15 |
| Donchian-20 | 1.390 | +0,10 | −0,02 | 37,6 | 1,16 |
| Nghịch đảo lõi | 2.224 | −0,04 | −0,16 | 32,9 | 0,93 |
| NULL: hướng ngẫu nhiên | 2.093 | +0,05 | −0,06 | 36,1 | 1,08 |

**Bốn điều rút ra, xếp theo tầm quan trọng:**

1. **Lõi có tín hiệu thật nhưng cực nhỏ.** +0,06 R gộp trên 4.016 lệnh (σ=1,41 ⇒ sai số chuẩn 0,022
   ⇒ t ≈ 2,7). Nó **không** phải nhiễu thuần. Nhưng nó **nhỏ hơn chi phí 0,118 R**.
2. **Net âm với độ tin cậy cao.** CI 95% `[−0,104; −0,015]` nằm trọn dưới 0. Đây là điều gần nhất
   với một kết luận chắc chắn trong toàn bộ tài liệu: **cấu hình 1h/SL 1,5 ATR/TP 2R hiện tại lỗ tiền.**
3. **Chiều tín hiệu đúng.** Trend +0,06 vs mean-reversion −0,03 ⇒ khoảng cách 0,09 R giữa hai chiều.
   Có một thiên hướng trend thật, chỉ là quá nhỏ để trả phí.
4. **Ở 4h edge gộp nhỉnh hơn (+0,09) nhưng null cũng nhỉnh hơn (+0,05)** ⇒ phần vượt null chỉ +0,04.
   Ở 1h phần vượt null là +0,06. **Đổi khung thời gian không làm edge to ra.**

### 2.3 Chi phí/R — biến thiết kế bị bỏ quên

`chi_phí_R = phí_khứ_hồi_% / (bội_số_SL × ATR%)`. Giả định phí taker 0,05%/chiều (§A17 tài liệu 01 —
**vẫn chưa xác minh**, phải đọc `fapi/v1/commissionRate`).

| Symbol | Khung | ATR(14) % giá | SL = 1,5 ATR | SL = 2,5 ATR | SL = 4,0 ATR |
|---|---|---|---|---|---|
| BTCUSDT | 1h | 0,591% | **0,1129 R** | 0,0677 R | 0,0423 R |
| BTCUSDT | 4h | 1,286% | 0,0518 R | 0,0311 R | 0,0194 R |
| BTCUSDT | **1d** | **3,721%** | **0,0179 R** | 0,0107 R | 0,0067 R |
| ETHUSDT | 1h | 0,784% | 0,0851 R | 0,0510 R | 0,0319 R |
| ETHUSDT | 4h | 2,039% | 0,0327 R | 0,0196 R | 0,0123 R |
| ETHUSDT | **1d** | **5,159%** | 0,0129 R | 0,0078 R | 0,0048 R |
| SOLUSDT | 1h | 0,888% | 0,0751 R | 0,0451 R | 0,0282 R |
| SOLUSDT | 4h | 2,287% | 0,0291 R | 0,0175 R | 0,0109 R |
| SOLUSDT | **1d** | **7,312%** | 0,0091 R | 0,0055 R | **0,0034 R** |

*[đo 2026-07-31, 3.000 nến 1h/4h và 1.500 nến 1d]*

**Đây là bảng quan trọng nhất trong tài liệu này.**

- **Tỷ lệ giữa ô đắt nhất và rẻ nhất là 33 lần** (0,1129 / 0,0034).
- Nó **hoàn toàn tất định**. Không cần dự đoán gì. Không cần LLM. Không cần backtest để biết.
- §2.2 cho thấy edge gộp gần như **bất biến** theo khung thời gian (+0,05 đến +0,10 R). Chi phí thì
  **biến thiên 33 lần**. ⇒ **Toàn bộ trò chơi nằm ở mẫu số, không phải tử số.**
- Chuyển từ (1h, SL 1,5 ATR, BTC) sang (1d, SL 2,5 ATR, BTC) đưa chi phí từ **0,1129 R → 0,0107 R**,
  tức **cắt 90,5% chi phí** mà không cần cải thiện tín hiệu một chút nào.

**So sánh trực tiếp với tài liệu 01:** tài liệu 01 §3.5 tính chi phí 0,118 R và §4.1 suy ra win-rate
hoà vốn 37,7%. Ở khung ngày với SL 2,5 ATR, chi phí là 0,0107 R ⇒ **win-rate hoà vốn tụt về 33,7%** —
gần như đúng con số lý thuyết 33,3%. Chi phí gần như biến mất khỏi bài toán.

### 2.4 Funding có dự báo được lợi suất không?

Cơ chế kinh tế rõ ràng: funding dương = phe LONG trả tiền phe SHORT = long đang đông đúc. Vị thế
đông đúc dễ bị thanh lý cưỡng bức. **Đây là loại edge KHÔNG phải dự đoán giá** — nó là đo lường
định vị (positioning), quan sát được trực tiếp.

Với mỗi mốc funding 8h, chia rổ theo phân vị của funding rate, đo lợi suất PERP về sau:

| Rổ funding | N | rate TB %/8h | fwd 8h % | fwd 24h % | fwd 72h % |
|---|---|---|---|---|---|
| p0–p5 (short đông nhất) | 449 | −0,01998 | +0,101 | **+0,185** | +0,069 |
| p5–p20 | 1.347 | −0,00520 | +0,099 | +0,213 | +0,364 |
| p20–p40 | 1.797 | −0,00040 | −0,002 | −0,008 | −0,081 |
| p40–p60 | 1.796 | +0,00211 | −0,032 | −0,041 | −0,237 |
| p60–p80 | 1.797 | +0,00579 | −0,019 | −0,144 | −0,384 |
| p80–p95 | 1.347 | +0,00957 | −0,074 | −0,066 | +0,144 |
| **p95–p100 (long đông nhất)** | 449 | +0,01091 | −0,166 | **−0,410** | **−0,883** |

*(8.982 mốc funding, 6 symbol, [đo 2026-07-31]. Trung bình chung fwd24h = −0,028%, σ = 3,288)*

- Gradient **đơn điệu và đúng chiều** ở cột fwd24h từ p20 trở lên. Đây là cấu trúc, không phải một rổ may mắn.
- Top-5%: t = **−2,32**. Bottom-5%: t = **+0,99**.
- **Nhưng:** cửa sổ 24h chồng lấn 3 mốc funding ⇒ số quan sát độc lập chỉ ~1/3 ⇒ **t hiệu chỉnh ≈ −1,34**.
  **Không đạt mức ý nghĩa.**
- Và mẫu này nằm trong một thị trường giảm (trung bình fwd24h âm) ⇒ một phần hiệu ứng có thể là
  "funding cao xảy ra ở đỉnh", tức là **trùng biến**, không phải nhân quả.

### 2.5 Chuyển tín hiệu funding thành chiến lược đo bằng R

Vũ trụ 20 symbol lâu đời, 1.500 mốc funding (2025-03-18 → 2026-07-31), ATR(14,1h) TB = 1,098%.
Cắt ngang: short N symbol funding cao nhất, long N symbol funding thấp nhất.

| Biến thể | N | Gộp | Chi phí | **Net** | t (gộp) | Win% | PF |
|---|---|---|---|---|---|---|---|
| top3, SL 1,5 ATR, RR 2, H 24h | 8.988 | +0,04 | 0,06 | −0,02 | 2,69 | 39,0 | 1,06 |
| top3, SL 2,5 ATR, RR 2, H 48h | 8.988 | +0,05 | 0,04 | +0,01 | 3,46 | 40,8 | 1,08 |
| top3, SL 4 ATR, RR 1,5, H 72h | 8.988 | +0,02 | 0,02 | +0,00 | 2,13 | 45,6 | 1,05 |
| top5, SL 2,5 ATR, RR 2, H 48h | 14.980 | +0,04 | 0,04 | +0,01 | 4,23 | 40,6 | 1,08 |
| top1, SL 2,5 ATR, RR 2, H 48h | 2.996 | +0,05 | 0,04 | +0,01 | 2,21 | 41,0 | 1,09 |
| top3, SL 2,5 ATR, RR 1, H 24h | 8.988 | +0,02 | 0,04 | −0,01 | 2,33 | 51,2 | 1,05 |
| **NULL hướng ngẫu nhiên** | 8.988 | +0,02 | 0,04 | −0,02 | 1,42 | 39,8 | 1,03 |

**Kết quả nhất quán đến mức nhàm chán: gộp +0,02 đến +0,05 R, vượt null khoảng +0,02 đến +0,03 R.**
t cao (đến 4,23) **chỉ vì N lớn**, không vì edge lớn. Đây chính xác là cái bẫy mà tài liệu 01 §4.2
cảnh báo: edge có ý nghĩa thống kê nhưng **không có ý nghĩa kinh tế**, vì nó nhỏ hơn mọi sai số vận hành.

### 2.6 Chọn lọc có làm edge to lên không? — quét ngưỡng, và cái bẫy bên trong

Đây là câu hỏi đúng: nếu edge/lệnh quá nhỏ, hãy giao dịch **ít hơn nhưng chọn lọc hơn**. Quét ngưỡng
`|funding rate|` (chi phí 0,0364 R ở SL 2,5 ATR). **Toàn bộ 9 ngưỡng đã thử đều được báo cáo:**

| Ngưỡng %/8h | ~%/năm | N | Gộp | **Net** | t | Win% | PF |
|---|---|---|---|---|---|---|---|
| 0 (không lọc) | 0 | 29.960 | 0,0454 | 0,0089 | 6,22 | 40,8 | 1,083 |
| 0,01 | 11 | 10.437 | 0,0715 | 0,0351 | 5,75 | 41,7 | 1,132 |
| 0,02 | 21,9 | 1.312 | 0,0716 | 0,0352 | 2,10 | 42,9 | 1,138 |
| 0,03 | 32,8 | 575 | 0,1001 | 0,0637 | 1,94 | 43,3 | 1,199 |
| 0,05 | 54,8 | 170 | 0,1452 | 0,1088 | 1,60 | 48,8 | 1,323 |
| **0,075** | **82,1** | **79** | **0,3284** | **0,2920** | **2,43** | **58,2** | **1,857** |
| 0,10 | 109,5 | 49 | 0,3676 | 0,3312 | 2,20 | 61,2 | 2,054 |
| 0,15 | 164,3 | 26 | 0,3874 | 0,3510 | 1,73 | 61,5 | 2,244 |
| 0,20 | 219 | 15 | — | quá ít mẫu | — | — | — |

**E[R] tăng đơn điệu theo độ chọn lọc — 8/8 bước.** Đó là cấu trúc thật, không phải một ô may mắn.
Và ở ngưỡng 0,075%/8h, **Net = +0,292 R — vượt cả ngưỡng +0,20 R lẫn ngưỡng lý tưởng +0,30 R gộp.**

**Đây là ứng viên duy nhất trong toàn bộ tài liệu vượt ngưỡng. Nên tôi mổ nó kỹ. Nó không sống sót.**

| Chẩn đoán tại ngưỡng 0,075%/8h | Kết quả | Phán quyết |
|---|---|---|
| Số lệnh | 79 | Tài liệu 01 §4.2: cần **155 lệnh** cho edge +0,30 R. **Chỉ có một nửa.** |
| Số ngày riêng biệt | 41 | 79 lệnh nhưng chỉ 41 ngày ⇒ **lệnh không độc lập** |
| Số symbol riêng biệt | 9 | Funding cực đoan xảy ra đồng loạt toàn thị trường |
| **Phân bố hướng** | **79/79 là LONG** | **Không có một lệnh SHORT nào.** Đây **không** phải chiến lược hai chiều — nó là "mua khi phe short đông nhất", một cược **một chiều trong một thị trường giảm** |
| **Tập trung theo tháng** | 2025-10: +13,87 R (n=17)<br>2025-11: +11,00 R (n=7)<br>Tổng toàn kỳ: +25,94 R | **24,87 / 25,94 = 95,9% lợi nhuận đến từ 2 tháng trên 16** |
| **Bootstrap theo cụm-ngày, CI 95%** | **[−0,051; +0,702]** | **Chạm 0.** p(E[R] ≤ 0) = 0,048 |
| Bonferroni (9 ngưỡng đã quét) | cần p < 0,0056 ⇒ t > 2,77 | t = 2,43 ⇒ **TRƯỢT** |
| Ngưỡng 0,05 và 0,10 (lân cận) | p = 0,142 và p = 0,069 | **Không có "cao nguyên"** (§3.3 quy tắc 4 tài liệu 01) |

**Phán quyết: đây là một đỉnh nhọn dựng trên 2 tháng của một thị trường, không phải một edge.**
Nó có cơ chế kinh tế hợp lý (bắt short squeeze khi phe short kiệt quệ) và **dấu đúng**, nhưng
bằng chứng không đạt bất kỳ cổng nào trong bảng §4.3 của tài liệu 01. Ghi nhận là **giả thuyết đáng
theo dõi**, không phải chiến lược đáng cấp vốn.

### 2.7 Khung ngày + trailing stop — và phát hiện quan trọng nhất về phương pháp

Vũ trụ 20 symbol lâu đời, nến ngày từ **2020-10-15** (≈5,8 năm — bao trùm bull 2021, bear 2022,
sideway 2023, bull 2024–25, sụp 2026). ATR(14) ngày TB = 7,514% ⇒ chi phí cực thấp.

| Biến thể | N | Gộp | Chi phí | **Net** | t | Win% | PF | Trung vị ngày |
|---|---|---|---|---|---|---|---|---|
| **SL/TP cố định (RR=2), horizon 60 ngày** |
| EMA20/50 | 2.400 | 0,040 | 0,009 | 0,031 | 1,40 | 36,0 | 1,06 | 8 |
| EMA20/50 + lọc EMA200 | 2.150 | 0,040 | 0,009 | 0,031 | 1,34 | 36,1 | 1,06 | 7 |
| Donchian-20 | 1.492 | 0,056 | 0,009 | 0,047 | 1,53 | 36,6 | 1,09 | 6 |
| Donchian-20 + lọc EMA200 | 1.175 | 0,069 | 0,009 | 0,060 | 1,69 | 37,2 | 1,11 | 6 |
| **Trailing Chandelier (không chặn trên), horizon 120 ngày** |
| Donchian + EMA200, trail 2 ATR | 1.150 | **0,094** | 0,007 | **0,087** | **3,00** | 41,1 | 1,28 | 6 |
| Donchian + EMA200, trail 3 ATR | 858 | 0,046 | 0,004 | 0,042 | 1,34 | 40,1 | 1,13 | 12 |
| Donchian + EMA200, trail 4 ATR | 678 | 0,014 | 0,003 | 0,011 | 0,39 | 41,7 | 1,04 | 23 |
| EMA20/50 + EMA200, trail 2 ATR | 2.225 | 0,061 | 0,007 | 0,054 | 2,44 | 38,1 | 1,17 | 8 |
| EMA20/50 + EMA200, trail 3 ATR | 1.222 | 0,043 | 0,004 | 0,038 | 1,36 | 37,4 | 1,12 | 15 |
| EMA20/50 + EMA200, trail 4 ATR | 792 | 0,075 | 0,003 | 0,071 | 2,00 | 40,9 | 1,22 | 30 |
| **NULL hướng ngẫu nhiên, trail 3 ATR** | 881 | **−0,003** | 0,004 | −0,007 | −0,09 | 36,8 | 0,99 | 13 |
| **NULL hướng ngẫu nhiên, RR=2 cố định** | 1.489 | **0,056** | 0,009 | 0,047 | 1,53 | 36,3 | 1,09 | 7 |

**Hai điều phải đọc kỹ ở bảng này.**

**(1) Dòng NULL với RR=2 cố định cho +0,056 R — BẰNG ĐÚNG Donchian-20 thật (+0,056).**
Ở khung ngày với SL/TP cố định, **hướng lệnh ngẫu nhiên tốt ngang chiến lược**. Toàn bộ nhóm
"SL/TP cố định khung ngày" (4 dòng đầu, t = 1,34–1,69) **không phân biệt được với tung đồng xu**.

**(2) Đường chuẩn ngẫu nhiên của trailing stop KHÔNG bằng 0.** Chạy 300 lần null với hướng ngẫu nhiên
trên cùng 1.675 điểm vào lệnh [đo 2026-07-31]:

| Cấu hình | Thật | Null: TB | Null: σ | Null p50 | Null p95 | **Null p99** | Null max | Vượt bao nhiêu/300 | p thực nghiệm |
|---|---|---|---|---|---|---|---|---|---|
| Trail 2 ATR | **0,0857** | **+0,0392** | 0,0211 | 0,0405 | 0,0727 | **0,0881** | 0,0931 | 296/300 | 0,0166 |
| Trail 3 ATR | 0,0591 | +0,0329 | 0,0212 | 0,0348 | 0,0658 | 0,0777 | 0,0908 | 269/300 | 0,1063 |

**Diễn giải — đây là phát hiện phương pháp luận quan trọng nhất tài liệu:**

- **Trailing stop tự nó tạo E[R] gộp dương (+0,039 R) kể cả khi hướng lệnh hoàn toàn ngẫu nhiên.**
  Lý do: trailing stop biến phân bố R thành lệch dương (nhiều lỗ nhỏ bị cắt, đuôi lãi để ngỏ).
  Đó là hiệu ứng **hình dạng exit**, không phải kỹ năng dự báo.
- ⇒ **Mọi so sánh trend-following với mốc 0 đều SAI.** Mốc đúng là +0,039 R.
- ⇒ Edge thật của biến thể tốt nhất = 0,0857 − 0,0392 = **+0,047 R**, không phải +0,086 R.
  **Đường chuẩn ngẫu nhiên vừa cắt một nửa edge biểu kiến.**
- Chấm theo bảng cổng §4.3 tài liệu 01, tiêu chí #6 ("so với đường chuẩn ngẫu nhiên"):
  thật ở **phân vị 98,7** ⇒ nằm trong dải 95–99 ⇒ **VÙNG XÁM**, không phải GO.
  Trail 3 ATR ở phân vị 89,7 ⇒ **STOP**.
- Trail 2/3/4 ATR cho 0,094 / 0,046 / 0,014 — **không có cao nguyên**, edge sụp khi nới trail.
  §3.3 quy tắc 4 tài liệu 01: chỉ tin cao nguyên. **Không đạt.**

**Phân bố R của biến thể trailing tốt nhất** (n=858, trail 3 ATR):

| p10 | p25 | p50 | p75 | p90 | p99 | max |
|---|---|---|---|---|---|---|
| −0,88 | −0,68 | **−0,26** | +0,54 | +1,31 | +2,98 | **+8,29** |

Long: +0,073 R (n=461, t=1,38). Short: +0,015 R (n=397, t=0,37). Tần suất: **7,4 lệnh/symbol/năm**.

### 2.8 Lọc theo chế độ thị trường có cứu được không?

Chia 3.992 lệnh lõi MMW khung 1h theo đặc trưng đo **tại nến vào lệnh** (không lookahead):

| Bộ lọc | N | Gộp | Net | t | Win% |
|---|---|---|---|---|---|
| **Tất cả** | 3.992 | 0,0562 | −0,0618 | 2,52 | 36,0 |
| **Cùng chiều EMA200** | 3.370 | **0,0656** | −0,0524 | 2,69 | 36,3 |
| **Ngược chiều EMA200** | 622 | **0,0052** | −0,1128 | 0,09 | 34,7 |
| Độ dốc EMA50 Q1 (thấp nhất) | 998 | 0,0843 | −0,0337 | 1,87 | 36,7 |
| Độ dốc EMA50 Q2 | 998 | 0,0057 | −0,1123 | 0,13 | 34,3 |
| Độ dốc EMA50 Q3 | 998 | 0,0919 | −0,0261 | 2,06 | 37,9 |
| Độ dốc EMA50 Q4 (cao nhất) | 998 | 0,0428 | −0,0752 | 0,96 | 35,4 |
| ATR% Q1 | 998 | 0,0679 | −0,0501 | 1,51 | 36,3 |
| ATR% Q2 | 998 | 0,0382 | −0,0798 | 0,86 | 35,3 |
| ATR% Q3 | 998 | 0,0730 | −0,0450 | 1,63 | 36,8 |
| ATR% Q4 | 998 | 0,0456 | −0,0724 | 1,03 | 35,9 |
| Cùng chiều EMA200 **và** độ dốc ≥ trung vị | 1.887 | 0,0816 | −0,0364 | 2,50 | 37,0 |
| ... chỉ LONG | 879 | **0,1073** | −0,0107 | 2,23 | 37,8 |
| ... chỉ SHORT | 1.008 | 0,0591 | −0,0589 | 1,34 | 36,3 |

**Chỉ MỘT bộ lọc trong bốn cái thử là có thật:**

- **Cùng chiều EMA200 phân tách sạch: 0,0656 vs 0,0052.** Lệnh ngược xu hướng khung lớn **không có
  edge nào cả**. Đây là bộ lọc rẻ nhất, đơn giản nhất, và duy nhất đáng giữ. *(Ghi chú: `grep -ri
  "ema200" src/` = 0 — prompt hiện tại **yêu cầu** LLM dùng EMA200 nhưng **không bao giờ gửi**
  EMA200; §1.2(3) tài liệu 01. Bộ lọc duy nhất có giá trị đo được lại chính là bộ lọc chưa từng
  được cài đặt.)*
- **Độ dốc: Q1 0,084 / Q2 0,006 / Q3 0,092 / Q4 0,043 — không đơn điệu ⇒ nhiễu.**
- **ATR: Q1 0,068 / Q2 0,038 / Q3 0,073 / Q4 0,046 — không đơn điệu ⇒ nhiễu.**
- Kết hợp bộ lọc tốt nhất + chỉ LONG cho +0,107 R gộp — **vẫn âm sau chi phí ở khung 1h**, và
  "chỉ LONG" là một tham số chọn sau khi nhìn dữ liệu (§3.3 quy tắc 3 tài liệu 01 cấm điều này).

### 2.9 Bảng tổng hợp — mọi thứ tôi đo được, đối chiếu ngưỡng +0,20 R

| Archetype | Khung | Cấu hình tốt nhất | N | Net R | Vượt null | Đạt +0,20 R? |
|---|---|---|---|---|---|---|
| Trend (lõi MMW) | 1h | EMA20/50+MACD, SL1,5ATR, RR2 | 4.016 | **−0,06** | +0,06 | ❌ |
| Trend (lõi MMW) | 4h | như trên | 2.042 | −0,03 | +0,04 | ❌ |
| Breakout | 1h | Donchian-20 | 2.743 | −0,07 | +0,05 | ❌ |
| Breakout | 4h | Donchian-20 | 1.390 | −0,02 | +0,05 | ❌ |
| Mean-reversion | 1h | nghịch đảo lõi | 4.466 | −0,15 | −0,03 | ❌ |
| Funding cắt ngang | 8h | top3, SL2,5ATR, RR2 | 8.988 | +0,01 | +0,03 | ❌ |
| Funding chọn lọc | 8h | ngưỡng 0,03%/8h | 575 | +0,06 | — | ❌ |
| **Funding cực đoan** | 8h | **ngưỡng 0,075%/8h** | **79** | **+0,29** | — | ⚠️ **về số thì có, nhưng trượt mọi cổng kiểm chứng (§2.6)** |
| Trend | 1d | Donchian+EMA200, RR2 | 1.175 | +0,060 | **+0,000** | ❌ |
| **Trend + trailing** | **1d** | **Donchian+EMA200, trail 2ATR** | **1.150** | **+0,087** | **+0,047** | ❌ |
| Trend + trailing | 1d | Donchian+EMA200, trail 3ATR | 858 | +0,042 | +0,026 | ❌ |

**Trả lời thẳng câu hỏi của brief:**

> **Không. Không archetype nào vượt được ngưỡng +0,20 R một cách đáng tin ở quy mô này.**
>
> Ứng viên tốt nhất qua được kiểm chứng đạt **+0,047 R** vượt đường chuẩn ngẫu nhiên.
> Ứng viên duy nhất chạm +0,29 R có **n=79**, **95,9% lợi nhuận từ 2/16 tháng**, **100% một chiều**,
> **trượt Bonferroni**, và **CI bootstrap theo cụm-ngày chạm 0**.

**Hệ quả số học không thể né:** với E[R] = +0,047 và σ_R = 1,4, công thức §4.2 tài liệu 01 cho

```
n ≥ 6,183 × (1,4 / 0,047)² ≈ 5.486 lệnh
```

Chiến lược khung ngày trên 10–20 symbol sinh **~100–150 lệnh/năm** ⇒ **cần 37–55 năm để chứng minh.**
Đây không phải vấn đề kiên nhẫn. Đây là **bất khả thi về mặt cấu trúc**.

---

## 3. Bằng chứng công khai — và nó yếu đến mức nào

> **Công cụ:** `WebSearch` lỗi backend (`There's an issue with the selected model (deepseek-v4-pro)`),
> đúng như brief cảnh báo. Chuyển sang browser automation qua DuckDuckGo. Mọi nguồn dưới đây đọc được thật.

### 3.1 Bằng chứng về archetype nói chung

| Nguồn | Ngày | Nội dung liên quan | Sức nặng |
|---|---|---|---|
| **Kang, Y. & Ryu, D., "Time-series momentum and market timing in Bitcoin", *Risk Management* 28, art. 54**, link.springer.com/article/10.1057/s41283-026-00234-7 | **10/07/2026** | *"Slower momentum signals deliver better risk-adjusted performance than fast signals in Bitcoin"*; tín hiệu chậm dựa trên horizon nền **12 tuần** vượt trội so với trung bình và nhanh; tín hiệu nhanh **phản ứng thái quá với nhiễu**; mẫu hình này **ngược với thị trường cổ phiếu**. Dữ liệu từ `data.binance.vision`. | **Mạnh nhất trong danh sách.** Bình duyệt, mới, đúng tài sản, và **độc lập xác nhận phép đo §2.1–2.3 của tôi**: khung nhanh là nhiễu, khung chậm mới có tín hiệu |
| "Systematic Trend-Following with Adaptive Portfolio Construction", arxiv.org/abs/2602.11708 | 12/02/2026 | *"Cryptocurrency markets exhibit pronounced momentum effects and regime-dependent volatility"* | Yếu — preprint, chỉ đọc được abstract, không kiểm được hiệu chỉnh multiple testing |
| "Cryptocurrency market risk-managed momentum strategies", *Finance Research Letters*, sciencedirect.com/science/article/pii/S1544612325011377 | 01/11/2025 | Quản trị rủi ro momentum nâng lợi suất tuần TB từ **3,18% → 3,47%**, Sharpe năm từ **1,12 → 1,42** | **Cẩn trọng cao.** Lợi suất tuần 3,18% ⇒ >180%/năm. Con số này gần như chắc chắn **chưa trừ chi phí giao dịch thực tế và chưa xử lý survivorship** trên vũ trụ altcoin. Không dùng được để lập kế hoạch |
| "Exploring risk and return profiles of funding rate arbitrage on CEX and DEX", sciencedirect.com/science/article/pii/S2096720925000818 | 01/08/2026 | Khảo sát funding arbitrage trên Binance/BitMEX/ApolloX/Drift | **Không áp dụng được cho MMW** — funding arbitrage cần chân spot, MMW không có (R5) |
| "The Two-Tiered Structure of Cryptocurrency Funding Rate Markets", *Mathematics* 14(2):346, mdpi.com | 20/01/2026 | Granger causality trên động lực funding rate | Mô tả cấu trúc, không đưa ra chiến lược khai thác được |

### 3.2 Bằng chứng rằng MỘT NGƯỜI BÁN LẺ tái lập được — đây mới là câu hỏi thật

Brief yêu cầu phân biệt rõ hai loại bằng chứng. Phân biệt đó là mấu chốt:

> **Tôi không tìm được bất kỳ nghiên cứu công khai nào đo hiệu quả của một chiến lược crypto perp
> vận hành bởi một cá nhân, với vốn dưới $20.000, qua REST API, đã trừ chi phí thật, đã hiệu chỉnh
> multiple testing, và đã kiểm soát survivorship bias.**
>
> Không tìm được không có nghĩa là không tồn tại. Nhưng sau khi tìm, **việc nó không tồn tại tự nó
> là dữ kiện** — và nó nhất quán với ghi chú cuối Phụ lục A của tài liệu 01, vốn cũng không tìm ra
> nghiên cứu nào về EMA20/50+MACD trên perp crypto đã hiệu chỉnh multiple testing.

**Khoảng cách giữa hai loại bằng chứng, cụ thể:**

| Bằng chứng học thuật thường có | Điều kiện thật của MMW |
|---|---|
| Danh mục cân bằng lại theo tuần trên 50–100 coin | Notional cap 50 USDT/lệnh (`LiveTradingOptions.cs:27`) ⇒ không dựng nổi danh mục |
| Chi phí giả định 10–20 bp | Đo được: chi phí ăn **0,113 R** ở cấu hình 1h hiện tại (§2.3) |
| Bỏ qua funding hoặc mô hình hoá đơn giản | `grep funding src/` = 0 — MMW **không trừ funding khỏi PnL** (`TradeResultSyncService.cs:221`) |
| Vũ trụ dựng từ dữ liệu đã dọn, có xử lý delisting | Watchlist cứng 4 symbol blue-chip (`SeedData.cs:61–64`) — **mẫu cực kỳ thiên lệch** |
| Sharpe báo cáo là của **danh mục**, đã đa dạng hoá | MMW giữ 1–5 vị thế ⇒ phương sai cao hơn nhiều lần |
| Không có ràng buộc vận hành | 1 người, 15–20 h/tuần, job Hangfire không có `DisableConcurrentExecution` (`Program.cs:126–156`) |

**Phán quyết về bằng chứng:** archetype trend-following khung chậm có bằng chứng công khai **vừa phải
và nhất quán với phép đo của tôi**. Bằng chứng rằng **một người bán lẻ với ràng buộc của MMW tái lập
được nó thì gần như bằng không.** Nói cách khác: nguồn đáng tin nhất (Kang & Ryu 2026) chỉ cho tôi
**hướng đi** (chậm hơn tốt hơn nhanh), không cho tôi **kỳ vọng lợi nhuận**.

---

## 4. Deterministic vs LLM — phán quyết

### 4.1 Vị trí nào trong pipeline thì LLM thêm giá trị?

Xét từng vị trí như brief yêu cầu:

| Vị trí | LLM có thêm giá trị? | Lập luận |
|---|---|---|
| **Sinh tín hiệu** | ❌ **KHÔNG. Bỏ.** | Vi phạm trực tiếp Hiến chương II ("AI **không bao giờ** là lớp duy nhất quyết định"). Không backtest được ⇒ không rơi vào bất kỳ cổng nào của tài liệu 01. Không tái lập được ⇒ vi phạm §3.6. Và tốn $1.078/năm = **53,9% vốn ở mức $2.000** (§4.5 tài liệu 01) |
| **Lọc/veto tín hiệu** | ❌ **KHÔNG — xem §4.2, đây là phần phản biện chính** | Giải quyết được mâu thuẫn hiến chương, nhưng **giá trị của nó không thể đo được về mặt cấu trúc** |
| **Chọn SL/TP** | ❌ **KHÔNG. Nguy hiểm.** | SL/TP quyết định `RiskAmount` (`Trade.cs:40`) và đơn vị R. Để một thành phần phi tất định định nghĩa **đơn vị đo** thì mọi thống kê sau đó vô nghĩa. Hiến chương II liệt kê "bội số R" vào nhóm **PHẢI deterministic 100%**. `ApplyAiLevels` (`MarketScanService.cs:191`) đang làm đúng điều bị cấm |
| **Đọc tin vĩ mô / lịch sự kiện** | ✅ **CÓ — vị trí duy nhất** | Đây là **trích xuất dữ liệu có cấu trúc từ văn bản**, không phải phán đoán thị trường. Đầu ra là một cờ tất định: `NoTradeWindow(from, to, reason)`. Kiểm chứng được (sự kiện có xảy ra đúng giờ đó không). Rẻ: vài lượt gọi/ngày, không theo symbol |
| **Phân tích lệnh đang mở** | ⚠️ **Chỉ để ĐỌC, không để HÀNH ĐỘNG** | Với autotrade, "tư vấn" lệnh đang mở mà không ai đọc là **đốt tiền**. `Program.cs:142` chạy `*/1` = **1.440 lượt/ngày × số lệnh mở**. Hạ xuống báo cáo hàng ngày, hoặc bỏ |

### 4.2 Phản biện giả thuyết "hạ LLM xuống làm veto"

Brief đưa giả thuyết: rule deterministic sinh setup, LLM chỉ được nói KHÔNG. Đánh giá từng vế:

| Vế | Phán quyết |
|---|---|
| (i) "Backtest khả thi vì đường base deterministic" | ✅ **ĐÚNG.** Backtest được đường base, rồi so đường base có veto vs không veto |
| (ii) "Chi phí sập vì veto chỉ chạy khi có setup" | ✅ **ĐÚNG và mạnh hơn bạn nghĩ.** Ở khung ngày với 10 symbol, số setup ≈ **100/năm** ⇒ ~100 lượt gọi LLM/năm, tức **dưới $1/năm**. So với $1.078/năm hiện tại |
| (iii) "Hết mâu thuẫn hiến chương" | ✅ **ĐÚNG.** Veto là "lớp lọc thêm", đúng nguyên văn Hiến chương II. Và nó **an toàn một chiều**: LLM hỏng ⇒ không veto ⇒ vẫn chạy được deterministic |
| **(iv) "Nhưng liệu veto có thêm giá trị đo được không?"** | ❌ **KHÔNG. Và đây là vế quyết định.** |

**Chứng minh vế (iv) — bằng số học, không bằng ý kiến:**

Giả sử LLM veto giỏi một cách hào phóng: nó cắt 30% số lệnh và nâng E[R] từ +0,047 lên +0,097 R
(tức **gấp đôi edge** — một giả định rất rộng lượng). Để phân biệt "veto thêm +0,05 R" với "veto
không làm gì", cần so sánh hai nhánh, mỗi nhánh:

```
n ≥ 6,183 × (σ_R / ΔE_R)² = 6,183 × (1,4 / 0,05)² ≈ 4.848 lệnh mỗi nhánh
```

Ở **100 lệnh/năm** ⇒ **~48 năm cho mỗi nhánh**. Và đó là với giả định veto giỏi gấp đôi thực tế.

> **⇒ Giá trị của LLM-veto không phải "chưa đo được". Nó là KHÔNG THỂ ĐO ĐƯỢC ở quy mô này,
> vĩnh viễn, bất kể kiên nhẫn tới đâu.**

Theo đúng chuẩn mà tài liệu 01 tự đặt ra (§4.2: *"Edge dưới +0,10 R là KHÔNG THỂ ĐO ĐƯỢC"*, §0
kết luận 5), một thành phần có giá trị không đo được **phải bị loại**, không phải giữ lại vì nó
"có vẻ hợp lý". Giữ nó lại chính xác là cái mà brief gọi là *"hợp lý hoá việc giữ lại một thành
phần đắt tiền"*.

**Vì vậy tôi đồng ý với 3 vế đầu và bác vế thứ tư — và vế thứ tư là vế quyết định.**

### 4.3 Kiến trúc đề xuất — giải quyết mâu thuẫn hiến chương

Mâu thuẫn: Hiến chương II nói AI không bao giờ được là lớp duy nhất quyết định; trong autotrade,
AI đang là người quyết định vào lệnh.

**Cách giải quyết không phải là hạ AI xuống veto. Là bỏ AI khỏi vòng lặp và để nguyên tắc II đúng
một cách tầm thường.**

```
┌─────────────────────────────────────────────────────────────────┐
│  VÒNG LẶP GIAO DỊCH — 100% TẤT ĐỊNH, KHÔNG CÓ LLM               │
│                                                                  │
│  Nến ngày đóng (00:00 UTC)                                       │
│      ↓                                                           │
│  IHistoricalKlineProvider → fapi (PERP)                          │
│      ↓                                                           │
│  MarketAnalyzer  (EMA/ATR/Donchian — thuần số học)               │
│      ↓                                                           │
│  SignalGenerator (setup, SL, size — thuần số học)  ← HỒI SINH   │
│      ↓                                                           │
│  RegimeGate      (EMA200 + cờ NoTradeWindow — thuần số học)      │
│      ↓                                                           │
│  RuleEngine + 14 lớp chặn LiveOrderService (đã có)               │
│      ↓                                                           │
│  Binance USDT-M                                                  │
└─────────────────────────────────────────────────────────────────┘
              ↑                                    ↓
   ┌──────────────────────┐          ┌──────────────────────────┐
   │ LLM #1: đọc lịch     │          │ LLM #2: viết nhật ký     │
   │ sự kiện vĩ mô →      │          │ tuần cho CON NGƯỜI đọc   │
   │ NoTradeWindow(from,  │          │ (không chạm quyết định)  │
   │ to, reason)          │          │                          │
   │ ~2 lượt/ngày         │          │ 1 lượt/tuần              │
   │ TẤT ĐỊNH HOÁ đầu ra  │          │ ~$5/năm                  │
   └──────────────────────┘          └──────────────────────────┘
```

**Vì sao kiến trúc này tuân thủ hiến chương chặt hơn cả thiết kế hiện tại:**

| Điều khoản | Cách tuân thủ |
|---|---|
| **II — Deterministic trước, AI sau** | AI **không nằm trên đường quyết định**. Không cần nhánh dự phòng vì không có gì để dự phòng |
| **II — ngoại lệ "đặt lệnh thật YÊU CẦU AI trả lời thật"** | ⚠️ **Điều khoản này phải sửa.** Nó được viết khi AI là người sinh tín hiệu. Trong kiến trúc mới nó trở thành **lớp chặn vô nghĩa** — chặn giao dịch vì một dịch vụ không liên quan đang lỗi. Xem §4.4 |
| **I — Kỷ luật hơn dự đoán** | Giảm từ 288 → 1 quyết định/symbol/ngày là **giảm** tần suất. Củng cố nguyên tắc |
| **III — An toàn mặc định** | 14 lớp chặn (`LiveOrderService.cs:85–245`) giữ nguyên vẹn, không bớt lớp nào |
| **IV — Ghi vết toàn bộ** | Vẫn ghi `AiSignalScanRecord` cho **mọi** quyết định, kể cả không vào lệnh. Nhưng giờ ghi được **input tất định** ⇒ tái dựng 100% |
| **V — Phân tầng** | `IHistoricalKlineProvider` là port trong Application, adapter trong Infrastructure |

### 4.4 Sửa đổi hiến chương cần thiết

**Nguyên tắc II, gạch đầu dòng 4 — hiện tại:**

> *"Ngoại lệ có chủ đích: đặt lệnh THẬT tự động YÊU CẦU AI trả lời thật (xem Nguyên tắc III).
> Đây là ràng buộc theo hướng an toàn — thiếu AI thì **không đặt lệnh**."*

**Vấn đề:** điều khoản này giả định AI **là** người ra quyết định, nên "AI im lặng ⇒ dừng" là an
toàn. Khi AI không còn trên đường quyết định, nó biến thành một **phụ thuộc ngoài giả tạo**:
MiniMax hết quota lúc 3 giờ sáng ⇒ chiến lược tất định bỏ lỡ setup vì một lý do không liên quan
gì tới rủi ro. Đây là lớp chặn **làm giảm** an toàn (tăng phương sai vận hành) chứ không tăng.

**Sửa đề xuất (MINOR — làm rõ phạm vi, không bớt lớp chặn):**

> - Ràng buộc "phải có AI mới được đặt lệnh thật" **chỉ áp dụng khi AI nằm trên đường quyết định
>   vào lệnh**. Nếu tín hiệu được sinh hoàn toàn tất định, ràng buộc bắt buộc tương ứng là:
>   **phải có `BacktestRun` đạt cổng GO** và **phải hoàn thành F0** (§7.2 tài liệu 01) — tức thay
>   một lớp chặn yếu (dịch vụ ngoài còn sống) bằng một lớp chặn mạnh (bằng chứng đã kiểm định).
> - Mọi đầu ra LLM chảy vào hệ thống PHẢI được **tất định hoá** thành kiểu dữ liệu có cấu trúc
>   trước khi bất kỳ logic nào đọc nó, và PHẢI có giá trị mặc định an toàn khi LLM không phản hồi.

*(Neo mã cần sửa cùng lúc: `LiveOrderService.cs:92` — lớp chặn "AI chưa cấu hình".)*

---

## 5. Hai chiến lược cụ thể, đủ chi tiết để code

> **Đọc phần này với đúng kỳ vọng.** Đây **không** phải hai chiến lược đã chứng minh sinh lợi.
> §2.9 đã nói rõ: chiến lược A đo được **+0,047 R** vượt null, nằm ở **VÙNG XÁM** của cổng §4.3.
> Chúng được đề xuất vì chúng là **cấu hình ít tệ nhất** — tất định, backtest được, chi phí thấp
> nhất, ít tham số nhất, và hợp quỹ thời gian 15–20 h/tuần. Kỳ vọng đúng đắn là **quanh hoà vốn**.

### 5.1 Chiến lược A — "Donchian ngày, thuận xu hướng lớn, trail" (chiến lược chính)

**Cơ chế kinh tế — ai ở phía bên kia và vì sao họ chấp nhận thua:**

Phía bên kia của một breakout ngày là ba nhóm, và cả ba đều có lý do phi-lợi-nhuận để giao dịch:

1. **Người bán chốt lời / cắt lỗ theo mức tâm lý.** Họ bán ở đỉnh 20 ngày vì đó là mức họ đã đặt
   lệnh từ trước, không vì họ nghĩ giá sẽ giảm. Họ đang bán **thanh khoản**, không phải **quan điểm**.
2. **Người bán bị buộc phải bán** — margin call, thanh lý. Họ không chọn giá; sàn chọn hộ. Đây là
   nguồn edge duy nhất trong crypto có bằng chứng cơ chế rõ ràng.
3. **Người viết quyền chọn / market maker phòng hộ delta.** Khi giá phá vùng, họ phải mua/bán theo
   để giữ delta ⇒ khuếch đại move. Họ chấp nhận lỗ ở chân này vì đã thu premium ở chân kia.

Nhóm (1) và (2) **không tối ưu hoá lợi nhuận trên giao dịch đó**. Đó là điều kiện cần để một edge
tồn tại lâu dài. Nhưng lưu ý trung thực: cơ chế này **giải thích được** một edge dương nhỏ — nó
**không** biện minh cho một edge lớn, và §2.7 đo được rằng edge đúng là nhỏ.

**Đặc tả đầy đủ:**

| Thành phần | Giá trị | Vì sao |
|---|---|---|
| **Khung thời gian** | **1d**, quyết định lúc nến ngày đóng (00:00 UTC) | Chi phí/R thấp nhất (§2.3). Kang & Ryu 2026: tín hiệu chậm > nhanh |
| **Điểm quyết định** | 1 lượt/ngày, ngay sau 00:00 UTC | Cắt **99,65%** lượt gọi so với `*/5` hiện tại |
| **Vũ trụ** | 10 symbol cố định (§6) | |
| **Điều kiện vào — LONG** | `Close[i] > max(High[i−20..i−1])` **VÀ** `Close[i] > EMA200[i]` | Donchian-20 là tham số kinh điển, không tối ưu. EMA200 là bộ lọc **duy nhất** đo được có giá trị (§2.8) |
| **Điều kiện vào — SHORT** | `Close[i] < min(Low[i−20..i−1])` **VÀ** `Close[i] < EMA200[i]` | |
| **Giá vào** | `Open[i+1]`, lệnh MARKET | Chống lookahead (§3.1 tài liệu 01) |
| **SL ban đầu** | `entry ∓ 2,0 × ATR(14, 1d)` | Chi phí 0,0067 R (BTC). Trail 2 ATR là biến thể duy nhất vượt null (§2.7) |
| **Exit** | **Chandelier trailing 2,0 × ATR(14,1d)** từ đỉnh/đáy chạy kể từ khi vào lệnh. **KHÔNG có TP cố định.** | **Đây là thay đổi quan trọng nhất so với `SignalGenerator.cs:12`.** TP = 2R cắt cụt đuôi phải — nguồn edge duy nhất (§1.3) |
| **Timeout** | 120 nến ngày | |
| **Size** | R cố định = `MaxRiskPerTradePercent` (mặc định 1%, `RiskSetting.cs:17`) | |
| **Tối đa vị thế đồng thời** | 4 | Ở 10 symbol tương quan cao, 4 vị thế ≈ 2 cược độc lập |
| **Chống trùng** | Không mở lệnh mới cùng symbol khi đang có vị thế; **cooldown 5 nến ngày** sau khi bị quét SL trên symbol đó | Chống whipsaw re-entry (§8.3) |
| **Không giao dịch khi** | `NoTradeWindow` đang bật; hoặc đã chạm `MaxDailyLossPercent`; hoặc **circuit breaker §8.2** đang mở | |

**Đếm tham số tự do: 5** (Donchian 20, EMA 200, ATR 14, trail 2,0, timeout 120).
So với **≈10** của cấu hình hiện tại (§3.3 tài liệu 01). **Cắt một nửa bậc tự do.**

**Tần suất kỳ vọng:** 7,4–9,9 lệnh/symbol/năm [đo 2026-07-31] × 10 symbol ≈ **75–100 lệnh/năm**.

**Cách nó thua — nói trước, không nói sau:**
- **Chop biến động thấp** (chính là 07/2026, §7): breakout giả liên tục, chuỗi lỗ nhỏ kéo dài.
- **Đảo chiều V**: trail 2 ATR bị quét trước khi trend tiếp diễn.
- **Gap cuối tuần** trên alt: thoát tại `Open` xa SL (§5.4 tài liệu 01 bắt buộc mô phỏng đúng điều này).

### 5.2 Chiến lược B — "Long khi phe short kiệt quệ" (overlay, quy mô nhỏ, tuỳ chọn)

> **Cảnh báo gắn liền:** §2.6 cho thấy chiến lược này **trượt mọi cổng kiểm chứng**.
> Nó có mặt ở đây vì brief yêu cầu 1–2 chiến lược và vì cơ chế của nó **khác hẳn** chiến lược A
> (bổ sung đa dạng hoá thật). **Nếu chỉ chọn một, chọn A.**

**Cơ chế kinh tế:** funding âm sâu nghĩa là phe SHORT đang trả tiền để giữ vị thế. Điều đó chỉ xảy
ra khi short đông đến mức perp giao dịch dưới spot. Người ở phía bên kia là **short bị nhốt**: họ
trả phí mỗi 8 giờ và bị siết dần. Khi họ buộc phải đóng, họ **mua** — và họ mua bất kể giá.

| Thành phần | Giá trị |
|---|---|
| **Kích hoạt** | `fundingRate ≤ −0,075%/8h` (≈ −82%/năm) tại mốc funding |
| **Hướng** | **LONG duy nhất.** Không short. (79/79 quan sát là long — không có mẫu để biện minh chiều ngược) |
| **Vũ trụ** | Cùng 10 symbol của chiến lược A |
| **Giá vào** | `Open` của nến 1h kế tiếp mốc funding |
| **SL** | `entry − 2,5 × ATR(14, 1h)` |
| **TP** | `entry + 5,0 × ATR(14, 1h)` (RR = 2) |
| **Timeout** | 48 nến 1h |
| **Size** | **0,5 R** (nửa rủi ro chuẩn) — vì bằng chứng yếu |
| **Trần** | Tối đa 1 vị thế B đồng thời; tối đa 2/tháng |
| **Tham số tự do** | **4** (ngưỡng funding, SL 2,5, RR 2, timeout 48) |

**Tần suất kỳ vọng: ~5 lệnh/năm** (79 lệnh / 16 tháng / 20 symbol × 10 symbol ≈ 30/năm ở vũ trụ 20;
với gate và trần 2/tháng thực tế thấp hơn nhiều).

**Điều kiện phải thoả mãn trước khi chiến lược B được cấp vốn thật:**
1. Backtest trên archive `data.binance.vision` **bao gồm symbol đã huỷ niêm yết**, giai đoạn
   2020–2024 (ngoài mẫu 16 tháng của tôi).
2. Bootstrap theo cụm-ngày phải cho CI 95% **không chạm 0** — hiện tại chạm.
3. Phải có ≥ 1 tháng ngoài Oct–Nov 2025 đóng góp dương đáng kể — hiện tại không có.

**Nếu bất kỳ điều nào trượt: bỏ chiến lược B.** Ghi rõ ở đây để không tự thương lượng lại sau.

### 5.3 Cái gì phải sửa trong mã trước khi cả hai chạy được

| # | Việc | Neo | Ghi chú |
|---|---|---|---|
| 1 | Klines từ **PERP** `fapi`, không phải spot | `BinanceOptions.cs:8`, `BinanceMarketDataProvider.cs:44–45` | §3.7 tài liệu 01 |
| 2 | **Chỉ quyết định trên nến đã đóng** | `MarketAnalyzer.cs:23–24`, `MarketScanService.cs:167–178` | Xoá 19% tín hiệu ma |
| 3 | **Bỏ TP cố định, thêm trailing exit** | `SignalGenerator.cs:12,24` | §1.3 — thay đổi thiết kế lớn nhất |
| 4 | Thêm `Interval = "1d"` vào watchlist | `SeedData.cs:61–64` | |
| 5 | Thêm bộ lọc EMA200 (cần ≥ 200 nến, `CandleLimit` đã = 200 ⇒ **phải nâng lên ≥ 250**) | `MarketScanService.cs:22` | Bộ lọc duy nhất đo được có giá trị |
| 6 | **Gọi `ISignalGenerator`** từ luồng production | `DependencyInjection.cs:72` (đã đăng ký, **0 call site**) | Xác minh độc lập: `grep` toàn `src/` chỉ ra **đúng 1 dòng** — dòng đăng ký DI |
| 7 | Sửa 2 bug testnet | `TradeResultSyncService.cs:68,134`, `BinanceAccountProviderFactory.cs:19` | §6.1 tài liệu 01 — chặn F0 |
| 8 | Trừ funding khỏi PnL | `TradeResultSyncService.cs:221` (`grep funding src/` = 0) | Chiến lược A giữ lệnh nhiều ngày ⇒ funding **có** đáng kể (khác kết luận khung 1h) |
| 9 | Cron `market-scan` `*/5` → `5 0 * * *` | `Program.cs:129` | 1 lượt/ngày |
| 10 | Cron `trade-advisor` `*/1` → bỏ hoặc `0 1 * * *` | `Program.cs:142` | 1.440 → 1 lượt/ngày |

**Lưu ý quan trọng về mục 8:** tài liệu 01 §3.5 kết luận *"funding KHÔNG phải chi phí lớn ở khung 1h"*.
Kết luận đó **đúng cho khung 1h và sai cho chiến lược A**. Chiến lược A có trung vị 6–12 ngày giữ lệnh
⇒ 18–36 kỳ funding. Ở mức trung bình đo được 0,0014785%/8h ⇒ 0,027–0,053% notional. Với SL = 2 ATR(1d)
≈ 7,4% giá ⇒ **0,004–0,007 R** — vẫn nhỏ. Nhưng ở pha funding cao 0,01%/8h ⇒ 0,18–0,36% ⇒
**0,024–0,049 R**, tức **bằng cỡ toàn bộ chi phí phí giao dịch**. Phải mô hình hoá, không được bỏ qua.

---

## 6. Danh sách symbol

### 6.1 Thanh khoản thật — đo trực tiếp

| Hạng | Symbol | Khối lượng quote 24h (USDT) | % tổng | Luỹ kế % |
|---|---|---|---|---|
| 1 | BTCUSDT | 7.695.436.382 | 33,83 | 33,83 |
| 2 | ETHUSDT | 5.911.297.189 | 25,99 | **59,82** |
| 3 | BANKUSDT | 1.095.636.515 | 4,82 | 64,63 |
| 4 | SOLUSDT | 934.161.151 | 4,11 | 68,74 |
| 5 | COTIUSDT | 551.139.632 | 2,42 | 71,16 |
| 6 | HYPEUSDT | 372.926.572 | 1,64 | 72,80 |
| 7 | BNBUSDT | 332.923.845 | 1,46 | 74,27 |
| 8 | XRPUSDT | 320.383.621 | 1,41 | 75,67 |
| 9 | ZECUSDT | 267.085.198 | 1,17 | 76,85 |
| 10 | KOMAUSDT | 237.805.620 | 1,05 | 77,89 |
| 11 | DOGEUSDT | 196.872.773 | 0,87 | 78,76 |
| … | | | | |
| 21 | (ngưỡng $100M) | 107.971.884 | | 84,60 |
| 37 | (ngưỡng $50M) | | | ~91 |
| 66 | (ngưỡng $20M) | | | ~94 |
| 529 | ALLUSDT | 376.591 | 0,00 | 100,00 |

*(529 perp USDT-M `TRADING`, tổng khối lượng 24h = **22,75 tỷ USDT**, [đo 2026-07-31])*

| Ngưỡng khối lượng 24h | Số symbol |
|---|---|
| ≥ $1 tỷ | 3 |
| ≥ $500M | 5 |
| ≥ $200M | 10 |
| ≥ $100M | 21 |
| ≥ $50M | 37 |
| ≥ $20M | 66 |
| ≥ $10M | 99 |

**Chỉ 210/529 (39,7%) symbol có ≥ 2 năm lịch sử** [đo 2026-07-31] — khớp chính xác §3.2 tài liệu 01.

### 6.2 Cái bẫy trong bảng trên — và nó liên quan trực tiếp tới phát hiện #8

**Nhìn kỹ top 20: BANKUSDT (#3), COTIUSDT (#5), HYPEUSDT (#6), KOMAUSDT (#10), AKEUSDT (#12),
UAIUSDT (#13), ONUSDT (#15), ESPUSDT (#17), MMTUSDT (#18), KAITOUSDT (#19).**

**10 trong 20 symbol có khối lượng cao nhất hôm nay là coin mới niêm yết.** BANKUSDT giao dịch
$1,1 tỷ/ngày — nhiều hơn SOL — và gần như chắc chắn không có 2 năm lịch sử.

⇒ **Chọn vũ trụ theo "khối lượng hôm nay" là survivorship bias ngược: bạn nạp đầy vũ trụ bằng
những thứ chưa có quá khứ để backtest, và trong 258 symbol đã biến mất (§3.2 tài liệu 01) phần lớn
từng có đúng hồ sơ này** — khối lượng bùng nổ lúc niêm yết rồi chết.

### 6.3 Đề xuất

| Câu hỏi | Trả lời | Vì sao |
|---|---|---|
| **Bao nhiêu symbol?** | **10** | Đủ để không phụ thuộc 1 symbol (cổng §4.3 #9: không symbol nào > 40% tổng R); đủ ít để 4 vị thế đồng thời có nghĩa; thấp hơn nhiều điểm vỡ 30–75 symbol/instance (file 04) ⇒ **không phải ràng buộc** |
| **Chỉ BTC/ETH?** | **Không** | 2 symbol × 8 lệnh/năm = **16 lệnh/năm**. Ở tần suất đó không bao giờ đo được gì. Và ETH tương quan ~0,8 với BTC ⇒ thực chất là 1 cược |
| **Mở rộng alt?** | **Có, nhưng chỉ alt lâu đời** | Alt cho ATR% cao hơn (SOL 1d = 7,31% vs BTC 3,72%) ⇒ **chi phí/R thấp hơn 2 lần** (§2.3). Đây là lý do định lượng, không phải "alt biến động vui hơn" |
| **Tiêu chí chọn** | (a) `onboardDate` ≥ **3 năm**; (b) khối lượng 24h ≥ **$50M**; (c) có trong archive `data.binance.vision` từ ≥ 2021-01 | (a) chống nạp coin không có quá khứ; (b) đảm bảo slippage ở notional 50 USDT không đáng kể; (c) đảm bảo backtest được |
| **Danh sách khởi điểm** | BTCUSDT, ETHUSDT, SOLUSDT, BNBUSDT, XRPUSDT, DOGEUSDT, ADAUSDT, LINKUSDT, AVAXUSDT, LTCUSDT | Toàn bộ đã dùng trong phép đo §2.7, đều có nến ngày từ **2020-10** hoặc sớm hơn |
| **Rà lại khi nào?** | **Mỗi quý, bằng quy tắc viết sẵn**, không bằng phán đoán | Đổi vũ trụ sau khi nhìn kết quả = bậc tự do ẩn (§3.3 quy tắc 3 tài liệu 01) |
| **Chi phí LLM khi mở rộng?** | **Không còn là ràng buộc** | Ở kiến trúc §4.3, LLM không chạy theo symbol. 10 hay 30 symbol đều ~$5–10/năm |

**Ghi chú bắt buộc về survivorship khi backtest (§3.2 tài liệu 01):** danh sách 10 symbol trên
**là những kẻ sống sót**. Backtest trên đúng 10 symbol này sẽ **lạc quan**. Cách chặn duy nhất: khi
chạy backtest lịch sử, dựng vũ trụ **tại thời điểm t** từ archive (gồm cả symbol đã chết), áp bộ lọc
thanh khoản **bằng dữ liệu trước t**, và báo cáo rõ bao nhiêu % nến bị mất.

---

## 7. Chế độ thị trường — có nên chạy ngay bây giờ không?

### 7.1 Đo trạng thái hiện tại

| Chỉ số | Giá trị | Nguồn |
|---|---|---|
| BTC đóng cửa ngày | **$64.750,00** (2026-07-30) | [`fapi/v1/klines`, đo 2026-07-31] |
| Đỉnh trong cửa sổ 1.500 ngày | $126.208,50 (2026-10-06 → **2025-10-06**) | như trên |
| **Sụt từ đỉnh** | **−48,7%** | như trên |
| BTC EMA50 (ngày) | $64.913 | như trên |
| BTC EMA200 (ngày) | $73.137 | như trên |
| **Cấu trúc xu hướng** | `Close < EMA50 < EMA200` ⇒ **DOWNTREND** | như trên |
| Biến động thực hiện 30 ngày (quy năm) | **29,7%** | như trên |
| Biến động thực hiện 1 năm (quy năm) | 43,4% | như trên |
| **Phân vị biến động 30 ngày** | **12** (so với 1.438 ngày) | như trên |
| Funding hiện tại (quy năm) | BTC **10,95%**, ETH 4,79%, SOL 10,24%, BNB 6,74%, XRP 10,95%, DOGE 5,54% | [`fapi/v1/premiumIndex`, đo 2026-07-31] |
| Fear & Greed | **14 — Extreme Fear** | [CoinStats AI, 01/07/2026, qua `docs/strategy/01-market-landscape.md`] |
| Dòng ETF 30 ngày | −$6,96 tỷ | [CoinStats AI, 01/07/2026, như trên] |

*(Lưu ý: tôi đo −48,7% từ đỉnh trong cửa sổ 1.500 nến ngày PERP; `01-market-landscape.md` ghi
−45,5% [CoinStats AI, 01/07/2026]. Chênh lệch do khác ngày tham chiếu và khác nguồn giá. Cả hai
cùng nói một điều.)*

### 7.2 Chiến lược A chết trong điều kiện nào — và điều kiện đó có đang xảy ra không?

| Điều kiện giết chiến lược A | Đang xảy ra? |
|---|---|
| **Biến động thấp + đi ngang** ⇒ breakout giả liên tục | ⚠️ **CÓ.** Biến động 30 ngày ở **phân vị 12** — thấp hơn 88% lịch sử |
| **Không có xu hướng bền** ⇒ trail bị quét | ⚠️ **CÓ.** Close ($64.750) và EMA50 ($64.913) cách nhau **0,25%** — nén sát nhau |
| Chi phí tăng (spread giãn, thanh khoản cạn) | ⚠️ Một phần. Khối lượng sàn giảm 32–48% [`01-market-landscape.md`] |
| Funding cao kéo dài ăn vị thế giữ lâu | ❌ Không. 4,8–11,0%/năm là mức bình thường |

**Chiến lược B có tín hiệu không?** Ngưỡng cần: funding ≤ **−0,075%/8h** (−82%/năm).
Thực tế: **tất cả 6 symbol đang DƯƠNG**, từ +4,79% đến +10,95%/năm.
⇒ **Không những không có tín hiệu, mà đang ở phía ngược lại của thang đo.**

### 7.3 Trả lời thẳng

> **Không nên chạy tiền thật bây giờ. Nên xây và đo bây giờ.**

Lý do, và không lý do nào là "vì thị trường đang giảm":

1. **Cả hai chiến lược đều không có setup đang bật.** A ở chế độ tệ nhất của nó (biến động phân vị
   12, chưa có xu hướng). B ở phía sai của ngưỡng kích hoạt. Chạy lúc này = trả phí để lấy mẫu nhiễu.
2. **Điều kiện tiên quyết chưa xong.** Forward-test testnet **hỏng hoàn toàn**
   (`TradeResultSyncService.cs:134` đọc fills mainnet trong khi đặt lệnh testnet, §6.1 tài liệu 01)
   ⇒ hiện tại **không tạo ra được một mẫu dữ liệu nào**. Đây là chặn cứng, không phải chặn mềm.
3. **Thời điểm này rẻ về mặt chi phí cơ hội.** Biến động thấp = ít move bị bỏ lỡ. 6 tuần xây engine
   trong pha này tốn ít cơ hội hơn 6 tuần trong pha trending.
4. **Nhưng đừng chờ "thị trường tốt hơn" rồi mới bật.** Bật theo **quy tắc viết sẵn**, không theo
   cảm nhận. Quy tắc đề xuất: chỉ nhận setup mới khi
   `biến_động_thực_hiện_30_ngày > phân_vị_25` của 2 năm gần nhất — một tham số, đo được, tất định.

**Một cảnh báo về chính quy tắc #4:** đây là bộ lọc thêm **sau khi nhìn dữ liệu**, đúng thứ §3.3
quy tắc 3 tài liệu 01 cấm. Nó chỉ được phép vì nó dựa trên cơ chế đã biết trước (trend-following
cần biến động), **không** dựa trên việc nó cải thiện kết quả backtest. **Phải khai báo nó là tham số
thứ 6 và tính vào ngân sách bậc tự do.** Nếu backtest cho thấy nó cải thiện kết quả, đó **không**
phải bằng chứng — đó là điều cần nghi ngờ.

---

## 8. Lớp kỷ luật MMW — giữ gì, bỏ gì, đổi nghĩa gì

Đây là tài sản thật của MMW và câu hỏi hay nhất trong brief: *behavior detector còn nghĩa gì khi
không còn con người bấm nút?*

### 8.1 Nguyên tắc phân loại

Mọi lớp kỷ luật hiện có phục vụ **một trong hai** mục đích, và autotrade ảnh hưởng khác nhau:

- **Lớp chặn CƠ HỌC** (SL bắt buộc, cap notional, cap đòn bẩy, chống trùng, giới hạn lệnh/ngày):
  bảo vệ chống **lỗi phần mềm và lỗi vận hành**. **Autotrade làm chúng QUAN TRỌNG HƠN**, vì không
  còn con người nhìn màn hình để bắt lỗi.
- **Lớp phát hiện HÀNH VI** (revenge, tilt, oversize): bảo vệ chống **cảm xúc con người**.
  Bot không có cảm xúc ⇒ nghĩa gốc mất. **Nhưng cấu trúc thì tái dụng được.**

### 8.2 Bảng phán quyết

| Thành phần | Neo | Phán quyết | Nghĩa mới trong autotrade |
|---|---|---|---|
| **14 lớp chặn `LiveOrderService`** | `LiveOrderService.cs:85,92,106,117,128,144,154,171,190,195,214,222,233,245` | ✅ **GIỮ NGUYÊN VẸN** | Quan trọng hơn trước. Hiến chương III: bớt lớp chặn phải qua sửa đổi MAJOR. **Ngoại lệ duy nhất: dòng 92 ("AI chưa cấu hình") phải sửa theo §4.4** |
| `MinRiskRewardRatio = 1.5` | `RiskSetting.cs:21` | ⚠️ **ĐỔI NGHĨA — cấp bách** | Chiến lược A **không có TP** ⇒ RR không xác định tại lúc vào lệnh ⇒ lớp chặn này sẽ **chặn mọi lệnh**. Phải đổi thành: RR tính theo **TP mục tiêu danh nghĩa** (ví dụ 3R) chỉ để chấm rule, hoặc miễn trừ khi `ExitMode = Trailing` |
| `RequireStopLoss = true` | `RiskSetting.cs:31` | ✅ **GIỮ** | Bất biến |
| `MaxRiskPerTradePercent = 1%` | `RiskSetting.cs:17` | ✅ **GIỮ** | |
| `MaxTradesPerDay = 5` | `RiskSetting.cs:24` | ⚠️ **ĐỔI NGHĨA** | Ở khung ngày, 5 lệnh/ngày **không bao giờ chạm tới** (thực tế ~0,3 lệnh/ngày). Nó trở thành **lớp chặn chống bug**: nếu chạm 5, đó là dấu hiệu job chạy chồng, không phải giao dịch nhiều |
| `MaxDailyLossPercent = 3%` | `RiskSetting.cs:28` | ✅ **GIỮ, nâng vai trò** | Trở thành circuit breaker chính trong ngày |
| **`LossStreakDetector`** | `LossStreakDetector.cs`, ngưỡng `RiskSetting.cs:38` | 🔄 **ĐỔI NGHĨA — giá trị cao nhất** | Xem §8.3 |
| **`RevengeTradeDetector`** | `RevengeTradeDetector.cs:16–30` | 🔄 **ĐỔI NGHĨA** | Xem §8.4 |
| **`OversizedAfterLossDetector`** | `OversizedAfterLossDetector.cs`, ngưỡng `RiskSetting.cs:42` | 🔄 **ĐỔI NGHĨA → bộ bắt bug size** | Bot không tự tăng size vì cay cú. Nhưng size **có thể** nhảy vì bug: `CurrentBalance` đọc sai (bug testnet §6.1), `stopDistance` ≈ 0 khi ATR lỗi ⇒ `quantity` khổng lồ (`TradeService.cs:100–104`). **Detector này trở thành lớp bắt lỗi tính toán — giữ lại và giữ nguyên ngưỡng 50%** |
| `TradingDay` boundary UTC | `TradingDayService.cs:36` | ✅ **GIỮ** | Ở khung ngày UTC, ranh giới ngày giao dịch **trùng** ranh giới nến. Vấn đề "reset lúc 07:00 giờ VN" (§7.4 tài liệu 01) **tự biến mất** |
| Thông báo SignalR / email | | ⚠️ **GIẢM** | Ở 100 lệnh/năm, thông báo mỗi lệnh là hợp lý. Thông báo mỗi lượt quét thì không |
| `TradeAdvisorService` (LLM tư vấn lệnh mở) | `Program.cs:139–142` | ❌ **BỎ hoặc hạ xuống 1 lượt/ngày** | 1.440 lượt/ngày × số lệnh mở, cho một chiến lược quyết định 1 lần/ngày |

### 8.3 "Revenge trade" của một con bot nghĩa là gì?

Brief hỏi thẳng câu này. Trả lời: **có, nó là một dạng circuit breaker — nhưng KHÔNG phải theo
nghĩa thời gian, mà theo nghĩa thống kê.**

`RevengeTradeDetector.cs:25–30` hiện đo *khoảng cách thời gian giữa lệnh thua vừa đóng và lệnh mới*.
Với con người, khoảng cách ngắn = chưa nguội đầu. Với bot, khoảng cách ngắn **không mang thông tin gì**
— bot vào lệnh khi tín hiệu bật, không khi nào khác.

**Nhưng có một hiện tượng thật, tương tự về mặt cấu trúc, và nó đáng chặn: whipsaw re-entry.**
Chiến lược A bị quét SL trên BTCUSDT, rồi hôm sau BTCUSDT lại phá đỉnh 20 ngày ⇒ bot vào lại đúng
symbol đó, đúng hướng đó, ở giá xấu hơn. Lặp 3 lần là mất 3 R cho cùng một nhiễu. Đây là "revenge
trade" phiên bản máy: **không phải cảm xúc, mà là tín hiệu tương quan cao với chính lệnh vừa thua**.

**Đổi nghĩa đề xuất:**

> `RevengeTradeDetector` → **`ReentryCooldownDetector`**: chặn lệnh mới **cùng symbol, cùng hướng**
> trong vòng **N nến của khung giao dịch** (không phải N phút) sau khi bị quét SL trên symbol đó.
> Ngưỡng: `ReentryCooldownBars`, mặc định **5**. Giữ nguyên `IBehaviorDetector`, chỉ đổi phép đo
> từ `TimeSpan` sang số nến — tức là **thêm một lớp cài đặt hợp đồng có sẵn**, đúng Hiến chương V.

### 8.4 `LossStreakDetector` → giám sát sức khoẻ chiến lược (SPC)

Đây là thành phần **giá trị nhất** sau khi đổi nghĩa, vì nó trả lời câu hỏi §7 của brief:
*làm sao phát hiện sớm chiến lược đã chết và tự động dừng?*

Ngưỡng hiện tại `LossStreakThreshold = 3` (`RiskSetting.cs:38`) là hằng số tuỳ ý. Với chiến lược A
có win-rate ~40%, xác suất thua 3 lệnh liên tiếp là `0,6³ = 21,6%` — **xảy ra suốt**, và báo động
lúc đó là báo động giả.

**Ngưỡng đúng phải suy ra từ chính phân bố R của chiến lược:**

| Chuỗi thua liên tiếp | Xác suất (win-rate 40%) | Kỳ vọng xảy ra trong 100 lệnh/năm |
|---|---|---|
| 3 | 21,6% | thường xuyên — **không phải tín hiệu** |
| 5 | 7,8% | vài lần/năm — không phải tín hiệu |
| 8 | 1,7% | ~1 lần/năm — đáng chú ý |
| **10** | **0,60%** | ~1 lần/3 năm — **đáng dừng** |
| 12 | 0,22% | hiếm — chắc chắn có gì đó sai |

**Đổi nghĩa đề xuất — hai tầng:**

> **Tầng 1 — `LossStreakDetector` (cảnh báo):** ngưỡng nâng từ 3 lên **8**, và ngưỡng phải được
> **tính lại từ win-rate quan sát được**, không hardcode. Hiến chương I: *"Ngưỡng kỷ luật PHẢI đọc
> từ cấu hình theo tài khoản. Hardcode ngưỡng trong logic phát hiện là vi phạm hiến chương."*
>
> **Tầng 2 — `StrategyHealthMonitor` (circuit breaker, thành phần MỚI):** theo dõi **drawdown tính
> bằng R** so với ngưỡng đã định trước từ backtest. Cổng §4.3 tiêu chí #7 đặt max drawdown chấp nhận
> được ở **≤ 12 R**. Quy tắc: nếu drawdown thực vượt **1,5 × max drawdown của backtest**, **tự động
> tắt `LiveTrading.Enabled`** và gửi cảnh báo. Không tự bật lại. Con người phải xem xét và bật tay.

Đây là câu trả lời cho *"làm sao phát hiện sớm và tự động dừng lại"*: **không phải bằng cách đoán
chế độ thị trường, mà bằng cách theo dõi chiến lược có đang cư xử như phân bố mà backtest đã đo
hay không.** Ưu điểm quyết định: nó bắt **mọi** nguyên nhân chết — chế độ đổi, bug, sàn đổi luật,
edge biến mất — mà không cần biết nguyên nhân là gì.

**Và nó cũng là lớp phòng vệ cuối cùng cho toàn bộ vấn đề của tài liệu này:** nếu edge thật là
+0,047 R và không bao giờ đo được (§2.9), thì `StrategyHealthMonitor` là thứ giới hạn thiệt hại
trong lúc bạn không biết. Nó không cho bạn edge. Nó cho bạn **quyền dừng trước khi mất quá nhiều**.

---

## 9. Lộ trình đề xuất — sửa lại thứ tự của tài liệu 01

Tài liệu 01 §8 đề xuất 12 tuần: xây kho klines (tuần 5–6) → engine (7–8) → thống kê (9) → Đường B
(10–11) → Đường A (12). **Phân tích ở tài liệu này thay đổi hai điều trong thứ tự đó:**

1. **Đường A nên bị khai tử ngay, không đợi tuần 12.** Ở kiến trúc §4.3, LLM không còn sinh tín hiệu
   ⇒ đo hiệu quả LLM sinh tín hiệu là đo một thứ sắp bị xoá. Tiết kiệm **2 người-ngày** và, quan
   trọng hơn, tránh cám dỗ giữ LLM lại vì "số liệu Đường A trông cũng được".
2. **Việc đổi khung sang 1d và đổi exit sang trailing phải làm TRƯỚC khi xây engine**, vì nó đổi
   yêu cầu của engine (cần trailing simulator, cần funding nhiều kỳ, không cần dữ liệu 5m).

| Tuần | Việc | Người-ngày | Cổng quyết định |
|---|---|---|---|
| **1** | Bước 0 + 0b của tài liệu 01 (kiểm toán tín hiệu ma + đếm dòng DB) | 1,5 | Nếu `Accepted` < 30 dòng ⇒ **khai tử Đường A ngay**, khỏi bàn tiếp |
| **2** | Quyết định kiến trúc §4.3: bỏ LLM khỏi vòng lặp. Sửa cron `market-scan` → 1 lượt/ngày, `trade-advisor` → 1 lượt/ngày. Sửa klines sang `fapi` PERP | 1,2 | Đo hoá đơn LLM sau 7 ngày. Kỳ vọng **−99%**. Không giảm ⇒ sửa sai chỗ |
| **3–4** | Sửa 2 bug testnet + vị thế ma + `DisableConcurrentExecution` + test lớp chặn | 4,3 | Đủ điều kiện F0 |
| **5** | Đổi `SignalGenerator`: bỏ TP cố định, thêm trailing. Nâng `CandleLimit` ≥ 250, thêm EMA200. Nối `ISignalGenerator` vào production | 1,5 | `ISignalGenerator` có call site thật. Sửa `MinRiskRewardRatio` (§8.2) |
| **6–7** | Kho klines **1d** + funding (nhẹ hơn nhiều so với 1h: 20 symbol × 6 năm ≈ 44.000 nến) | 2,5 | Klines archive khớp REST ở vùng chồng lấn |
| **8–9** | Engine + `ExitSimulator` **có trailing** + `CostModel` **có funding nhiều kỳ** + Metrics | 4,0 | Test trên chuỗi giá tổng hợp có đáp án biết trước |
| **10** | `RandomDecisionSource` × 1.000 + Permutation + Bootstrap **theo cụm-ngày** | 2,0 | **Null của trailing phải ra ≈ +0,039 R, KHÔNG phải 0** (§2.7). Nếu engine cho null ≈ 0 ⇒ engine sai |
| **11** | Đường B trên vùng khoá 2020–2024 (sanity, chưa kết luận) | 1,0 | Số lệnh/năm có khớp ~7–10/symbol không? |
| **12** | **Đường B trên OOS 2025-01 → 2026-07. MỞ ĐÚNG MỘT LẦN** | 1,0 | **Cổng §4.3.** Dự đoán trung thực của tôi: **VÙNG XÁM** ⇒ theo quy tắc kết hợp = **STOP** |
| **13+** | Nếu GO: F0 testnet → F1 tiền thật tối thiểu → F2 | — | |
| | **TỔNG tới cổng chính** | **~19 người-ngày ≈ 10 tuần** | |

**Tôi ghi dự đoán của mình vào tài liệu trước khi chạy, để nó kiểm chứng được:**
dựa trên §2.7 và §2.9, tôi dự đoán Đường B sẽ cho **E[R] net trong khoảng +0,02 đến +0,08 R** và
**trượt cổng #2 (cần ≥ +0,30 R), #5 (cần p < 0,01) và #6 (cần ngoài phân vị 99)**.
Nếu kết quả thật vượt +0,15 R, **hãy nghi ngờ engine trước khi ăn mừng** — khả năng cao nhất là
đường chuẩn ngẫu nhiên chưa được trừ đúng (§2.7).

---

## 10. Những điều tôi KHÔNG trả lời được

| Câu hỏi | Vì sao |
|---|---|
| Chiến lược A có sinh lợi không? | **Chưa biết và sẽ rất lâu mới biết.** Đo được +0,047 R vượt null, cần ~5.500 lệnh (37–55 năm) để chứng minh. Đây là câu trả lời trung thực nhất tôi có |
| Nên bỏ bao nhiêu vốn? | **Không tư vấn.** Tôi phân tích khả thi kỹ thuật của một hệ thống phần mềm |
| Phí taker thật của tài khoản? | Vẫn phải đọc `GET /fapi/v1/commissionRate` (có ký). Mọi chi phí trong tài liệu này dùng **giả định 0,05%/chiều**. Nếu phí thật cao hơn 50%, chi phí/R ở §2.3 tăng 50% và một số kết luận biên có thể đảo |
| Slippage thật ở notional 20–50 USDT? | Chỉ đo được bằng lệnh thật (F1, chi phí < $25 theo §6.2 tài liệu 01) |
| Kết quả của tôi có sống ngoài mẫu 2020–2026 không? | Không biết. Mẫu khung ngày dài 5,8 năm (đa chế độ, tốt). Mẫu khung 1h chỉ **1,4 năm** và toàn bộ nằm trong pha giảm — **đây là điểm yếu lớn nhất của các phép đo khung 1h** |
| Có archetype nào tôi bỏ sót đạt +0,20 R không? | Có thể. Tôi không đo được: order-flow imbalance (cần tick data, R2), liquidation cascade theo thời gian thực (cần websocket, R1), on-chain signals (không có nguồn), tương quan chéo sàn (chỉ 1 adapter, R5). **Điểm chung: tất cả đều bị chặn bởi chính các ràng buộc kiến trúc ở §1.1** |
| LLM có thật sự vô dụng không? | Tôi **không** kết luận LLM vô dụng. Tôi kết luận **giá trị của nó không đo được ở quy mô 100 lệnh/năm**, nên theo chuẩn của chính dự án này, nó không được phép nằm trên đường quyết định. Đó là hai mệnh đề khác nhau |

---

## Phụ lục A — Toàn bộ số liệu tự đo, kèm phương pháp

| # | Số liệu | Giá trị | Phương pháp |
|---|---|---|---|
| B1 | Variance Ratio 1h, 6 symbol, 6 horizon | VR ∈ [0,806; 1,099]; **max \|z\| = 1,14** | Lo–MacKinlay bền phương sai thay đổi, 11.999 nến/symbol, 2025-03-18→2026-07-31, [đo 2026-07-31] |
| B2 | Lõi MMW khung 1h | N=4.016, gộp **+0,06 R**, net **−0,06 R**, CI95 [−0,104; −0,015], σ=1,41, win 36,1%, PF 1,09 | Mô phỏng nến-đóng, entry `Open[i+1]`, [đo 2026-07-31] |
| B3 | Đường chuẩn ngẫu nhiên 1h, SL/TP cố định | gộp **−0,00 R** (N=4.219) | Kiểm tra tự-kiểm lookahead — **đạt** |
| B4 | Lõi MMW khung 4h | N=2.042, gộp +0,09 R, net −0,03 R | như trên |
| B5 | Nghịch đảo lõi (mean-reversion) 1h | N=4.466, gộp **−0,03 R** | như trên |
| B6 | ATR(14) % giá | 1h: BTC 0,591 / ETH 0,784 / SOL 0,888 · 4h: 1,286 / 2,039 / 2,287 · **1d: 3,721 / 5,159 / 7,312** | 3.000 nến (1h,4h), 1.500 nến (1d), [đo 2026-07-31] |
| B7 | Chi phí/R (phí 0,10% khứ hồi) | từ **0,0034 R** (SOL 1d SL4ATR) tới **0,1129 R** (BTC 1h SL1,5ATR) — **chênh 33 lần** | Tính từ B6 |
| B8 | Vũ trụ USDT-M perp `TRADING` | **529**; tổng khối lượng 24h **22,75 tỷ USDT**; BTC+ETH = **59,82%** | `fapi/v1/exchangeInfo` + `fapi/v1/ticker/24hr`, [đo 2026-07-31] |
| B9 | Số symbol theo ngưỡng khối lượng | ≥$1tỷ: 3 · ≥$500M: 5 · ≥$200M: 10 · ≥$100M: 21 · ≥$50M: 37 · ≥$20M: 66 · ≥$10M: 99 | như trên |
| B10 | Symbol có ≥ 2 năm lịch sử | **210 / 529 (39,7%)** | `onboardDate`, [đo 2026-07-31] — khớp §3.2 tài liệu 01 |
| B11 | Funding → lợi suất về sau (7 rổ) | p95–100: fwd24h **−0,410%** (t=−2,32) · p0–p5: **+0,185%** · TB chung −0,028% | 8.982 mốc funding, 6 symbol, [đo 2026-07-31] |
| B12 | Funding cắt ngang, 20 symbol | gộp +0,02→+0,05 R; null +0,02 R | 1.500 mốc, 8.988–14.980 lệnh, [đo 2026-07-31] |
| B13 | Quét ngưỡng chọn lọc funding (9 mức) | tăng **đơn điệu** từ +0,045 R (N=29.960) tới +0,387 R (N=26) | [đo 2026-07-31] |
| B14 | Ngưỡng 0,075%/8h — chẩn đoán | N=79, **41 ngày**, 9 symbol, **79/79 LONG**, **95,9% lãi từ 2/16 tháng**, CI cụm-ngày **[−0,051; +0,702]**, Bonferroni cần t>2,77 có t=2,43 ⇒ **trượt** | [đo 2026-07-31] |
| B15 | Khung ngày, SL/TP cố định RR2 | Donchian+EMA200: N=1.175, net +0,060 R, t=1,69. **Null ngẫu nhiên: +0,056 R** ⇒ không phân biệt được | 20 symbol, nến ngày từ 2020-10-15, [đo 2026-07-31] |
| B16 | Khung ngày, trailing 2 ATR | Donchian+EMA200: N=1.150, gộp +0,094 R, net **+0,087 R**, t=3,00, PF 1,28 | như trên |
| B17 | **Phân bố null của trailing (300 lần chạy)** | **TB +0,0392 R**, σ 0,0211, p95 0,0727, **p99 0,0881**. Thật 0,0857 ⇒ **phân vị 98,7 = VÙNG XÁM** | 1.675 điểm vào lệnh, hướng ngẫu nhiên, [đo 2026-07-31] |
| B18 | Edge thật vượt null (biến thể tốt nhất) | **+0,047 R** | B16 − B17 |
| B19 | Phân bố R, trailing 3 ATR | p10 −0,88 · p25 −0,68 · **p50 −0,26** · p75 +0,54 · p90 +1,31 · p99 +2,98 · **max +8,29** | N=858, [đo 2026-07-31] |
| B20 | Tần suất lệnh khung ngày | **7,4–9,9 lệnh/symbol/năm** | [đo 2026-07-31] |
| B21 | Lọc EMA200 (khung 1h) | cùng chiều **+0,0656 R** (N=3.370) vs ngược chiều **+0,0052 R** (N=622) | Bộ lọc duy nhất trong 4 cái thử cho phân tách sạch |
| B22 | Lọc độ dốc / ATR (khung 1h) | Q1–Q4 **không đơn điệu** (0,084/0,006/0,092/0,043 và 0,068/0,038/0,073/0,046) | ⇒ **nhiễu, không dùng** |
| B23 | Trạng thái BTC 2026-07-30 | close $64.750 · đỉnh $126.208,50 (2025-10-06) · **DD −48,7%** · EMA50 $64.913 · EMA200 $73.137 ⇒ **downtrend** | `fapi/v1/klines` 1d, [đo 2026-07-31] |
| B24 | Biến động BTC | 30 ngày **29,7%**/năm · 1 năm 43,4%/năm · **phân vị 12** trên 1.438 ngày | như trên |
| B25 | Funding hiện tại (quy năm) | BTC 10,95% · ETH 4,79% · SOL 10,24% · BNB 6,74% · XRP 10,95% · DOGE 5,54% | `fapi/v1/premiumIndex`, [đo 2026-07-31] |
| B26 | Cỡ mẫu cần cho edge +0,047 R | **≈ 5.486 lệnh** ⇒ ở 100–150 lệnh/năm = **37–55 năm** | Công thức §4.2 tài liệu 01, σ_R = 1,4 |

**Giới hạn đã biết của mọi phép đo trên:** in-sample; không hiệu chỉnh multiple testing (~40 biến thể
đã thử); chi phí là phí phẳng 0,10% khứ hồi, **chưa gồm funding và slippage**; phép đo khung 1h chỉ
trải **1,4 năm** trong một chế độ thị trường duy nhất; vũ trụ 20 symbol là **kẻ sống sót** (§6.3).
Bốn giới hạn này đều **thiên về lạc quan** ⇒ con số thật nhiều khả năng **thấp hơn** con số báo cáo.

## Phụ lục B — Nguồn công khai

| # | Nguồn | Ngày | Dùng cho |
|---|---|---|---|
| C1 | Kang, Y. & Ryu, D., "Time-series momentum and market timing in Bitcoin", *Risk Management* **28**, art. 54, `link.springer.com/article/10.1057/s41283-026-00234-7` | 10/07/2026 | §3.1 — tín hiệu chậm > tín hiệu nhanh trên Bitcoin; mẫu hình ngược với cổ phiếu |
| C2 | "Systematic Trend-Following with Adaptive Portfolio Construction", `arxiv.org/abs/2602.11708` | 12/02/2026 | §3.1 — momentum + biến động phụ thuộc chế độ (chỉ abstract) |
| C3 | "Cryptocurrency market risk-managed momentum strategies", *Finance Research Letters*, `sciencedirect.com/science/article/pii/S1544612325011377` | 01/11/2025 | §3.1 — **dùng với cảnh báo**, con số lợi suất tuần 3,18% khó tin ở mức chi phí thật |
| C4 | "Exploring risk and return profiles of funding rate arbitrage on CEX and DEX", `sciencedirect.com/science/article/pii/S2096720925000818` | 01/08/2026 | §3.1 — **không áp dụng được**, cần chân spot |
| C5 | "The Two-Tiered Structure of Cryptocurrency Funding Rate Markets", *Mathematics* 14(2):346, `mdpi.com/2227-7390/14/2/346` | 20/01/2026 | §3.1 — cấu trúc funding, không có chiến lược khai thác |
| C6 | Bailey & López de Prado, "The Deflated Sharpe Ratio", *J. Portfolio Management* 40(5) | — | Kế thừa từ §A19 tài liệu 01 |
| C7 | `docs/strategy/01-market-landscape.md` (nội bộ) | 29/07/2026 | §7.1 — Fear & Greed 14, dòng ETF −$6,96 tỷ, khối lượng sàn giảm 32–48% |

**Không tìm được dữ liệu công khai cho:** bất kỳ nghiên cứu nào đo hiệu quả của một chiến lược
crypto perp vận hành bởi **một cá nhân** với vốn < $20.000 qua REST API, đã trừ chi phí thật, đã
hiệu chỉnh multiple testing và đã kiểm soát survivorship bias. Đã tìm, không thấy. **Việc nó không
tồn tại tự nó là dữ kiện** — và nó nhất quán với ghi nhận tương tự ở cuối Phụ lục A tài liệu 01.

## Phụ lục C — Neo mã nguồn mới của tài liệu này

| Khẳng định | Neo |
|---|---|
| `ISignalGenerator` đăng ký DI nhưng **0 call site** — xác minh độc lập bằng `grep` toàn `src/`, chỉ ra đúng 1 dòng | `DependencyInjection.cs:72` |
| TP cố định RR=2 cắt cụt đuôi phải của trend-following | `SignalGenerator.cs:12,24` |
| Score chỉ dùng EMA20/50 + MACD histogram; RSI **không** vào score | `MarketAnalyzer.cs:32–53` |
| Chỉ báo tính trên nến cuối (chưa đóng) | `MarketAnalyzer.cs:23–24` |
| 14 lớp chặn `BlockAsync` trong luồng đặt lệnh thật | `LiveOrderService.cs:85,92,106,117,128,144,154,171,190,195,214,222,233,245` |
| Lớp chặn "AI chưa cấu hình" — cần sửa theo §4.4 | `LiveOrderService.cs:92` |
| `MinRiskRewardRatio = 1.5` sẽ chặn mọi lệnh trailing | `RiskSetting.cs:21` |
| `LossStreakThreshold = 3` — quá nhạy với win-rate 40% | `RiskSetting.cs:38` |
| `RevengeTradeWindowMinutes = 30` — đo bằng phút, vô nghĩa với bot | `RiskSetting.cs:35`, `RevengeTradeDetector.cs:16–30` |
| `TiltSizeIncreasePercent = 50` — tái dụng làm bộ bắt bug size | `RiskSetting.cs:42` |
| Cron `market-scan` `*/5`, `trade-advisor` `*/1` | `Program.cs:129`, `Program.cs:142` |
| Watchlist 4 symbol, đều `Interval = "1h"` | `SeedData.cs:61–64` |
| Size theo `CurrentBalance`, `stopDistance` ở mẫu số | `TradeService.cs:100–104` |
| Ngưỡng rủi ro mặc định | `RiskSetting.cs:17–42` |

---

*Tài liệu này không chứa lời khuyên đầu tư. Nó phân tích tính khả thi kỹ thuật và cấu trúc chi phí
của một hệ thống phần mềm giao dịch. Mọi quyết định về vốn thuộc về chủ dự án.*

*Mọi phép đo trong tài liệu này là **sàng lọc in-sample**, đủ mạnh để loại trừ, không đủ mạnh để
xác nhận. Cổng xác nhận duy nhất có hiệu lực vẫn là bảng §4.3 của `01-edge-measurement.md`.*
