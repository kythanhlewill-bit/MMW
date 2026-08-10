# Adaptive Execution V6 — kết quả triển khai và backtest R0

Ngày đánh giá: 2026-08-07  
Trạng thái: **triển khai xong, không đạt cổng backtest để bật live**

## 1. Phạm vi đã triển khai

V6 tách setup sideway khỏi playbook trend thay vì nới lỏng BOS–retest–reclaim:

- Nhận diện rectangle bằng biên robust, số lần chạm, containment và độ dốc.
- Nhận diện triangle bằng pivot đã xác nhận, hồi quy hai biên và kiểm tra co hẹp.
- Thêm `RectangleRangeFade`, `RectangleBreakout`, `TriangleBreakout`.
- Range Fade cho phép sweep ở nến trước và xác nhận ở nến kế tiếp.
- Breakout yêu cầu close/volume và hỗ trợ breakout–retest theo thời hạn.
- Chấm `SetupQualityScore` riêng và giới hạn size theo setup; setup sideway nhỏ hơn trend.
- Kế hoạch entry/TP/runner riêng cho Range Fade và Compression Breakout.
- Admission V5 được đưa vào runtime: bỏ Trend Pullback, trend exhaustion từ 2 trở lên và trend ngày Chủ nhật.
- Telemetry `P0-D0.3` ghi stage, event id, setup quality, fill state và `setup × fill-state`.
- Thêm migration `AddAdaptiveSidewaysV6` và test chống look-ahead/duplicate event.

Các thành phần chính:

- `SidewaysPatternAnalyzer`: detector rectangle/triangle không nhìn trước.
- `SetupTriggerPolicy`: state machine theo từng playbook.
- `StrategyAdmissionPolicy`: admission runtime theo version/setup.
- `ScoreBasedPositionSizer`: setup cap và quality multiplier.
- `TradeExecutionPlanner`: tranche, target và runner riêng theo setup.
- `SignalEvalService`: đánh giá hai chiều trong range rồi chọn chiều có trigger thực sự.

## 2. Xác minh kỹ thuật

| Hạng mục | Kết quả |
|---|---:|
| Unit/integration tests | **865 passed, 0 failed** |
| `dotnet build MMW.sln --no-restore` | **Passed** |
| Test detector không look-ahead | Passed |
| Test xác nhận range qua hai nến | Passed |
| Test không phát trùng event | Passed |
| Test sizing và execution theo setup | Passed |

Build còn cảnh báo bảo mật NuGet `NU1903` của `AutoMapper 14.0.0`; đây là dependency đã có trước V6, không phải lỗi biên dịch V6.

## 3. Thiết kế phép thử

- Kho dữ liệu 15 phút: BTCUSDT và ETHUSDT.
- Khoảng: `2020-01-01T00:00:00Z` đến `2026-08-04T23:15:00Z`.
- Account: `BACK TEST - REAL` (`TradingAccountId = 3`).
- V3, runtime V5 và V6 chạy cùng binary, dữ liệu, phí và mô hình conservative.
- Telemetry schema: `P0-D0.3`.
- Conservative #50 ghi `ComparableTrialNumber = 1`; sensitivity #51 ghi số 4 sau khi ba control/candidate cùng phạm vi đã tồn tại. Run id, snapshot và fingerprint là khóa đối chiếu chính.

## 4. Kết quả control và candidate

| Run | Version | Lệnh | Win rate | Gross exp. | Net exp. | 95% CI net exp. | Max DD | Loss streak |
|---:|---|---:|---:|---:|---:|---:|---:|---:|
| #49 | V3 control | 344 | 37,21% | +0,0530R | **−0,1026R** | [−0,2543; +0,0490]R | 11,0426R | 18 |
| #48 | V5 runtime control | 239 | 43,10% | +0,2331R | **+0,0847R** | [−0,1079; +0,2772]R | 3,3466R | 14 |
| #50 | V6 R0 conservative | 382 | 46,34% | +0,0807R | **−0,1064R** | [−0,2430; +0,0303]R | 11,1142R | 8 |
| #51 | V6 R0 optimistic | 382 | 46,34% | +0,0807R | **−0,1064R** | [−0,2430; +0,0303]R | 11,1142R | 8 |

Fingerprint:

| Run | Trade fingerprint |
|---:|---|
| #49 V3 | `55f6dd3a6cb42f93d86efe2b3cefc4813cea3b37aa66e3c1e6b430d17ca8a45e` |
| #48 V5 | `d5f9f65a25fa2fb2c493de02f758ea37c34284497dfc967549e3287315aedf2b` |
| #50 V6 | `ccaf2905bd95d66e2a9565f641c5d6650f8dd10b096f8a70827af13185656ade` |
| #51 V6 optimistic | `ccaf2905bd95d66e2a9565f641c5d6650f8dd10b096f8a70827af13185656ade` |

V3 #49 khớp tuyệt đối fingerprint của control V3 trước đó. Vì vậy việc thêm telemetry/V6 không làm thay đổi hành vi của V3.

V5 runtime không bằng cohort V5 lọc hậu nghiệm trong tài liệu V5. Khi loại một lệnh ngay trong runtime, trạng thái position, max-trades, loss streak và những cơ hội phía sau thay đổi; do đó post-filter không phải là backtest parity hợp lệ. Từ đây phải dùng #48 làm control V5 thực thi thật.

### 4.1 Sensitivity của mô hình fill

Snapshot trong DB xác nhận:

- #50: `BacktestLimitFillRequiresThrough = true` (conservative).
- #51: `BacktestLimitFillRequiresThrough = false` (optimistic/touch).

Hai run cho kết quả, trade fingerprint và cả CSV giống tuyệt đối. SHA-256 của hai CSV đều là:

`75B5FADA65F174662DCAF34350C8B4C6A5AD0C13B15DC041B3055A269084BB5F`

Điều này cho thấy trong tập lệnh R0 không có trường hợp “chỉ chạm nhưng không xuyên mức” làm thay đổi đường thực thi. Nó không chứng minh khả năng đứng đầu queue ngoài thị trường thật; cả hai vẫn là mô hình candle OHLC, không có order-book replay.

## 5. V6 theo setup

| Setup | Lệnh | Win rate | Net exp. | 95% CI | Đủ 100 lệnh? |
|---|---:|---:|---:|---:|---:|
| Rectangle Breakout | 106 | 44,34% | **−0,1963R** | [−0,4483; +0,0557]R | Có |
| Rectangle Range Fade | 33 | 51,52% | **−0,3406R** | [−0,6504; −0,0308]R | Không |
| Strong Trend Breakout | 53 | 54,72% | **+0,2397R** | [−0,0754; +0,5549]R | Không |
| Triangle Breakout | 190 | 44,21% | **−0,1121R** | [−0,3233; +0,0991]R | Có |

Hai setup có đủ 100 lệnh đều chưa chứng minh expectancy dương. Range Fade có mẫu nhỏ và CI âm; Strong Trend dương nhưng mới 53 lệnh nên chỉ được coi là tín hiệu shadow.

## 6. Phát hiện lớn nhất: adverse selection ở chân limit

Đây là attribution quan sát, **chưa phải counterfactual nhân quả**. Tuy vậy dấu hiệu nhất quán trên cả bốn setup:

| Setup | Fill state | Lệnh | Win rate | Net exp. | Tổng net R |
|---|---|---:|---:|---:|---:|
| Rectangle Breakout | Market only | 65 | 67,69% | **+0,28R** | +18,03R |
| Rectangle Breakout | Market + limit | 41 | 7,32% | **−0,95R** | −38,84R |
| Rectangle Range Fade | Market only | 17 | 82,35% | **+0,07R** | +1,16R |
| Rectangle Range Fade | Market + limit | 16 | 18,75% | **−0,77R** | −12,40R |
| Strong Trend Breakout | Market only | 34 | 67,65% | **+0,66R** | +22,44R |
| Strong Trend Breakout | Market + limit | 19 | 31,58% | **−0,51R** | −9,73R |
| Triangle Breakout | Market only | 117 | 64,10% | **+0,25R** | +29,44R |
| Triangle Breakout | Market + limit | 73 | 12,33% | **−0,69R** | −50,73R |

Tổng hợp:

- Market only: 233 lệnh, thắng 66,95%, net **+0,305R/lệnh**.
- Market + limit: 149 lệnh, thắng 14,09%, net **−0,750R/lệnh**.
- Limit khớp 149/382 chân, tương đương 39,0%; 154 chân hết hạn.
- Friction trung bình 0,1870R/lệnh, tổng khoảng 71,45R.

Diễn giải thực chiến: limit thường chỉ được chạm khi giá quay ngược đủ sâu. Trong mẫu R0, điều này đang chọn đúng những đường giá có động lượng bất lợi; phần thêm size nhận đủ rủi ro trong khi xác suất stop tăng mạnh. Không nên kết luận “limit gây lỗ” chỉ từ phép chia nhóm này, vì nhóm market-only và market-plus-limit vốn có đường giá khác nhau. Bước kế tiếp phải là một run paired/counterfactual giữ nguyên signal nhưng tắt tranche limit.

## 7. Độ bền theo năm và symbol

| Năm | Lệnh | Win rate | Net exp. | Tổng net R |
|---:|---:|---:|---:|---:|
| 2020 | 61 | 52,46% | −0,01R | −0,81R |
| 2021 | 123 | 49,59% | +0,08R | +9,52R |
| 2022 | 68 | 35,29% | **−0,49R** | −33,24R |
| 2023 | 14 | 50,00% | −0,07R | −1,04R |
| 2024 | 69 | 42,03% | **−0,21R** | −14,45R |
| 2025 | 35 | 54,29% | +0,10R | +3,66R |
| 2026 | 12 | 41,67% | −0,36R | −4,28R |

| Symbol | Lệnh | Win rate | Net exp. | Tổng net R |
|---|---:|---:|---:|---:|
| BTCUSDT | 125 | 42,40% | −0,19R | −23,16R |
| ETHUSDT | 257 | 48,25% | −0,07R | −17,48R |

V6 không chỉ lỗ trên một symbol; cả BTC và ETH đều âm. Năm 2022 là điểm gãy lớn nhất, nhưng loại riêng năm xấu sau khi xem kết quả sẽ là overfit và không được phép.

## 8. Funnel theo event và exit

- 462.330 lượt quyết định.
- 80.572 distinct candidate events.
- 3.259 distinct confirmed events.
- 382 distinct entered events.
- 329/382 lệnh (86,1%) đến từ ba setup sideway mới; trend còn 53 lệnh.

Như vậy số lệnh tăng do setup sideway mới, không phải do vô tình nới trigger trend. Tuy nhiên chỉ thêm số lệnh chưa tạo edge dương.

| Exit reason | Lệnh | Net exp. | Tổng net R |
|---|---:|---:|---:|
| Target | 79 | +1,75R | +138,58R |
| Stop | 286 | −0,62R | −177,36R |
| Time stop | 17 | −0,11R | −1,86R |

## 9. Đối chiếu điều kiện nghiệm thu

| Điều kiện | Kết quả |
|---|---|
| Determinism/parity, không look-ahead | **Đạt** |
| Báo cáo riêng theo setup/fill/exit/CI | **Đạt** |
| Conservative và optimistic sensitivity | **Đạt phép đo**; hai kết quả giống tuyệt đối và cùng âm |
| Không kết luận setup dưới 100 lệnh | **Đạt quy trình**; Range Fade và Strong Trend tiếp tục shadow |
| Full net expectancy > 0 | **Không đạt** |
| CI lower của expectancy > 0 | **Không đạt** |
| Bền theo năm và symbol | **Không đạt** |
| DD/loss streak không xấu >25% so V3 | **Đạt**; DD gần ngang, streak giảm |
| Số lệnh tăng từ setup mới | **Đạt** |
| Trial number và fingerprint | **Đạt** |

## 10. Quyết định và bước tiếp theo

**V6 R0 bị loại khỏi live.** Win rate tăng từ 37,21% của V3 lên 46,34%, nhưng net expectancy vẫn âm và gần như không đổi theo hướng tốt (`−0,1064R` so với `−0,1026R`). Đây là ví dụ rõ rằng tối ưu win rate riêng lẻ không đồng nghĩa tối ưu lợi nhuận.

Không chỉnh ngưỡng detector bằng chính full range này. Thử nghiệm kế tiếp nên là **V6 R1 execution lab**:

1. Giữ nguyên toàn bộ signal/event của #50.
2. Tắt chân limit, hoặc chỉ cho add-on sau reclaim mới thay vì khi giá retrace chạm limit.
3. So paired theo cùng event id để đo tác động nhân quả lên R, không chỉ chia nhóm quan sát.
4. Chạy development/OOS tách biệt; chỉ khi conservative expectancy dương mới xét forward shadow risk 0.

File CSV:

- Conservative: `src/MMW.Web/.artifacts/backtests/v6-r0-candidate.csv`.
- Optimistic: `src/MMW.Web/.artifacts/backtests/v6-r0-candidate-optimistic.csv`.
