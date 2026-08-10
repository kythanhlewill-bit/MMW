# Adaptive Execution V3 — P0 telemetry, parity và kết quả backtest

Ngày chốt số liệu: **2026-08-04**  
Phạm vi chính: **BTCUSDT, ETHUSDT · 2020-01-01 → 2026-08-04 05:15 UTC**  
Kết luận rollout: **KHÔNG bật V3 cho live**. V3 giảm lỗ và drawdown rất mạnh trên full range, nhưng expectancy OOS vẫn âm có ý nghĩa thống kê.

## 1. Những gì đã triển khai

### P0 telemetry/parity

- Gắn `TradingStrategyVersion`, `SetupType`, `SetupTriggerState` vào scorecard và backtest run.
- Ghi decision/trade fingerprint SHA-256 để hai lần chạy chỉ được coi là cùng hành vi khi hash khớp.
- Đo gross expectancy, friction, MFE/MAE, score band, setup, trigger, criterion-point, cost/stop-distance decile và cross-breakdown `trigger-confirmed × veto`.
- Telemetry là observer một chiều: chỉ đọc card/plan/trade, không trả dữ liệu vào quyết định.
- Lưu aggregate vào `BacktestRuns.DiagnosticsJson`; không INSERT hàng trăm nghìn scorecard cho full run.
- CLI hỗ trợ `--version v2|v3`, `--fill conservative|optimistic`, `--telemetry true|false`.

### Trigger-First, Cost-Aware V3

- `RangeRejection`: phải sweep biên, đóng lại trong range, thân nến và relative volume đạt ngưỡng.
- `TrendPullback`/`StrongTrendBreakout`: phải có BOS thuận chiều, retest/reclaim còn mới, impulse đủ thân/volume và pullback không nở volume.
- Trigger đã xác nhận được phép dùng score bối cảnh thấp với size thấp; không cho score chung thay thế trigger.
- Range sweep được quyền thay veto `NotAtRangeEdge`, vì close đã reclaim khỏi biên.
- `InsufficientRoom` chỉ được nhường cho economics gate khi vẫn dựng được stop và target hợp lệ. Không dựng được mức vẫn bị chặn.
- Economics gate tính theo quantity thật của từng tranche; yêu cầu net R:R và cost/target sau fee/slippage.
- Entry theo tranche: Range `60% market + 40% limit`, TrendPullback `50/50`, StrongTrend `60/40`. Tổng risk weight luôn bằng 1.
- V2 vẫn là mặc định trong DB; CLI thử V3 bằng override transient, không thay cấu hình live.

## 2. P0 parity

### Test tự động

- `855/855` test pass sau khi bổ sung entry-fill attribution D2.
- Test parity riêng xác nhận bật/tắt telemetry không đổi trade count, win rate, expectancy, drawdown, fee, funding, slippage, mode và exit reason.
- Fingerprint ổn định khi chạy lại cùng dữ liệu/cấu hình.

### A/B trên DB thật — 2025-01

| Run | Telemetry | Load setting | Lệnh | Win rate | Expectancy | Drawdown | Fee R | Funding R | Slippage R |
|---:|---|---|---:|---:|---:|---:|---:|---:|---:|
| #32 | Tắt | override | 70 | 31,4286% | −0,2943R | 3,2416R | 16,19269788 | −0,32553602 | 6,13115066 |
| #33 | Tắt | legacy | 70 | 31,4286% | −0,2943R | 3,2416R | 16,19269788 | −0,32553602 | 6,13115066 |
| #34 | Bật | legacy | 70 | 31,4286% | −0,2943R | 3,2416R | 16,19269788 | −0,32553602 | 6,13115066 |

Kết luận: **P0 parity đạt** trên cả test cô lập và DB thật.

Run #29 cũ không có fingerprint. Run P0 full #31 có 4.894 lệnh, trong khi #29 có 4.799 lệnh dù snapshot tham số V2 giống nhau. Vì A/B #32/#33/#34 trùng tuyệt đối, chênh lệch này không do telemetry; không đủ dữ liệu để quy nguyên nhân cho binary/data drift trước khi fingerprint tồn tại. Từ P0 trở đi, kết quả không có fingerprint không được dùng làm chuẩn bit-for-bit.

Fingerprint chuẩn P0 tháng 2025-01 (#34):

- decision: `1e4dde7c22d55c0e9a5fd4a6b5cd69451129d17c79c1e321689bfa390d717fef`
- trade: `bdeabbc8478841baae720688233146f8f24d77c4db75d90c02b4f406e3d91126`

## 3. So sánh full range

So sánh công bằng dùng V2 P0 #31 và V3 #40 trên cùng dữ liệu, cùng phí, cùng fill model conservative.

| Chỉ số | V2 P0 #31 | V3 #40 | Thay đổi |
|---|---:|---:|---:|
| Số lệnh | 4.894 | 344 | −93,0% |
| Win rate | 29,0356% | **37,2093%** | **+8,17 điểm %** |
| Gross expectancy | −0,0129R | **+0,0530R** | +0,0659R |
| Net expectancy | −0,3144R | **−0,1026R** | **+0,2118R** |
| CI 95% expectancy | [−0,3643; −0,2645] | **[−0,2543; +0,0490]** | V3 chưa khác 0 rõ ràng |
| Max drawdown | 205,3886R | **11,0426R** | **−94,6%** |
| Friction trung bình | 0,3015R/lệnh | **0,1556R/lệnh** | −48,4% |

V3 đã biến gross expectancy từ âm sang dương trên full range, nhưng friction vẫn lớn hơn edge gross nên net còn âm.

### Theo setup — V3 full #40

| Setup | Lệnh | Win rate | Expectancy | CI 95% expectancy | Đủ 100 lệnh? |
|---|---:|---:|---:|---:|---|
| RangeRejectionV3 | 194 | 37,63% | **−0,0171R** | [−0,2413; +0,2072] | Có |
| StrongTrendRunnerV3 | 101 | **42,57%** | −0,0821R | [−0,3119; +0,1478] | Có |
| TrendPullbackV3 | 49 | 24,49% | **−0,4839R** | **[−0,8195; −0,1482]** | Không |

TrendPullback là nhánh yếu rõ nhất trên full range. Tuy nhiên không được xóa nhánh rồi tuyên bố kết quả mới là OOS, vì full range đã được nhìn và dùng để chọn thay đổi.

### Telemetry nguyên nhân — V3 full #40

- Trigger-confirmed: 1.864 lượt; 344 vào lệnh, 1.520 bị veto.
- Veto sau trigger: `InsufficientRoom=567`, `ExecutionCostTooHigh=406`, `HtfMisaligned=289`, `PositionAlreadyOpen=228`, các discipline/data veto còn lại nhỏ.
- Trong 406 cost reject: 267 trường hợp stop dưới 50 bps; đây là vùng quantity/cost trên mỗi R tăng mạnh.
- Limit fill rate chỉ **49,1%**, dưới cổng 60%; vì V3 có tranche market nên mọi trade vẫn có fill, nhưng chân average-price thường không khớp.
- MFE/MAE full: trung bình `1,202R / 0,948R`; winner `2,101R / 0,428R`; loser `0,669R / 1,257R`.

Fingerprint V3 full #40:

- decision: `8f7cbd1e64c2b2a103de5d4c900c90bc594c20d3f125ab681bac12bbfc17ab3c`
- trade: `55f6dd3a6cb42f93d86efe2b3cefc4813cea3b37aa66e3c1e6b430d17ca8a45e`

## 4. Out-of-sample 2022–2023

| Chỉ số | V2 #28 conservative | V3 #41 conservative | V3 #42 optimistic |
|---|---:|---:|---:|
| Số lệnh | 1.524 | 87 | 87 |
| Win rate | 30,0525% | **25,2874%** | **25,2874%** |
| Gross expectancy | chưa đo | −0,1862R | −0,1826R |
| Net expectancy | −0,3613R | −0,3471R | −0,3434R |
| CI 95% expectancy | [−0,4490; −0,2736] | **[−0,6267; −0,0676]** | **[−0,6242; −0,0626]** |
| Max drawdown | 76,8690R | 4,1330R | 4,1330R |

Kết luận OOS:

- V3 chỉ cải thiện expectancy khoảng 0,014–0,018R so V2, nhưng vẫn âm và CI 95% hoàn toàn dưới 0.
- Win rate OOS giảm 4,77 điểm phần trăm.
- Conservative và optimistic gần như trùng nhau; thất bại OOS **không do** giả định limit fill.
- Cả ba mode OOS đều dưới 100 lệnh. StrongTrendRunnerV3 xấu nhất về độ chắc chắn: 22 lệnh, expectancy −0,5717R, CI [−0,9620; −0,1815].
- OOS MFE/MAE: trung bình `0,915R / 0,979R`; winner MAE chỉ `0,399R`, loser MFE vẫn `0,544R`. Nhiều lệnh thua từng đi đúng hướng nhưng trả lợi nhuận lại trước stop.

Fingerprint OOS:

- Conservative #41 decision `e7e22b6fa71834c74eaf7d45c5c6c683cec80003aa682607390d45ea3099da71`, trade `8a4c6add0474d40a0f81803b87cce0ba3eedd891040e8e514a792f6b60ea8252`.
- Optimistic #42 có cùng decision fingerprint; trade `4423eb760ed169ffdbc10a64e35d27bcdd57214c554f578c673d78c74bde5f66`.

## 5. Quyết định

| Cổng | Kết quả |
|---|---|
| Telemetry bật/tắt không đổi hành vi | **PASS** |
| Build/test | **PASS — 851/851** |
| Full win rate tốt hơn V2 | **PASS** |
| Full net expectancy > 0 và CI > 0 | **FAIL** |
| OOS net expectancy > 0 và CI > 0 | **FAIL** |
| Mỗi mode ≥100 lệnh | **FAIL** |
| Conservative/optimistic ổn định | **PASS, nhưng cùng âm** |
| Limit tranche fill ≥60% | **FAIL — 49,1% full; 55,2% OOS optimistic** |

**Không bật `TriggerFirstV3` cho live.** Tài khoản tiếp tục giữ `AdaptiveV2`; live trading vẫn chịu cổng an toàn hiện có.

## 6. Việc hợp lý tiếp theo — V3.1, chưa triển khai trong lần chốt này

1. **Tách regime/volatility telemetry theo năm và setup.** StrongTrendRunner tốt tương đối trên full nhưng hỏng nặng ở 2022–2023; cần tìm state chuyển chế độ, không dùng một rule runner cho mọi trend.
2. **Đưa TrendPullback về shadow-only.** Full CI âm hoàn toàn và OOS không cứu được; không cho phát lệnh thật cho tới khi có ít nhất 100 mẫu độc lập.
3. **Thiết kế exit từ path telemetry.** Loser OOS vẫn đạt MFE trung bình 0,544R; thử nghiệm pre-declared gồm partial nhỏ tại 0,5R, stop theo phí sau partial, và failure-to-follow-through time stop ngắn hơn. Không tối ưu trực tiếp trên OOS #41/#42.
4. **Giảm friction mà không làm mất xác nhận.** Thử market tranche nhỏ hơn chỉ ở RangeRejection, giữ limit theo reclaim thay vì biên tuyệt đối; chấp nhận nếu maker share tăng nhưng fill sensitivity không làm đổi dấu expectancy.
5. **Walk-forward có sổ trial.** Mọi thay đổi V3.1 phải ghi trial trước khi chạy, dùng các cửa sổ năm tuần tự; #40/#41/#42 đã bị nhìn nên không còn là holdout chưa đụng tới.

## 7. D2 entry-fill attribution — run #43

Schema telemetry `P0-D0.2` bổ sung hiệu quả theo trạng thái fill và `setup × fill-state`. Run #43 dùng đúng full range/cấu hình conservative của #40 và khớp tuyệt đối 344 lệnh, mọi metric, decision fingerprint và trade fingerprint; telemetry không đổi hành vi.

| Fill state | Lệnh | Win rate | Net expectancy | Gross expectancy | Friction | Risk đã khớp |
|---|---:|---:|---:|---:|---:|---:|
| MarketOnly | 175 | **51,43%** | **+0,276R** | +0,408R | 0,132R | 58,5% |
| MarketPlusLimit | 169 | 22,49% | **−0,495R** | −0,315R | 0,180R | 100,0% |

Theo setup, `MarketOnly` đều tốt hơn cohort full-fill: Range `+0,381R` so với `−0,384R`, StrongTrend `+0,323R` so với `−0,587R`, TrendPullback `−0,202R` so với `−0,802R`.

Đây là **attribution quan sát**, không phải counterfactual. Limit thường khớp khi giá hồi/ngược, nên đường giá tự chọn cohort; ngoài ra cohort full-fill chịu 100% risk còn MarketOnly chỉ khớp trung bình 58,5%. Kết quả đủ để ưu tiên paired execution simulator trong V4, nhưng chưa đủ để kết luận bỏ limit và vào 100% market.

### Legacy OOS D2 — run #44

Run #44 khớp tuyệt đối mọi metric/fingerprint của #41. Trong 2022–2023:

| Fill state | Lệnh | Win rate | Net | Gross | Friction |
|---|---:|---:|---:|---:|---:|
| MarketOnly | 40 | **40,0%** | **+0,003R** | +0,132R | 0,130R |
| MarketPlusLimit | 47 | 12,8% | **−0,645R** | −0,457R | 0,187R |

Pattern adverse-path lặp lại, nhưng MarketOnly chỉ gần hòa vốn. Vì vậy execution là một nguồn làm lỗ nặng hơn, không phải toàn bộ nguyên nhân thiếu edge. V4 phải paired-test execution đồng thời sửa regime/setup và exit; #44 chỉ dùng diagnosis vì 2022–2023 đã được nhìn từ trước.
