# Adaptive Execution V4 — review bằng chứng và thiết kế đề xuất

Ngày review: **2026-08-04**  
Trạng thái: **PROPOSAL / SHADOW ONLY — chưa được phép bật live**  
Baseline bắt buộc: V3 full run `#40`, V3 OOS cũ `#41/#42`, và run attribution D2 được ghi ở §3.

## 1. Kết luận điều hành

V3 là một cải tiến lớn so với V2 về lọc lệnh và kiểm soát drawdown, nhưng **chưa có lợi thế ròng có thể giao dịch**:

- full 2020–2026: gross `+0,0530R`, friction `0,1556R`, net `−0,1026R`;
- 2022–2023: net `−0,3471R`, CI 95% hoàn toàn dưới 0;
- `TrendPullbackV3` âm rõ trên full; `StrongTrendRunnerV3` không ổn định theo giai đoạn;
- tỷ lệ limit được khớp không tự trả lời limit giúp hay hại, vì limit thường khớp khi giá đang đi ngược vị thế.

V4 nên tập trung vào ba thay đổi có thể kiểm chứng:

1. **Paired execution attribution:** cùng một tín hiệu phải được shadow-simulate qua `MarketOnly`, `CurrentHybridV3` và `AdaptiveHybridV4` trên cùng đường giá.
2. **Setup portfolio có quyền tắt nhánh:** `TrendPullback` về shadow-only; Range và StrongTrend có state machine và policy execution riêng.
3. **Path-aware exit có kiểm soát:** thử partial sớm và failure-to-follow-through bằng các biến thể đăng ký trước; không siết stop chỉ vì muốn tăng win rate.

Không nên tạo V4 bằng cách tăng ngưỡng điểm, thêm nhiều indicator, hoặc chọn tham số đẹp nhất trên 2022–2023. Toàn bộ dữ liệu lịch sử hiện đã được nhìn; bằng chứng live tiếp theo phải đến từ shadow forward.

## 2. Bảng review V3 → yêu cầu V4

Thang điểm: `0 = chưa có`, `1 = yếu`, `2 = có nhưng thiếu`, `3 = dùng được`, `4 = tốt`, `5 = đủ chuẩn rollout`.

| Hạng mục | V3 | Nhận định | Điều V4 phải bổ sung | Cổng V4 |
|---|---:|---|---|---|
| Trigger theo setup | 4/5 | Đã tách Range/Trend/StrongTrend và trigger-first | Giữ hard trigger; không cho tổng điểm cứu trigger thiếu | Trigger parity với V3 control |
| Ổn định regime | 2/5 | Full cải thiện nhưng 2022–2023 hỏng | Thêm trạng thái 1h giữa execution 15m và bias 4h/1d; tắt setup không phù hợp | Walk-forward theo năm |
| Chất lượng TrendPullback | 1/5 | Full `−0,4839R`, CI âm hoàn toàn, chỉ 49 mẫu | Shadow-only, không góp lệnh portfolio | ≥100 mẫu độc lập trước khi xét lại |
| Execution market/limit | 2/5 | Tỷ lệ 60/40 hoặc 50/50 cố định | Paired counterfactual và urgency state; không tăng limit mù | Paired delta CI > 0 |
| Fill realism | 2/5 | Có optimistic/conservative candle fill | Đo adverse fill, expiry, maker flag; shadow L2 khi có dữ liệu | Hai fill model cùng dấu |
| Economics sau phí | 3/5 | Đã có cost gate và gross/net telemetry | Tách alpha edge khỏi execution cost; đặt budget friction theo setup | Net CI > 0; gross đủ trả cost |
| Exit theo đường giá | 2/5 | Có TP1, runner, time-stop 16 nến | Thử partial 0,5R và time-stop 8/12/16 theo variant | Paired delta CI > 0 |
| Kiểm soát drawdown | 4/5 | Full DD giảm từ `205,39R` xuống `11,04R` | Không đánh đổi DD để làm đẹp win rate | DD không xấu hơn V3 |
| Kỷ luật thống kê | 3/5 | Có CI, fingerprints, comparable-trial | Trial registry, paired bootstrap, PBO/DSR, walk-forward | Đạt §10 |
| Live/backtest parity | 3/5 | Logic dùng chung và telemetry observer | Actual fill/maker/slippage reconciliation | Shadow forward đạt §10 |

**Điểm tổng V3: 26/50.** V3 đủ làm control nghiên cứu, chưa đủ làm chiến lược live. V4 chỉ được nâng trạng thái khi đạt toàn bộ cổng, không dựa vào điểm tổng.

## 3. Attribution entry-fill D2

Telemetry schema `P0-D0.2` phân loại theo fill thực tế:

- `MarketOnly`: market tranche khớp, limit không khớp;
- `MarketPlusLimit`: cả market và limit đều khớp;
- `LimitOnly`: chỉ limit khớp;
- `NoLimitPlanned`: plan không có limit;
- `NoFills`: chưa có tranche nào khớp.

Mỗi nhóm ghi: số lệnh, win rate, net expectancy, gross expectancy, friction, tỷ lệ risk đã khớp; đồng thời phân rã `setup × fill-state` để tránh kết luận sai do trộn setup.

> **Cảnh báo diễn giải:** đây là attribution quan sát, không phải causal counterfactual. `MarketPlusLimit` xảy ra trên đường giá đã hồi/ngược tới limit, còn `MarketOnly` thường là đường giá chạy ngay. Khác biệt outcome giữa hai nhóm có thể do đường giá, không phải do loại lệnh.

<!-- D2_FULL_RESULTS_START -->
Run `#43`, full 2020–2026, conservative fill:

- parity với run #40: **PASS tuyệt đối** — 344 lệnh, win `37,2093%`, net `−0,10265R`, DD `11,0426R`;
- decision fingerprint: `8f7cbd1e64c2b2a103de5d4c900c90bc594c20d3f125ab681bac12bbfc17ab3c`;
- trade fingerprint: `55f6dd3a6cb42f93d86efe2b3cefc4813cea3b37aa66e3c1e6b430d17ca8a45e`;
- market fill `344/344 = 100%`; limit fill `169/344 = 49,1%`; limit expiry `153/344 = 44,5%`; 22 limit còn lại bị hủy do trade đóng trước;
- maker fee share chỉ `15,8%` khi tính cả entry/exit fills.

| Fill state | Lệnh | Win rate | Net expectancy | Gross expectancy | Friction | Risk đã khớp |
|---|---:|---:|---:|---:|---:|---:|
| MarketOnly | 175 | **51,43%** | **+0,276R** | +0,408R | 0,132R | 58,5% |
| MarketPlusLimit | 169 | **22,49%** | **−0,495R** | −0,315R | 0,180R | 100,0% |

| Setup × fill state | Lệnh | Win rate | Net | Gross | Friction |
|---|---:|---:|---:|---:|---:|
| RangeRejection · MarketOnly | 93 | **52,69%** | **+0,381R** | +0,510R | 0,128R |
| RangeRejection · MarketPlusLimit | 101 | 23,76% | **−0,384R** | −0,198R | 0,185R |
| StrongTrend · MarketOnly | 56 | **57,14%** | **+0,323R** | +0,436R | 0,112R |
| StrongTrend · MarketPlusLimit | 45 | 24,44% | **−0,587R** | −0,431R | 0,156R |
| TrendPullback · MarketOnly | 26 | 34,62% | **−0,202R** | −0,014R | 0,188R |
| TrendPullback · MarketPlusLimit | 23 | 13,04% | **−0,802R** | −0,599R | 0,204R |

**Diễn giải đúng:** limit fill là marker rất mạnh của một đường giá bất lợi. Hiệu ứng xuất hiện ở cả ba setup nên không phải Simpson's paradox do trộn setup. Tuy nhiên chênh lệch `MarketOnly − MarketPlusLimit` vẫn không đo tác động của limit: hai cohort không đi qua cùng đường giá, và cohort full-fill nhận đủ 100% risk trong khi MarketOnly trung bình chỉ khớp 58,5%. Đây là bằng chứng ưu tiên xây paired execution lab và adverse-fill cancellation, không phải bằng chứng đủ để chuyển toàn bộ sang market-only.

Run `#44`, legacy OOS already observed 2022–2023, conservative fill:

- parity với #41: **PASS tuyệt đối** — 87 lệnh, win `25,2874%`, net `−0,34712R`, DD `4,1330R`;
- decision fingerprint `e7e22b6fa71834c74eaf7d45c5c6c683cec80003aa682607390d45ea3099da71`;
- trade fingerprint `8a4c6add0474d40a0f81803b87cce0ba3eedd891040e8e514a792f6b60ea8252`;
- limit fill `47/87 = 54,0%`, expiry `34/87 = 39,1%`.

| Fill state | Lệnh | Win rate | Net | Gross | Friction | Risk đã khớp |
|---|---:|---:|---:|---:|---:|---:|
| MarketOnly | 40 | **40,0%** | **+0,003R** | +0,132R | 0,130R | 58,3% |
| MarketPlusLimit | 47 | **12,8%** | **−0,645R** | −0,457R | 0,187R | 100,0% |

Cross-breakdown cũng cùng chiều: Range `+0,084R` so với `−0,530R`; StrongTrend `−0,174R` so với `−0,847R`; TrendPullback `−0,049R` so với `−0,741R`. Tất cả cell đều dưới 30 lệnh nên chỉ là diagnosis.

**Hệ quả cho V4:** adverse-path selection lặp lại ở giai đoạn xấu, nhưng cohort MarketOnly chỉ gần hòa vốn. Execution có thể giảm mức lỗ, song không tự tạo alpha. V4 vẫn phải sửa regime/setup admission và exit; chuyển 100% market mà không paired-test có thể chỉ đổi fill bias lấy taker fee/slippage cao hơn.
<!-- D2_FULL_RESULTS_END -->

## 4. Nguyên nhân hệ thống còn lỗ

### 4.1 Edge chọn lệnh chưa bền theo regime

V3 có gross edge dương trên full nhưng gross OOS 2022–2023 âm. Đây không còn là bài toán chỉ giảm phí: trong giai đoạn đó, bản thân tập tín hiệu đã không có edge trước chi phí.

`StrongTrendRunnerV3` có win rate full tốt nhất nhưng OOS cũ lại là nhánh xấu chắc chắn nhất. Một nhãn `TrendUp/TrendDown` cấp ngày hoặc 4h chưa đủ phân biệt:

- trend mới hình thành với expansion khỏe;
- trend già, biến động mở rộng nhưng follow-through yếu;
- cú breakout có volume nhưng là exhaustion;
- chop biến động cao bị gọi nhầm là strong trend.

### 4.2 TrendPullback đang kéo expectancy xuống

Full `TrendPullbackV3` chỉ có 49 lệnh, win rate `24,49%`, expectancy `−0,4839R`, CI `[−0,8195; −0,1482]R`. Dữ liệu chưa đủ để tối ưu tiếp nhánh này nhưng đủ để **không cho nó đóng góp rủi ro live**.

### 4.3 Friction lớn hơn gross edge

Full V3 tạo `+0,0530R` trước phí nhưng trả `0,1556R/lệnh`. Giảm phí có ích, nhưng không thể cứu setup gross âm. V4 phải báo hai câu riêng:

1. tín hiệu có gross edge hay không;
2. policy execution giữ lại bao nhiêu edge sau phí, funding và slippage.

### 4.4 Fixed tranche không phản ánh urgency và adverse selection

V3 dùng cố định 60/40 cho Range/StrongTrend và 50/50 cho TrendPullback, limit hết hạn tối đa 4 nến. Policy này chưa biết:

- breakout đang chạy gấp hay có xác suất retest cao;
- limit fill là pullback lành mạnh hay thesis đang hỏng;
- stop quá gần làm cost trên mỗi R phình to;
- risk thực tế chỉ khớp 50–60% nhưng outcome đang được so với lệnh đầy đủ.

### 4.5 Exit hiện không tận dụng được MFE của loser

Ở 2022–2023, loser vẫn từng đạt MFE trung bình `0,544R`, trong khi winner có MAE trung bình `0,399R`. Hai số này cùng lúc cảnh báo:

- giữ toàn bộ vị thế tới stop đang trả lại một phần excursion thuận lợi;
- dời stop về hòa vốn quá sớm cũng có thể giết winner bình thường.

Do đó V4 phải thử exit theo cặp trên cùng trade path, không áp ngay quy tắc “đạt 0,5R thì BE toàn bộ”.

### 4.6 Dữ liệu 15m không đủ chứng minh fill trong sổ lệnh

OHLCV 15m biết giá đã chạm/xuyên mức, nhưng không biết queue position, khối lượng đứng trước, spread tại thời điểm đặt hoặc fill từng phần. Conservative/optimistic candle model là hai biên cần giữ, không phải thay thế cho dữ liệu order book.

## 5. Kiến trúc V4 đề xuất

### 5.1 Giữ 15m làm execution timeframe, thêm state 1h

- `15m`: trigger, entry, stop, quản trị trade.
- `1h`: trend persistence / expansion / exhaustion state.
- `4h + 1d`: bias và structural context hiện có.

State 1h chỉ dùng các feature lịch sử có thể backtest parity với live: directional efficiency, cấu trúc HH/HL hoặc LH/LL, ATR percentile, close location và relative volume. Không dùng L2 làm hard gate cho tới khi có kho lịch sử L2 tương ứng.

Các state tối thiểu:

| State | Ý nghĩa | Setup được phép |
|---|---|---|
| `TrendExpansion` | 1h/4h cùng chiều, efficiency và volume expansion, chưa exhaustion | StrongTrend |
| `TrendPullbackHealthy` | trend còn nguyên, pullback giảm volume, chưa phá pivot | TrendPullback shadow-only |
| `RangeBalanced` | efficiency thấp, biên ổn định, không volume shock | RangeRejection |
| `HighVolChop` | ATR cao nhưng direction efficiency thấp | Không trade |
| `ExhaustionRisk` | volume/ATR spike nhưng close/follow-through yếu | Không mở mới; quản runner |

### 5.2 Paired execution lab — P0 của V4

Một scorecard đã được chấp nhận tạo đúng một `DecisionEvent`. Từ event đó, simulator chạy ba policy độc lập:

| Variant | Mục đích | Entry |
|---|---|---|
| `MarketOnlyControl` | Tách chất lượng signal khỏi fill limit | 100% market, cùng risk budget |
| `CurrentHybridV3` | Control implementation hiện tại | 60/40 hoặc 50/50 như V3 |
| `AdaptiveHybridV4` | Candidate | tranche theo setup và urgency state |

Quy tắc fairness:

- cùng symbol, timestamp, direction, stop, target, fee/funding model và candle path;
- cùng **planned risk budget**, đồng thời báo riêng `filled risk budget`;
- shadow variant không thay đổi `PositionAlreadyOpen` của lịch admission chính;
- so sánh delta theo cặp trên đúng `DecisionEvent`, không so hai tập trade khác nhau;
- báo `paired net ΔR`, win/loss flip, fill rate, missed winner, avoided loser và CI bootstrap của delta.

Sau khi chọn policy bằng paired lab, phải chạy lại portfolio backtest riêng vì thời gian đóng vị thế khác nhau có thể thay đổi các tín hiệu sau do gate một vị thế/mã.

### 5.3 Adaptive entry policy — candidate, chưa phải tham số đã duyệt

Không tạo thêm một “điểm alpha tổng”. V4 dùng `ExecutionUrgency` chỉ để chọn cách khớp sau khi hard trigger đã pass:

| Urgency | Điều kiện định tính | Candidate entry |
|---|---|---|
| `High` | TrendExpansion, close gần cực trị, follow-through/volume đồng thuận | 50–60% market, 40–50% retest limit |
| `Medium` | trigger đúng nhưng retest có xác suất hợp lý | 30–40% market, phần còn lại 1–2 limit |
| `Low` | RangeBalanced hoặc cost market cao | passive-first; không chase sau expiry nếu trigger không tái xác nhận |
| `Blocked` | HighVolChop, ExhaustionRisk, tin mạnh, discipline veto | không đặt lệnh |

Nếu dùng 2–3 điểm vào:

- tranche sau chỉ được đặt tại reclaim/pivot/volume-supported structure đã biết trước;
- tổng risk tại stop không vượt risk budget ban đầu;
- không thêm tranche sau khi nến đóng phá invalidation hoặc volume mở rộng ngược thesis;
- không đổi stop xa hơn để “cứu” giá vốn;
- expiry phải gắn với setup; hết hạn là hủy, không market-chase tự động.

Đây là scale-in một thesis còn hợp lệ, không phải averaging-down không giới hạn.

### 5.4 Limit-order safety và telemetry live

- Dùng post-only (`GTX`) khi mục tiêu là maker; reject/cancel phải là trạng thái bình thường, không tự chuyển market im lặng.
- Lưu order intent, exchange order id, `maker`, executed quantity, average price, commission, reject/cancel reason và latency.
- Gắn nhãn `AdverseFill` theo cửa sổ 1–3 nến sau fill bằng excursion bất lợi đã đăng ký trước.
- Lưu queue/imbalance ở shadow nếu có L2; chưa dùng hard gate trước khi đủ dữ liệu lịch sử.
- Reconciliation actual fill phải chạy trước mọi kết luận từ live shadow.

Binance USDⓈ-M hỗ trợ LIMIT/MARKET và các TIF gồm GTC/IOC/FOK/GTX/GTD; GTX là post-only. Điều này cho phép policy maker được biểu diễn tường minh thay vì suy ra từ giá limit.

### 5.5 Exit lab

Các variant phải đăng ký trước và chạy paired trên cùng path:

| Variant | Partial | Failure-to-follow-through | Runner |
|---|---|---|---|
| `ExitControlV3` | như V3 | 16 nến, MFE < 0,5R | như V3 |
| `Protect05` | 25% tại +0,5R | 12 nến | stop còn lại chỉ nâng sau close xác nhận; bù đủ cost |
| `Protect05Fast` | 25% tại +0,5R | 8 nến | như trên |
| `Runner50` | 50% tại TP1 cấu trúc | 12 nến | trail pivot; không nới stop |

Chỉ bốn variant này được chạy ở vòng đầu. Không tạo lưới hàng chục tỷ lệ partial/time-stop. Candidate thắng phải cải thiện net expectancy và drawdown; tăng win rate nhưng giảm expectancy là **không đạt**.

### 5.6 Portfolio setup

- `RangeRejectionV4`: active candidate; mục tiêu lời nhanh trong RangeBalanced, không runner mặc định.
- `StrongTrendV4`: active candidate chỉ trong TrendExpansion; partial + runner, có exhaustion exit.
- `TrendPullbackV4`: shadow-only; không góp risk cho portfolio cho tới khi đủ mẫu và CI.
- Các ngày tin mạnh, cuối tuần, daily loss/loss-streak/correlation gates tiếp tục là hard safety; V4 không được override.

## 6. Telemetry V4 bắt buộc

Mỗi `DecisionEvent` và execution variant phải có:

- `StrategyVersion`, `PolicyVersion`, `TrialId`, decision/trade fingerprint;
- setup, regime 1h/4h/1d, volatility bucket, session/day-of-week;
- execution urgency và lý do state;
- planned/filled risk budget, từng tranche intent/fill/expiry;
- maker/taker, fee, funding, slippage, implementation shortfall;
- adverse-fill flag và excursion 1/2/3 nến sau fill;
- MFE/MAE, bars-to-MFE/MAE, exit reason;
- paired delta so với V3 control;
- breakdown `setup × regime × fill-state × year`.

Không dùng win rate riêng lẻ. Báo tối thiểu: trade count, win rate + Wilson CI, gross/net expectancy + bootstrap CI, payoff ratio, profit factor, drawdown, longest loss streak, friction và exposure/risk đã khớp.

## 7. Kế hoạch kiểm định không tự lừa mình

### 7.1 Trạng thái dữ liệu

Kho 2020–2026 đã được nhìn trong quá trình V2/V3. Vì vậy không còn historical holdout thật sự chưa đụng tới. 2022–2023 phải gọi là `legacy OOS already observed`, không tái sử dụng như bằng chứng xác nhận cuối.

### 7.2 Hai tầng kiểm định

1. **Historical robustness:** expanding walk-forward theo năm, có purge/embargo quanh ranh giới; báo phân phối fold, không chỉ tổng full range.
2. **Forward shadow:** tối thiểu 8 tuần và 100 decision event sau khi code V4 đóng băng; đây mới là bằng chứng chưa được nhìn.

### 7.3 Trial registry

Trước mỗi run phải ghi:

- giả thuyết, code hash, parameter set, symbols/date range;
- metric chính và điều kiện loại;
- số thứ tự thử độc lập/tương quan;
- tất cả kết quả, kể cả âm.

Tính PBO/CSCV cho tập candidate và Deflated Sharpe Ratio sau khi tính đến số lần thử. Không chọn candidate bằng metric rồi dùng chính metric đó như kiểm định.

## 8. Điều kiện chấp nhận V4

### P0 — telemetry/parity

- 855 test hiện tại và test V4 pass; build không lỗi.
- Bật/tắt telemetry không đổi hành vi.
- D2 rerun cùng V3 phải khớp trade count, metrics và fingerprint run #40.
- Paired variants dùng chung `DecisionEvent`; không tự tạo tập tín hiệu khác nhau.

### P1 — execution candidate

- `AdaptiveHybridV4 − CurrentHybridV3` paired net delta > 0 và CI 95% lower > 0.
- Kết quả conservative và optimistic cùng dấu; chênh expectancy không quá `0,05R`.
- Không có setup active nào dưới 100 lệnh full; ≥200 là mức mong muốn.
- Average filled risk và missed-winner rate được báo; không dùng fill rate cao như mục tiêu độc lập.

### P2 — portfolio historical

- Full net expectancy > 0 và CI 95% lower > 0.
- Win rate point estimate ≥40%, nhưng không được đánh đổi payoff để đạt con số này.
- Mỗi setup active net expectancy không âm; không setup nào đóng góp >50% tổng lợi nhuận.
- Max drawdown full ≤ `11,04R`; không fold năm nào vượt `1,25×` V3 control cùng fold.
- Friction ≤50% gross expectancy để có margin of safety sau sai số mô phỏng.
- Đa số walk-forward folds dương; fold âm phải có nguyên nhân regime đã định nghĩa trước, không sửa rule hồi tố.

### P3 — forward shadow

- Tối thiểu 8 tuần và 100 decision event sau code freeze.
- Actual maker/taker, fill, commission, slippage reconciliation ≥99% orders.
- Forward net expectancy point estimate dương; CI và sample limitation phải công khai.
- Không safety veto, news/weekend gate hoặc risk budget nào bị bypass.

Chỉ khi **P0–P3 đều đạt** mới lập proposal bật V4 live với risk nhỏ. Không tự động bật từ backtest.

## 9. Các quyết định review

| Đề xuất | Quyết định | Lý do |
|---|---|---|
| Giữ V3 live disabled | **Giữ** | Net full âm, legacy OOS âm rõ |
| Thêm fill-state telemetry | **Thực hiện D2** | Trả lời limit fill/win và setup cross |
| Kết luận limit tốt/xấu từ D2 cohort | **Bác bỏ** | Selection/adverse-fill bias |
| Paired execution lab | **P0 của V4** | Đo causal delta trên cùng decision/path |
| TrendPullback active | **Tắt, shadow-only** | Full CI âm hoàn toàn |
| StrongTrend mọi regime | **Bác bỏ** | Không ổn định 2022–2023 |
| 2–3 điểm vào | **Cho thử có điều kiện** | Chỉ khi thesis còn hợp lệ và tổng risk cố định |
| BE toàn vị thế tại +0,5R | **Không áp ngay** | Winner MAE 0,399R; nguy cơ cắt winner |
| Partial + runner | **Cho vào exit lab** | Phù hợp MFE loser nhưng phải paired-test |
| Thêm indicator để tăng score | **Không ưu tiên** | Chưa chứng minh causal edge, tăng overfit |
| Tối ưu tiếp trên 2022–2023 | **Cấm dùng làm final proof** | Tập đã được nhìn |

## 10. Thứ tự triển khai đề xuất

1. Chốt D2 attribution và parity V3.
2. Xây `DecisionEvent` + paired execution simulator; chưa sửa alpha trigger.
3. Thêm adverse-fill/implementation-shortfall telemetry và trial registry.
4. Chạy execution lab, chọn hoặc bác bỏ `AdaptiveHybridV4`.
5. Thêm state 1h và portfolio setup policy; TrendPullback shadow-only.
6. Chạy exit lab bốn variant đăng ký trước.
7. Chạy historical walk-forward + PBO/DSR.
8. Code freeze, forward shadow 8 tuần/100 events.
9. Chỉ lập rollout proposal nếu toàn bộ §8 đạt.

## 11. Nguồn tham khảo chính

- [Binance USDⓈ-M — New Order](https://developers.binance.com/docs/derivatives/usds-margined-futures/trade/rest-api/New-Order): loại lệnh, TIF và trường bắt buộc của LIMIT/MARKET.
- [Binance USDⓈ-M — Common definitions](https://developers.binance.com/docs/derivatives/usds-margined-futures/common-definition): GTX là Good Till Crossing/Post Only.
- Lehalle & Mounjid, [Limit Order Strategic Placement with Adverse Selection Risk and the Role of Latency](https://arxiv.org/abs/1610.00261): fill probability phải cân bằng với adverse selection và opportunity cost.
- Lalor & Swishchuk, [Market Simulation under Adverse Selection](https://arxiv.org/abs/2409.12721): mô phỏng fill độc lập với price path có thể làm phồng hiệu quả; cần tách adverse/non-adverse fills.
- Lokin & Yu, [Fill Probabilities in a Limit Order Book with State-Dependent Stochastic Order Flows](https://arxiv.org/abs/2403.02572): fill probability phụ thuộc trạng thái queue/order flow và độ sâu mức giá.
- Bailey et al., [The Probability of Backtest Overfitting](https://www.davidhbailey.com/dhbpapers/backtest-prob.pdf): đánh giá xác suất chọn nhầm candidate do thử nhiều chiến lược.
- Bailey & López de Prado, [The Deflated Sharpe Ratio](https://www.davidhbailey.com/dhbpapers/deflated-sharpe.pdf): hiệu chỉnh selection bias, multiple testing và non-normality.

## 12. Phán quyết cuối

V4 **đáng triển khai như một research/shadow version**, vì failure mode của V3 đã đủ rõ để đặt giả thuyết kiểm chứng. V4 **chưa phải bản nâng cấp live** và không có lời hứa rằng win rate sẽ tăng. Tiêu chuẩn thành công là lợi thế ròng dương, bền theo thời gian và giữ drawdown — win rate chỉ là một metric phụ có cổng tối thiểu.
