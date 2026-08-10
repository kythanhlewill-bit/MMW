# Phân tích hệ thống đang lỗ và benchmark các hệ thống giao dịch tương tự

Ngày phân tích: **2026-08-04**  
Phạm vi: MMW Adaptive Execution V2, BTCUSDT + ETHUSDT, khung vào lệnh 15 phút  
Baseline hợp lệ: backtest **#28, #29 và #30**  
Tài liệu liên quan: [adaptive-execution-v2.md](adaptive-execution-v2.md),
[adaptive-execution-v2-review.md](adaptive-execution-v2-review.md)

> Đây là tài liệu nghiên cứu hệ thống, không phải cam kết lợi nhuận hay khuyến nghị bật lệnh thật.
> V2 hiện không đạt điều kiện chấp nhận; live trading phải tiếp tục tắt cho tới khi một thiết kế mới
> chứng minh được expectancy dương sau phí trên mẫu chưa dùng để chỉnh tham số.

---

## 1. Kết luận điều hành

Hệ thống không lỗ chủ yếu vì chọn sai một ngưỡng R:R, sai vài giờ giao dịch hoặc do mô hình khớp
limit quá bảo thủ. Hệ thống lỗ vì hai vấn đề nằm sâu hơn:

1. **Chưa có lợi thế thô đủ lớn.** Run #29 đạt expectancy sau mọi ma sát là `−0,3116R/lệnh`.
   Tổng commission, funding và slippage xấp xỉ `0,3030R/lệnh`; cộng chúng trở lại chỉ còn khoảng
   `−0,0086R/lệnh`. Nói cách khác, trước chi phí hệ thống gần hòa vốn, sau chi phí thì lỗ rõ ràng.
2. **Điểm bối cảnh đang được phép thay thế trigger vào lệnh.** Một setup có thể đủ điểm nhờ regime,
   xu hướng 4h, volatility, session, leader, funding và vị trí EMA/VWAP dù **không có BOS, momentum
   bằng 0, volume bằng 0 và liquidity bằng 0**. Đây là nguyên nhân kiến trúc, không chữa được bằng
   cách cộng thêm RSI, Fibonacci hoặc một mẫu hình nữa.

Nguồn lỗ lớn nhất là mode `Standard`: 3.664/4.799 lệnh, tương đương 76,35% số lệnh, tạo khoảng
`−1.271R`, gần 85% tổng mức lỗ. `RangeQuick` cũng âm rõ ràng. `StrongTrendRunner` ít xấu hơn nhưng
chưa chứng minh được lợi thế dương.

Hướng nghiên cứu đáng làm tiếp là **Adaptive Execution V3 — Trigger-first, cost-aware**:

```text
Xác định setup
    → trigger bắt buộc theo setup
    → score chỉ đo chất lượng, không cứu trigger thiếu
    → kiểm tra R:R ròng và chi phí theo đúng entry/stop
    → lập lệnh và quản trị runner
```

Ưu tiên tăng tỉ lệ thắng là hợp lý, nhưng thứ tự mục tiêu phải là:

1. expectancy sau phí không âm;
2. cận dưới khoảng tin cậy của win rate tăng;
3. drawdown và chuỗi thua giảm;
4. đủ số mẫu và không tối ưu lặp lại trên OOS cũ.

Không chấp nhận cách “tăng win rate” bằng TP quá gần hoặc stop quá rộng nếu expectancy tiếp tục âm.

---

## 2. Baseline: hệ thống đang lỗ như thế nào?

### 2.1 Ba run hợp lệ cuối

| Run | Khoảng / fill | Lệnh | Win rate | Expectancy, CI 95% | Max DD |
|---:|---|---:|---:|---:|---:|
| #28 | OOS 2022–2023, bảo thủ | 1.524 | 30,05% | **−0,361R** [−0,449; −0,274] | 76,87R |
| #30 | OOS 2022–2023, lạc quan | 1.525 | 30,03% | **−0,362R** [−0,450; −0,275] | 77,14R |
| #29 | 2020–2026-08, bảo thủ | 4.799 | 29,03% | **−0,312R** [−0,362; −0,261] | 196,93R |

Hai fill model ở OOS chỉ lệch một lệnh. Vì vậy kết luận âm **không phụ thuộc** vào giả định “chạm
là khớp” hay “phải xuyên mức mới khớp”.

### 2.2 Nguồn lỗ theo mode

| Mode | Lệnh | Tỉ trọng | Win rate | Expectancy | Tổng R xấp xỉ | Đánh giá |
|---|---:|---:|---:|---:|---:|---|
| RangeQuick | 835 | 17,40% | 28,62% | −0,2252R | −188R | Blind fade ở biên chưa có edge. |
| Standard | 3.664 | 76,35% | 28,28% | −0,3469R | −1.271R | Nguồn lỗ chính. |
| StrongTrendRunner | 300 | 6,25% | 39,33% | −0,1213R | −36R | Ít xấu hơn, CI vẫn chứa 0; chưa có edge. |

`StrongTrendRunner` hiện yêu cầu structure ít nhất 8 điểm và volume đủ 5 điểm. Win rate 39,33% so
với 28,28% của `Standard` là bằng chứng **gợi ý** rằng structure + volume bắt buộc có ích. Đây chưa
phải bằng chứng nhân quả vì hai mode còn khác regime, cách entry và cách exit.

### 2.3 Hệ thống bị stop quá nhiều

| Exit reason | Lệnh | Tỉ trọng | R trung bình |
|---|---:|---:|---:|
| Target | 1.053 | 21,94% | +2,584R |
| Stop | 3.628 | 75,60% | −1,151R |
| TimeStop | 118 | 2,46% | −0,333R |

Time-stop không phải gốc vấn đề vì chỉ chiếm 2,46%. Gốc vấn đề là gần ba phần tư lệnh chạm stop.

Một phép thử tư duy, giữ nguyên giá trị trung bình các exit: cần biến khoảng **400 stop thành target**
để bù khoảng `−1.495R` của toàn run. Con số này tương đương thay đổi kết quả của khoảng 8,3% tổng
số lệnh, đưa win rate từ 29,03% lên xấp xỉ **37,4%**. Mục tiêu này không phải dự báo; nó chỉ cho
thấy mức cải thiện cần thiết lớn hơn nhiều so với việc bỏ một vài giờ xấu.

### 2.4 Chi phí đang ăn hết lợi thế thô

| Thành phần run #29 | Tổng | Bình quân/lệnh |
|---|---:|---:|
| Commission | 1.057,2735R | 0,2203R |
| Funding ròng | 7,1097R | 0,0015R |
| Slippage | 389,8499R | 0,0812R |
| **Tổng ma sát** | **1.454,2331R** | **0,3030R** |
| Expectancy sau ma sát |  | **−0,3116R** |
| Expectancy thô xấp xỉ |  | **−0,0086R** |

`0,2218R/lệnh` được UI/tài liệu cũ gọi là “chi phí” chỉ gồm commission + funding. Khi đánh giá
khả năng giao dịch thật phải dùng **tổng ma sát 0,3030R**, bao gồm slippage.

Đây là tín hiệu quan trọng: nếu chỉ nâng win rate bằng TP ngắn hơn, số lần thoát target có thể tăng
nhưng chi phí chiếm tỉ trọng lớn hơn trên mỗi R lời. Kết quả có thể đẹp về win rate và xấu hơn về PnL.

### 2.5 Không phải lỗi riêng của một năm, regime hay khung giờ

Expectancy theo năm của run #29 đều âm:

| Năm | Expectancy |
|---:|---:|
| 2020 | −0,2805R |
| 2021 | −0,1715R |
| 2022 | −0,2094R |
| 2023 | −0,5287R |
| 2024 | −0,1912R |
| 2025 | −0,4670R |
| 2026 đến tháng 8 | −0,3175R |

Các regime chính cũng âm: Range `−0,225R`, TrendUp `−0,374R`, TrendDown `−0,391R`, HighVol
`−0,251R`. Cả 24 giờ UTC đều âm. Vì vậy:

- bỏ vài giờ xấu không thể tạo edge;
- chặn cuối tuần có thể giảm risk/overtrading nhưng hiện chưa có breakdown theo thứ trong tuần để
  kết luận nó làm tăng expectancy;
- tránh tin mạnh vẫn là quy tắc quản trị tail risk hợp lý, nhưng `EventDay` hiện chỉ có 42 lệnh,
  không đủ để suy ra lợi thế;
- phải sửa chất lượng setup và execution trước khi tối ưu lịch giao dịch.

---

## 3. Nguyên nhân kỹ thuật trong thiết kế hiện tại

### 3.1 Một tổng điểm cho phép “điểm nền” cứu setup thiếu trigger

Thang điểm đầy đủ là 85. Backtest không có lịch sử OI và depth nên đo được 75. Với
`MinScoreToEnter = 55`, số điểm thực tế cần đạt là:

```text
ceil(55 × 75 / 85) = 49 điểm
```

Một trường hợp yếu nhưng hợp lệ về mặt code có thể nhận:

```text
daily/regime alignment       10
4h trend alignment           10
volatility quality            6
session quality               6
leader correlation            4
funding crowding              4
near EMA/VWAP                 8
market structure: no BOS      3
momentum                      0
volume confirmation           0
liquidity                     0
-------------------------------
total                        51  >= 49
```

`MarketStructureCriterion` cho 3 điểm nền khi không có BOS; BOS chưa retest cho 6; retest thất bại
trả 0 điểm nhưng không veto. `VolumeConfirmationCriterion` cũng có thể trả 0 mà setup vẫn vào.
`EntryLocationCriterion` cho 8 điểm chỉ vì giá gần EMA/VWAP.

Như vậy hệ thống đang trả lời tốt câu hỏi “bối cảnh có tương đối thuận không?”, nhưng chưa bắt buộc
trả lời câu hỏi quan trọng hơn: **“sự kiện nào vừa xảy ra khiến entry ngay lúc này có lợi thế?”**

### 3.2 `Standard` là “mọi thứ còn lại”, không phải một setup cụ thể

Logic hiện tại là:

- Range → `RangeQuick`;
- trend đúng chiều + structure ≥ 8 + volume = 5 → `StrongTrendRunner`;
- mọi trường hợp không thuộc hai nhóm trên → `Standard`.

Vì vậy `Standard` gom nhiều trạng thái khác nhau: pullback tốt, pullback chưa xác nhận, BOS chưa
retest, không BOS, chạm EMA, động lượng yếu và cả trường hợp volume không thuận. Một mode rộng như
vậy khó có phân phối thống kê ổn định và chiếm 76,35% lệnh là điều nguy hiểm.

### 3.3 Lệnh limit hiện tối ưu giá nhưng chưa chứng minh timing

`Standard` đặt limit ở structural retest hoặc EMA20; `RangeQuick` đặt limit quanh biên range. Cả hai
chưa bắt buộc có reclaim/acceptance tại thời điểm vào. Hệ quả thực chiến:

- range limit dễ khớp đúng lúc thị trường chuyển thành breakout;
- trend limit dễ khớp khi pullback đã chuyển thành đảo chiều;
- “được giá tốt hơn” đi kèm adverse selection: limit buy có xác suất khớp cao nhất đúng lúc giá
  đang tiếp tục đi xuống.

Nghiên cứu limit-order nhấn mạnh fill probability phụ thuộc trạng thái queue/order flow và phải cân
bằng với adverse selection; mức limit sâu hơn không chỉ “rẻ hơn” mà còn có xác suất khớp khác.
([Lehalle & Mounjid](https://arxiv.org/abs/1610.00261),
[Lokin & Yu](https://arxiv.org/abs/2403.02572))

Hai fill model OHLC của #28/#30 gần như trùng nhau chỉ chứng minh kết luận backtest không nhạy với
quy tắc touch/through hiện tại. Nó **không chứng minh** live fill, queue position và adverse selection
là không đáng kể.

### 3.4 R:R đang là gross geometric R:R, chưa phải net tradeability

`MinStructuralRr = 1,6` hiện giữ lại trên 92% cơ hội, nên không phải bottleneck. Tuy nhiên nó đang
đo khoảng cách hình học entry–stop–target, trong khi xác suất thắng và chi phí theo R thay đổi rất
mạnh theo khoảng stop.

`SafeLimitEntry` cho phép entry cách stop chỉ bằng 0,25 lần unit risk ban đầu. Khi risk budget cố
định, entry càng gần stop thì quantity càng lớn; commission theo R có thể tăng mạnh. R:R hình học
đẹp hơn nhưng trade có thể kém hơn sau phí.

Nếu không tìm được cản đối diện, planner còn dùng target fallback ít nhất bằng `MinStructuralRr`.
Target đó là giả định, không phải mức thanh khoản đã được thị trường chứng minh.

### 3.5 Chưa có dữ liệu để quy tội chính xác cho từng indicator

Full backtest dùng `PersistScorecards = false`. Báo cáo hiện không có:

- score band của lệnh thắng/thua;
- từng criterion tại entry;
- ngày trong tuần;
- stop distance theo bps;
- expected/actual cost R theo lệnh;
- MAE/MFE;
- TP1/runner contribution;
- adverse movement ngay sau khi passive limit fill.

Vì vậy có thể kết luận chắc về **mode, exit, cost và kiến trúc**, nhưng chưa thể nói chính xác
“RSI gây bao nhiêu lệnh thua” hay “Fibonacci tăng bao nhiêu win rate”. Phải thêm telemetry trước
khi loại hoặc nâng trọng số từng indicator.

---

## 4. Các hệ thống bên ngoài cho ta học được gì?

### 4.1 Trend-following: tách regime khỏi trigger và chấp nhận ít lệnh

Nghiên cứu time-series momentum kinh điển tìm thấy return persistence 1–12 tháng trên 58 hợp đồng
futures/forward thanh khoản. Đây là bằng chứng cho ý tưởng “đi theo xu hướng” ở mức danh mục và
horizon dài, **không phải** bằng chứng rằng một EMA hoặc MACD 15 phút trên crypto có edge.
([Moskowitz, Ooi & Pedersen](https://pages.stern.nyu.edu/~lpederse/papers/TimeSeriesMomentum.pdf))

Một nghiên cứu phản biện sau đó cho rằng bằng chứng predictability asset-by-asset yếu và lợi nhuận
danh mục có thể gần với chiến lược dựa trên historical mean. Điều này nhắc rằng không được biến
“trend following đã được nghiên cứu” thành lý do mặc định để mọi setup trend được vào.
([Huang et al., JFE 2020](https://www.sciencedirect.com/science/article/abs/pii/S0304405X19301953))

Bài học áp dụng cho MMW:

- daily/4h trend là **context classifier**;
- BOS, acceptance, pullback contraction và reclaim mới là **entry trigger**;
- context thuận không được bù cho trigger thiếu;
- volatility scaling giúp giảm biến động đường vốn, không tự tạo win rate hoặc expectancy.

### 4.2 Trading-range breakout trên Bitcoin: dùng đúng regime, không dùng như bằng chứng chung

Một nghiên cứu peer-reviewed trên dữ liệu Bitcoin ngày 2010–2019 cho thấy trading-range breakout
là nhóm rule có forecasting power nổi bật, đặc biệt trong thị trường trend mạnh.
([Gerritsen et al.](https://dspace.library.uu.nl/handle/1874/407735))

Bài học áp dụng:

- ngày có breakout + volume mạnh nên chuyển sang setup breakout/retest, không tiếp tục fade range;
- range boundary chỉ có ý nghĩa khi thị trường còn giữ balance;
- một nến đóng ngoài range với thân và volume xác nhận phải hủy mọi lệnh fade chưa khớp;
- không sao chép nguyên lookback của nghiên cứu ngày sang khung 15 phút perpetual futures.

### 4.3 Technical rules trên Bitcoin: ensemble và kiểm soát false discovery

Frömmel & Deprez kiểm tra 75.360 rule thuộc sáu lớp, có transaction cost, multiple-hypothesis
procedure và portfolio OOS; họ tìm thấy một số tổ hợp rule có thể vượt buy-and-hold về risk-return.
Điểm đáng học không phải “indicator X luôn thắng”, mà là quy trình **chọn rule sau chi phí, kiểm
soát data mining và đánh giá tổ hợp ngoài mẫu**.
([Frömmel & Deprez](https://ssrn.com/abstract=4401552))

Áp dụng cho MMW:

- RSI divergence, Fibonacci, double bottom, H&S và staircase chỉ nên là confluence sau structure
  trigger;
- không cộng càng nhiều indicator càng tốt;
- ghi số lần thử và dùng false-discovery/overfitting control khi so nhiều biến thể;
- nếu xây ensemble, mỗi thành viên phải là một setup hoàn chỉnh, không phải một indicator rời.

### 4.4 Volume có thông tin, nhưng “volume cao” không tự định nghĩa chiều

Nghiên cứu trên crypto cho thấy quan hệ returns–volume hai chiều và thay đổi theo horizon; volume
có ích khi phân tích cùng price movement. Nó không nói rằng cứ volume cao là mua hoặc bán.
([Returns and volume: Frequency connectedness in cryptocurrency markets](https://www.sciencedirect.com/science/article/pii/S0264999320312499))

Áp dụng:

- trend: volume phải đi cùng impulse/BOS thuận chiều, sau đó co trong pullback;
- range: volume lớn nhưng giá không giữ được ngoài biên có thể là rejection; volume lớn kèm close
  ngoài biên và follow-through là breakout, không phải fade;
- đo volume theo relative volume cùng khung giờ để tránh nhầm seasonality phiên;
- volume là xác nhận cho event giá, không đứng riêng thành trigger.

### 4.5 Volatility-managed risk: giảm drawdown, không chữa entry

Moreira & Muir cho thấy giảm exposure khi volatility cao có thể cải thiện Sharpe trên nhiều factor
truyền thống. Nhưng các nghiên cứu sau không tìm thấy outperformance có hệ thống trong mọi trường
hợp. ([Moreira & Muir](https://www.nber.org/papers/w22208),
[Cederburg et al.](https://www.sciencedirect.com/science/article/abs/pii/S0304405X2030132X))

Với MMW, expectancy được chuẩn hóa theo filled risk budget nên giảm size ở high-vol chủ yếu làm
giảm drawdown tiền thật, không biến một entry `−0,3R` thành entry dương. Giữ volatility scaling như
risk control, không báo cáo nó như cải thiện chất lượng tín hiệu.

### 4.6 Execution-aware system: dùng actual fills, maker flag và commission

Binance USDⓈ-M hỗ trợ `GTX` trong `timeInForce`; dữ liệu user trade trả về `commission`, `maker`,
`price`, `qty` và `realizedPnl`. Đây là nguồn đúng để hiệu chỉnh live/shadow thay vì giả định maker
fee cố định. ([Binance USDⓈ-M Trade API](https://developers.binance.com/docs/derivatives/usds-margined-futures/trade/rest-api/New-Order))

Áp dụng:

- entry passive dùng `GTX/post-only`; nếu lệnh sẽ ăn book ngay thì hủy, không âm thầm thành taker;
- snapshot actual maker/taker commission theo account;
- lưu queue wait time, partial fill và return sau fill 1/3 nến;
- stop phải mô phỏng như market/protected market có slippage. Stop order sau khi kích hoạt không
  bảo đảm đúng giá trigger. ([CME Futures Order Types](https://www.cmegroup.com/education/courses/futures-trading-mechanics-and-regulation/futures-order-types))

### 4.7 Backtest discipline là một phần của strategy

Bailey et al. chỉ ra holdout thông thường có thể không đủ khi người phát triển thử quá nhiều biến
thể và đề xuất đo xác suất backtest overfitting bằng combinatorially symmetric cross-validation.
([The Probability of Backtest Overfitting](https://papers.ssrn.com/abstract=2326253))

OOS 2022–2023 của MMW đã tới trial thứ 5, nên không còn là mẫu “chưa nhìn”. Mọi threshold mới tìm
trên đó phải được coi là development result. Bằng chứng cuối cần đến từ symbol chưa dùng để tune
và/hoặc forward shadow bắt đầu sau ngày đóng băng V3.

---

## 5. Kiến trúc đề xuất: Adaptive Execution V3

### 5.1 Tách năm tầng quyết định

| Tầng | Câu hỏi | Có quyền cho vào lệnh? |
|---|---|---|
| 0. Safety/Data | Dữ liệu, calendar, exposure, daily loss có hợp lệ? | Chỉ veto/giảm risk. |
| 1. Setup classifier | Đây là RangeRejection, TrendPullback, StrongTrendBreakout hay NoSetup? | Không; chỉ chọn playbook. |
| 2. Core trigger gate | Event bắt buộc của playbook đã xảy ra và còn fresh? | Có. Thiếu một điều kiện lõi → NoTrade. |
| 3. Quality score | Setup đã hợp lệ tốt đến mức nào? | Chỉ rank/size trong cùng setup; không cứu gate fail. |
| 4. Execution viability | R:R ròng, cost R, fill và stop có giao dịch được? | Có quyền veto. |
| 5. Position/exit | Vào bao nhiêu, chốt phần nào, runner ra sao? | Không được tăng tổng risk đã duyệt. |

Điểm khác biệt cốt lõi: **score không còn là phép OR mềm giữa các tín hiệu**. Regime, EMA, RSI,
Fibonacci và session chỉ được chấm sau khi setup đã có event giá bắt buộc.

### 5.2 Setup A — TrendPullback

State machine:

```text
NoSetup
  → Armed: daily/4h cùng chiều, không bị news/safety veto
  → ImpulseConfirmed: BOS đóng nến + thân nến hợp lệ + volume thuận
  → Pullback: giá quay về broken level/value area, volume co so với impulse
  → ReclaimConfirmed: retest không gãy và nến đóng lại thuận chiều
  → PassiveOrder: post-only tại retest, hết hạn sau 3–4 nến
  → Filled / Expired / Invalidated
```

Core gate bắt buộc:

1. effective regime và 4h cùng chiều;
2. có BOS thuận chiều; `No BOS` không còn nhận quyền đi tiếp;
3. BOS không bị retest fail;
4. impulse có body xác nhận theo `MinCandleBodyRatio` và volume theo
   `VolumeBreakoutMultiple` hiện có;
5. pullback không đóng qua invalidation và volume thấp hơn impulse;
6. có reclaim close sau retest;
7. trigger còn fresh trong `RetestWindowBars`;
8. qua net cost/R:R gate.

EMA20/VWAP chỉ là vị trí hợp lưu. Chạm EMA mà không có BOS → pullback → reclaim không phải setup.

### 5.3 Setup B — RangeRejection

State machine:

```text
StableRange
  → ArmedAtEdge
  → SweepOutside
  → CloseBackInside + rejection
  → Retest
  → PassiveOrder
  → QuickTarget / Stop / Expired
```

Core gate bắt buộc:

1. active range có tối thiểu hai pivot xác nhận ở mỗi phía và width không mở rộng liên tục;
2. không có intraday override sang TrendUp/TrendDown;
3. giá sweep qua biên rồi **đóng trở lại trong range**;
4. nến rejection có vị trí close đúng chiều;
5. volume không xác nhận continuation breakout;
6. lệnh chỉ đặt sau confirmation/retest và hết hạn sau 3–4 nến;
7. target đầu là vùng cân bằng/mức cấu trúc thực, không tự dựng 1,6R khi không có cản;
8. nếu có close ngoài biên + body mạnh + relative volume mạnh thì hủy fade, chuyển chờ
   StrongTrendBreakout.

`RangeQuick` hiện tại phải được thay thế, không chỉ đổi tên. Backtest cho thấy đặt limit mù ở biên
không có edge.

### 5.4 Setup C — StrongTrendBreakout/Runner

Giữ ý tưởng mode hiện tại nhưng thêm trigger timing:

1. trend đúng chiều ở daily/4h;
2. structure ≥ 8 và volume = 5 như hiện tại;
3. BOS close có acceptance/follow-through, không phải một wick xuyên mức;
4. không có dấu hiệu exhaustion: volume cực lớn nhưng thân nhỏ/close xấu;
5. tranche đầu chỉ vào sau confirmation;
6. tranche thứ hai chỉ arm khi retest/reclaim, không đặt sẵn chỉ vì giá thấp hơn;
7. tổng risk của mọi tranche cố định trước lần khớp đầu.

Chưa nên dùng ba tranche. Hai tranche 60/40 hiện tại đủ để kiểm định giả thuyết mà không thêm một
nhánh fill và một tham số mới.

### 5.5 Scale-in có cấu trúc, không averaging-down một thesis đang sai

Quy tắc bắt buộc:

```text
riskBudget[i] = totalRiskBudget × riskWeight[i]
quantity[i]   = riskBudget[i] / abs(entry[i] - sharedStop)
sum(riskWeight) = 1
```

- tranche sâu hơn chỉ được mở sau **một confirmation mới**;
- không thêm lệnh chỉ vì floating PnL đang âm;
- close qua invalidation → hủy toàn bộ tranche chưa khớp;
- không nới shared stop để “cho lệnh thở”;
- tính commission và expected slippage của từng tranche trước khi duyệt.

Đây là cách lấy giá trung bình có kiểm soát mà không biến scale-in thành martingale.

---

## 6. Cost gate và R:R ròng

### 6.1 Công thức phải tính sau khi đã biết entry và stop

Cho một kế hoạch cụ thể:

```text
ExpectedCostR =
    (entry commission
     + expected exit commission
     + expected entry slippage
     + expected stop slippage
     + expected funding) / FilledRiskBudget

NetTargetR = GrossTargetR - ExpectedCostR
NetStopR   = 1 + ExpectedCostR

BreakEvenWinRate = NetStopR / (NetStopR + NetTargetR)
```

Rào đề xuất:

1. `NetStructuralRr >= 1,5`, không dùng gross R:R;
2. `ExpectedCostR / GrossTargetR <= 10%`;
3. target fallback phải mang cờ `IsApproximateTarget`; setup dùng fallback không được full size;
4. stop distance phải đủ lớn để cost/R không vượt rào;
5. **không được nới stop giả tạo để vượt cost gate** — nếu stop cấu trúc quá gần thì bỏ lệnh.

Ngưỡng 10% và net 1,5 là giả thuyết cần đóng băng trước backtest, không phải tham số đã được chứng
minh. Chỉ so một biến thể vừa phải và một biến thể chặt; không grid-search hàng chục mức trên
2022–2025.

### 6.2 Vì sao cost gate có khả năng tác động lớn

Run #29 trả trung bình 0,303R ma sát cho mỗi lệnh. Một setup gross gần 0 chỉ cần bị loại vì cost cao
là đã cải thiện net expectancy, không cần dự báo chiều tốt hơn. Cost gate không trực tiếp nâng xác
suất giá đi đúng, nhưng nó loại các lệnh mà “đúng hướng vẫn không đủ tiền trả phí”.

Phải báo cáo riêng:

- gross expectancy trước phí;
- commission R;
- funding R;
- slippage R;
- net expectancy;
- phân phối cost R theo mode và stop-distance decile.

---

## 7. Partial profit và runner để tăng lợi nhuận mà không làm giả win rate

Chốt 50% cố định không phải lúc nào cũng tối ưu. Đề xuất:

1. TP1 phải là mức cấu trúc và đạt net R tối thiểu sau phí;
2. sau TP1, stop runner chuyển tới **fee-adjusted breakeven**, không phải entry thô;
3. chọn phần chốt nhỏ nhất trong khoảng cho phép để khóa `LockedNetRMin`, ví dụ:

```text
lockedNetR = firstTakeProfitFraction × GrossTp1R - ExpectedCostR
```

4. chỉ chấp nhận fraction nếu `lockedNetR >= LockedNetRMin`;
5. runner trail theo pivot xác nhận từ nến kế tiếp, stop chỉ siết chặt, không nới;
6. báo cáo riêng `TP1 contribution`, `runner contribution`, `runner give-back` và số lệnh TP1 rồi
   quay về breakeven.

Đề xuất ban đầu `LockedNetRMin = 0,25R`, fraction trong `[30%, 60%]`. Đây là tham số nghiên cứu,
không bật live trước khi có phân phối MFE/MAE. Mục đích là khóa một phần lợi nhuận sau phí và vẫn
giữ exposure vào ngày trend mạnh, không phải đẩy win rate bằng chốt vài tick.

---

## 8. Telemetry bắt buộc trước khi sửa strategy

Thêm một run chẩn đoán **không đổi hành vi** và lưu aggregate, tránh hàng trăm nghìn scorecard DB:

| Nhóm | Trường cần lưu |
|---|---|
| Phân đoạn | Symbol, Mode, SetupType, Regime, Direction, HourUtc, DayOfWeek, ExitReason |
| Trigger | BOS state, retest state, reclaim state, trigger age, range sweep/rejection state |
| Score | TotalScore band 5 điểm, từng criterion points, available max |
| Structure | stop distance ATR/bps, gross R:R, net R:R, target approximate flag |
| Cost | expected/actual fee R, funding R, slippage R, total cost R |
| Path | MAE R, MFE R, bars-to-MAE, bars-to-MFE, bars-held |
| Execution | order type, offered/filled/expired, wait bars, partial fill, maker/taker |
| Adverse selection | return 1 và 3 nến sau fill, theo R và theo bps |
| Exit | TP1 hit, fraction closed, runner R, give-back R, fee-adjusted BE hit |

Các bảng tối thiểu cần xem:

1. win rate/expectancy theo `Mode × StructurePoints × VolumePoints`;
2. win rate/expectancy theo `BOS/Retest/Reclaim state`;
3. expectancy theo cost-R decile và stop-distance-bps decile;
4. MAE/MFE của winner và loser theo setup;
5. passive fill rate và adverse selection theo khoảng cách limit;
6. breakdown thứ trong tuần để kiểm chứng giả thuyết cuối tuần;
7. score band calibration: score cao hơn có thực sự cho win rate/expectancy cao hơn không.

Nếu score 70 không tốt hơn score 55, score hiện tại không calibrated và không nên tiếp tục dùng để
tăng size.

---

## 9. Ma trận backtest đề xuất

Mỗi run chỉ thay đổi một khối logic. Mọi tham số phải snapshot và ghi `ComparableTrialNumber`.

| Bước | Biến thể | Thay đổi duy nhất | Điều kiện đi tiếp |
|---|---|---|---|
| D0 | Diagnostic parity | Chỉ thêm telemetry, không đổi quyết định | Trade-by-trade parity với #29. |
| E1 | Standard CoreGate | `No BOS` và retest fail không được thành Standard; dùng TrendPullback state machine | WR tăng ≥5 điểm %, expectancy và DD tốt hơn baseline. |
| E2 | Range Rejection | Thay blind limit bằng sweep → close-back → retest | Range có ≥200 lệnh hoặc CI đủ hẹp; expectancy không âm. |
| E3 | Net Cost Gate | Net R:R + expected CostR theo entry/stop | Net expectancy tăng; gross expectancy không bị báo sai. |
| E4 | Strong Trend Trigger | Acceptance/follow-through + conditional second tranche | ≥200 lệnh trên tập mở rộng; CI expectancy không âm. |
| E5 | Dynamic Partial | Fraction theo locked net R; runner contribution riêng | Net expectancy tăng, không chỉ win rate. |
| C1 | Combined candidate | Chỉ ghép các E-step đã pass độc lập | Pass full, walk-forward và shadow. |

### 9.1 Objective và acceptance

Research acceptance cho mỗi bước:

- số lệnh/mode tối thiểu 100, ưu tiên 200;
- win rate tăng ít nhất 5 điểm phần trăm **và** cận dưới Wilson tăng;
- expectancy point estimate không âm hoặc cải thiện có ý nghĩa so với baseline;
- max drawdown theo size thật giảm;
- kết quả không đảo dấu giữa fill bảo thủ và lạc quan;
- không một bước nào được tuyên bố tốt chỉ vì cắt gần hết sample;
- báo số cơ hội bị loại, không chỉ số lệnh còn lại.

Live acceptance nghiêm hơn:

- CI 95% của expectancy có cận dưới > 0 sau mọi chi phí;
- mode định bật có ít nhất 200 lệnh ngoài mẫu hoặc số lượng forward-shadow tương đương đã định trước;
- actual shadow cost/fill không xấu hơn mô hình backtest quá tolerance đóng băng trước;
- không thay threshold sau khi mở cửa sổ final validation.

### 9.2 Chia dữ liệu để tránh tự lừa mình

2022–2023 đã là trial thứ 5; 2024–2025 cũng đã được dùng nhiều lần. Do đó:

- dùng dữ liệu đã xem để **phát triển và bác bỏ** ý tưởng, không gọi nó là final OOS;
- backfill thêm symbol theo một quy tắc thanh khoản định trước, không chọn symbol vì backtest đẹp;
- test walk-forward theo năm và theo symbol;
- dành một tập symbol/giai đoạn chưa mở báo cáo để final validation;
- sau khi đóng băng candidate, chạy forward shadow từ 2026-08 trở đi;
- nếu thử nhiều biến thể, dùng CSCV/PBO hoặc ít nhất false-discovery control và công khai tổng số
  trial.

---

## 10. Thứ tự triển khai khuyến nghị

### P0 — Làm trước mọi thay đổi strategy

1. Thêm aggregate telemetry ở §8 và parity test.
2. Sửa báo cáo UI để tách commission+funding khỏi tổng ma sát có slippage.
3. Báo gross expectancy và net expectancy.
4. Thêm DayOfWeek, score band, stop-distance/cost decile, MAE/MFE.

### P1 — Chặn nguồn lỗ lớn nhất

5. Thay mode `Standard` chung chung bằng `TrendPullback` có CoreGate.
6. Score chỉ được tính sau CoreGate; core fail không được bù bằng điểm nền.
7. Không BOS → NoSetup; failed retest → veto setup.

### P2 — Sửa range và execution economics

8. Thay `RangeQuick` bằng `RangeRejection` state machine.
9. Thêm expected CostR, net R:R và minimum economic stop distance.
10. Entry passive dùng post-only; shadow lưu actual maker/taker/commission.

### P3 — Runner và scale-in

11. StrongTrend chỉ scale-in sau confirmation mới.
12. Dynamic partial theo locked net R; báo runner contribution riêng.
13. Volatility chỉ điều chỉnh size/drawdown, không dùng để cứu setup.

### P4 — Chỉ sau khi P0–P3 có bằng chứng

14. Đánh giá weekend/news filter từ breakdown thật.
15. Calibrate score hoặc bỏ score nếu score band không monotonic.
16. Mở rộng symbol và chạy final untouched/forward validation.

---

## 11. Những việc không nên làm

- Không hạ `MinStructuralRr`; nó đang giữ trên 92% mẫu và không phải bottleneck.
- Không tăng trọng số RSI/Fibonacci/pattern để ép score qua ngưỡng.
- Không tối ưu riêng từng giờ UTC khi cả 24 giờ đều âm.
- Không nhìn OOS #28/#30 thêm rồi gọi threshold mới là out-of-sample.
- Không tăng win rate bằng TP quá gần mà bỏ qua expectancy sau phí.
- Không nới stop để làm chi phí theo R trông nhỏ hơn.
- Không đặt sẵn 2–3 lệnh bình quân giá khi chưa có confirmation mới.
- Không bật riêng StrongTrendRunner chỉ vì win rate 39,33%; expectancy chưa dương và OOS mới 91 lệnh.
- Không dùng AI/LLM để override CoreGate; AI chỉ được veto hoặc giảm risk.

---

## 12. Quyết định đề xuất

1. **Bác bỏ V2 hiện tại cho live trading.** Bằng chứng âm ổn định trên full, OOS và hai fill model.
2. **Không tiếp tục tune threshold của score chung.** Sai số chính nằm ở kiến trúc “điểm nền cứu
   trigger”, không phải một con số 55 hay 1,6.
3. **Mở phase nghiên cứu V3 với tên `Trigger-first, cost-aware`.** P0 đầu tiên chỉ là telemetry và
   parity, chưa thay chiến lược.
4. **Thay Standard trước.** Đây là 76,35% số lệnh và gần 85% tổng lỗ, nên có expected impact lớn nhất.
5. **Tăng win rate bằng selective entry, không bằng sửa payoff giả tạo.** Mục tiêu nghiên cứu hợp lý
   đầu tiên là vùng 35–40%, nhưng chỉ được chấp nhận nếu expectancy sau toàn bộ ma sát không âm.
6. **Giữ live off** cho tới khi combined candidate đạt CI expectancy dương trên tập chưa tune và
   actual forward-shadow fill/cost không làm mất edge.

Đánh giá thực chiến cuối cùng: hệ thống hiện có nhiều thành phần tốt về risk discipline, audit và
backtest conservatism, nhưng đang thiếu thứ quan trọng nhất của một hệ thống vào lệnh — một event
trigger cụ thể, khác nhau cho từng playbook. Sửa đúng chỗ này có cơ sở tăng win rate; thêm indicator
vào tổng điểm hiện tại thì không.

---

## 13. Nguồn tham khảo chính

1. Moskowitz, Ooi & Pedersen — [Time Series Momentum](https://pages.stern.nyu.edu/~lpederse/papers/TimeSeriesMomentum.pdf).
2. Huang et al. — [Time series momentum: Is it there?](https://www.sciencedirect.com/science/article/abs/pii/S0304405X19301953).
3. Gerritsen et al. — [The profitability of technical trading rules in the Bitcoin market](https://dspace.library.uu.nl/handle/1874/407735).
4. Frömmel & Deprez — [Are Simple Technical Trading Rules Profitable in Bitcoin Markets?](https://ssrn.com/abstract=4401552).
5. Fousekis & Tzaferi — [Returns and volume: Frequency connectedness in cryptocurrency markets](https://www.sciencedirect.com/science/article/pii/S0264999320312499).
6. Moreira & Muir — [Volatility Managed Portfolios](https://www.nber.org/papers/w22208).
7. Cederburg et al. — [On the performance of volatility-managed portfolios](https://www.sciencedirect.com/science/article/abs/pii/S0304405X2030132X).
8. Lehalle & Mounjid — [Limit Order Strategic Placement with Adverse Selection Risk and the Role of Latency](https://arxiv.org/abs/1610.00261).
9. Lokin & Yu — [Fill Probabilities in a Limit Order Book with State-Dependent Stochastic Order Flows](https://arxiv.org/abs/2403.02572).
10. Binance — [USDⓈ-M Futures Trade API](https://developers.binance.com/docs/derivatives/usds-margined-futures/trade/rest-api/New-Order).
11. CME Group — [Futures Order Types](https://www.cmegroup.com/education/courses/futures-trading-mechanics-and-regulation/futures-order-types).
12. Bailey et al. — [The Probability of Backtest Overfitting](https://papers.ssrn.com/abstract=2326253).
