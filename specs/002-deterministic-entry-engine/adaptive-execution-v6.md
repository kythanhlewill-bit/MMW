# Adaptive Execution V6 — setup-specific sideways capture

Ngày chốt thiết kế: 2026-08-07  
Control bắt buộc: `TriggerFirstV3` và `CalibratedV5` chạy cùng binary, cùng dữ liệu, cùng fill model.  
Trạng thái: implementation candidate; chưa được phép suy diễn kết quả backtest thành hiệu quả live.

## 1. Mục tiêu

V6 không nới trigger trend để lấy thêm lệnh. V6 giữ chuỗi xác nhận BOS–retest–reclaim của V3,
đồng thời bổ sung hai họ cơ hội mà V3 chưa mô hình hoá:

1. `RectangleRangeFade`: hồi quy từ biên của một hình chữ nhật có cấu trúc.
2. `TriangleBreakout` / `RectangleBreakout`: thoát khỏi vùng co hẹp, trực tiếp hoặc breakout–retest.

Sideway nhiễu không có biên ổn định vẫn là `NoTradeChop`. Mục tiêu là tăng số cơ hội có định nghĩa,
không biến mọi cây nến đi ngang thành tín hiệu.

## 2. Những điều V6 sửa

- V3 chỉ cho Range khi cùng một nến vừa sweep vừa rejection. V6 cho phép sweep ở nến trước và
  confirmation ở nến sau trong cửa sổ cấu hình được.
- V3 chặn giữa range trước khi trigger được đánh giá. V6 cho hai chiều đi tới detector khi regime
  là Range; detector tự quyết định fade tại biên hay breakout theo chiều thực tế.
- Telemetry V3 đếm mọi lần quét nến. V6 ghi thêm stage và event id để phân biệt mật độ quét với
  conversion của setup thật.
- Điểm tổng cũ không được dùng như xác suất thắng. V6 có `SetupQualityScore` riêng theo playbook,
  và position size luôn bị cap theo loại setup.
- Stop/target xuất phát từ invalidation của pattern, không dịch tuỳ ý theo điểm.

## 3. Phiên bản và control

### 3.1 `CalibratedV5`

V5 chạy đúng trigger và execution V3, sau đó chỉ admission khi:

```text
TriggerState == Confirmed
SetupType != TrendPullback
ExhaustionCount <= 1
Entry day != Sunday UTC
```

`ExhaustionCount` là số tiêu chí đạt điểm tối đa trong ba tiêu chí đã ghi ở V5:
`technical.htf_alignment`, `technical.momentum`, `market.volatility_regime`.

### 3.2 `AdaptiveSidewaysV6`

V6 giữ admission chống exhaustion/Sunday của V5 cho setup trend. Các setup sideway mới dùng
quality score và economics riêng; không bị loại chỉ vì không có BOS.

## 4. Nhận diện pattern không nhìn trước

Mọi detector chỉ nhận nến đã đóng tại `CandleCloseTimeUtc`. Với một trigger ở nến `t`, hình học
pattern được dựng từ các nến kết thúc trước nến breakout/sweep; nến tương lai không được dùng để
xác nhận pivot hoặc fit đường biên.

### 4.1 Rectangle

Trong cửa sổ mặc định 32 nến M15:

- dựng biên dưới/trên bằng vùng phân vị robust của low/high;
- chiều rộng phải nằm trong dải ATR cấu hình được;
- tối thiểu hai lần chạm mỗi biên;
- phần lớn close phải nằm trong biên có tolerance;
- hai biên không được trôi quá nhanh; nếu co hội tụ thì chuyển sang candidate Triangle.

### 4.2 Triangle

- tối thiểu hai pivot high và hai pivot low đã xác nhận;
- đường high giảm và/hoặc đường low tăng;
- khoảng cách hai đường tại cuối pattern nhỏ hơn rõ rệt so với đầu pattern;
- hai đường chưa cắt nhau trước nến trigger;
- close phần lớn nằm trong envelope.

Tam giác đối xứng không có bias cố định. Chiều chỉ được chọn sau khi nến breakout đóng.

## 5. Trigger theo playbook

### 5.1 `RectangleRangeFade`

Long tại biên dưới, Short tại biên trên:

1. Có Rectangle hợp lệ.
2. Sweep xảy ra trong tối đa `V6RangeSweepLookbackBars` nến.
3. Một nến directional đóng trở lại range; sweep và confirmation có thể là cùng nến.
4. Body, close-location và relative volume đạt sàn cấu hình.
5. Đây là confirmation đầu tiên của event; các nến kế tiếp không sinh event mới.

Stop nằm ngoài cực trị sweep cộng buffer ATR. TP1 là mid-range; runner là biên đối diện. Nếu room
ròng tới TP1 không trả được chi phí theo ngưỡng Range thì không vào.

### 5.2 `TriangleBreakout` và `RectangleBreakout`

Hai đường xác nhận:

- Direct: nến hiện tại đóng ngoài biên có buffer, body và relative volume đủ mạnh.
- Retest: breakout đã xảy ra trong cửa sổ fresh; giá chạm lại biên rồi đóng giữ được đúng chiều.

Stop nằm phía trong pattern sau vùng retest/invalidation. TP1 theo R tối thiểu; runner dùng chiều
cao pattern hoặc mức thanh khoản cấu trúc gần nhất, chọn mức hợp lệ và xa hơn TP1.

### 5.3 Trend

Giữ nguyên BOS–retest–reclaim của V3. Không hạ ngưỡng BOS để bù số lệnh.

## 6. Setup quality và sizing

`SetupQualityScore` nằm trong `[0,100]`, được tính riêng theo playbook từ các thành phần không cộng
trùng ý nghĩa:

| Playbook | Thành phần chính |
|---|---|
| Range Fade | hình học/touches, edge+sweep, confirmation, volume, room |
| Compression Breakout | hình học/co hẹp, breakout close, impulse volume, retest, room |
| Trend | trigger V3 và điểm bối cảnh hiện hữu; sizing vẫn bị cap |

Công thức:

```text
FinalSizeR = BaseSizeR
           × SetupRiskCap
           × SetupQualityMultiplier
           × DayRisk
           × Discipline
           × AI-reduction-only
           × DataCoverage
```

Giá trị khởi tạo là giả thuyết phải backtest:

| Setup | Risk cap |
|---|---:|
| RectangleRangeFade | 0,60 |
| Triangle/RectangleBreakout | 0,70 |
| Trend | 1,00 |

Quality dưới 60 không được xác nhận. Các bậc 60–69, 70–84, 85–100 lần lượt nhân 0,50 / 0,75 /
1,00. Chỉ được giữ bảng này nếu expectancy theo bucket không đảo chiều nghiêm trọng; không được gọi
đây là xác suất thắng.

## 7. Execution và exit

| Setup | Entry | TP1 | Runner | Quản trị |
|---|---|---|---|---|
| Range Fade | market confirmation + limit tại biên | mid-range | biên đối diện | chốt 60%, BE có phí, expiry ngắn |
| Compression Breakout | market breakout/reclaim + limit retest | tối thiểu 1,2R | measured move/structure | chốt động, trail pivot |
| Trend | giữ V3 | 1,5R/structure | structure hoặc 3R | giữ V3 |

Range không dùng target/SL của trend. Điểm cao chỉ tăng size trong cap hoặc phần runner; không nới
stop ra khỏi invalidation.

## 8. Telemetry funnel theo event

Mỗi scorecard ghi:

- `SetupStage`: `NotEligible`, `EligibleContext`, `StructureCandidate`, `TriggerStarted`, `Confirmed`;
- `SetupEventId`: ổn định theo symbol, setup, pattern start và biên đã lượng tử hoá;
- `SetupQualityScore` và `SetupSizeMultiplier`.

Báo cáo phải có cả hai mẫu số:

```text
scan conversion  = confirmed decisions / all 15m decisions
event conversion = distinct confirmed event ids / distinct candidate event ids
```

Một range tồn tại 24 nến không được hiểu thành 23 setup thất bại và một setup thành công.

## 9. Economics và veto

- Range Fade dùng net first-target R:R tối thiểu riêng, mặc định 1,00.
- Compression Breakout mặc định 1,30.
- Trend giữ 1,50 của V3.
- Cost/target vẫn là gate cứng, với trần riêng theo setup nếu cần.
- Tin mạnh, thiếu dữ liệu, stop không hợp lệ, position/concentration và discipline vẫn là veto cứng.

## 10. Backtest và điều kiện nghiệm thu

Chạy `2020-01-01` tới ngày cuối kho, BTCUSDT + ETHUSDT, conservative fill; sau đó optimistic fill
như sensitivity. V3, V5 và V6 phải dùng cùng binary và cùng dữ liệu.

V6 chỉ là candidate tốt hơn control khi:

1. Determinism/parity tests qua; không có look-ahead.
2. Mỗi setup mới có báo cáo riêng về trades, win rate, expectancy, CI 95%, exit reason và fill state.
3. Không kết luận từ setup có dưới 100 lệnh; setup đó tiếp tục shadow.
4. Full net expectancy > 0; CI lower > 0 mới được coi là bằng chứng mạnh.
5. Không năm nào đóng góp quá 50% tổng net R và không chỉ sống trên một symbol.
6. Max drawdown và longest loss streak không xấu đi quá 25% so với control nếu lợi nhuận tăng.
7. Event funnel chứng minh số lệnh tăng từ setup mới, không phải do vô tình nới trigger trend.
8. Ghi số thứ tự thử tham số và fingerprint của từng run.

Không bật live tự động sau backtest. V6 phải chạy forward shadow trước với risk bằng 0.
