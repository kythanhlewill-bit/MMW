# Review Adaptive scoring & execution V2

Ngày review: 2026-08-04  
Tài liệu được review: [adaptive-execution-v2.md](adaptive-execution-v2.md)

## Kết luận

V2 đúng hướng về chi phí, dừng lỗ theo cấu trúc và giảm tín hiệu price action nhiễu, nhưng **chưa
nên triển khai nguyên văn**. Nếu mục tiêu ưu tiên là tăng tỉ lệ thắng bền vững, hai thay đổi quan
trọng nhất còn thiếu là:

1. Tách điều kiện và bộ điểm theo từng setup (`RangeFade`, `TrendPullback`, `StrongTrend`).
2. Thay lệnh limit đặt sẵn bằng state machine `armed → confirmed → retest → filled/expired`.

| Hạng mục | Điểm đánh giá | Nhận xét |
|---|---:|---|
| Ý tưởng tổng thể | 7/10 | Xác định đúng vấn đề chi phí, stop và false positive. |
| Mức sẵn sàng triển khai | 5,5/10 | Còn mâu thuẫn với code, một số công thức và giả định chưa đúng. |
| Khả năng tăng win rate nếu giữ nguyên | Chưa chắc chắn | Limit mù và score chung có thể tiếp tục tạo lệnh sai bối cảnh. |
| Khả năng tăng win rate sau khi sửa P0 | Khá | Phải kiểm chứng bằng backtest tuần tự và mẫu ngoài giai đoạn đã tối ưu. |

## Bằng chứng từ baseline hiện tại

Backtest run #12 — `Adaptive V1.4 - final evaluation`, BTCUSDT, 2024-01-01 đến 2025-12-31:

| Chỉ số | Kết quả |
|---|---:|
| Số lệnh | 1.324 |
| Win rate | 49,6224% |
| Expectancy sau phí | -0,0404R/lệnh |
| Max drawdown theo đường vốn R | 55,1732R |
| Chuỗi thua dài nhất | 13 |

| Regime | Số lệnh | Win rate | Expectancy |
|---|---:|---:|---:|
| Range | 682 | 52,05% | -0,0423R |
| Trend Up | 312 | 51,60% | -0,0316R |
| Trend Down | 270 | 42,59% | -0,0437R |
| High Volatility | 60 | 43,33% | -0,0505R |

Một số giờ có win rate cao nhưng expectancy vẫn âm:

| Giờ UTC | Số lệnh | Win rate | Expectancy |
|---|---:|---:|---:|
| 08 | 85 | 64,71% | -0,0047R |
| 03 | 40 | 60,00% | -0,0271R |
| 05 | 32 | 59,38% | -0,0370R |
| 14 | 86 | 58,14% | -0,0152R |

Do đó, win rate chỉ được chấp nhận khi expectancy sau toàn bộ chi phí không xấu đi. Không được tăng
win rate bằng cách chốt quá gần hoặc mở stop quá rộng.

## Bảng đánh giá chi tiết

| Ưu tiên | Phần V2 | Đánh giá thực chiến | Thay đổi bắt buộc |
|---|---|---|---|
| P0 | Kiến trúc score chung | Một tổng điểm cho Range Fade và Trend Pullback cho phép các điểm không liên quan bù cho bằng chứng vào lệnh yếu. | Xác định setup trước; áp core gate và score riêng cho từng mode. Score chỉ xếp hạng trong cùng mode. |
| P0 | §7.1 Range entry | Limit đặt mù ở biên dễ bắt dao rơi khi thị trường đang breakout. Thời hạn 8 nến làm tín hiệu quá cũ. | Arm tại biên; yêu cầu sweep/rejection và nến đóng lại trong range; đặt post-only limit tại retest; hết hạn sau 3–4 nến. |
| P0 | §7.2 Standard entry | Chạm EMA20 không phải xác nhận trend còn hiệu lực. Limit tại EMA20 có thể khớp đúng lúc cấu trúc gãy. | Yêu cầu breakout acceptance, retest giữ mức, volume pullback co lại và nến reclaim. EMA20 chỉ là hợp lưu. |
| P0 | §3 Structural stop | Pivot gần nhất trong 40 nến có thể là nhiễu và không phải điểm phủ định của setup. Trần 3 ATR áp chung quá cứng. | Neo stop theo từng setup. Dùng trần theo mode: Range 2 ATR, Standard 2,5–3 ATR, Strong Trend tối đa 3,5 ATR kèm room cao và giảm size. |
| P0 | §5 Chuẩn hóa dữ liệu thiếu | Chuẩn hóa `TotalScore / AvailableMaxPoints` không bảo đảm parity: live có dữ liệu xấu có thể từ chối lệnh mà backtest thiếu dữ liệu lại nhận. | Core score chỉ dùng feature có lịch sử. OI/depth live chỉ veto hoặc giảm size cho tới khi có dữ liệu shadow đủ dài. |
| P0 | §7.3 Scale-in | Lý do loại tranche thứ ba vì RR xấu là sai: entry gần stop hơn có RR hình học tốt hơn. Rủi ro thật là pullback sâu báo hiệu thesis yếu. | Phân biệt `riskWeight` và `quantityWeight`; tính quantity theo khoảng cách tới stop; tranche sâu chỉ được arm sau sweep/reclaim, không đặt mù. |
| P0 | Regime trong ngày | Daily plan cố định đầu ngày không bắt được ngày chuyển từ range sang trend mạnh giữa phiên. | Cho phép override một chiều `Range → TrendUp/TrendDown` bằng nến 4h đóng, BOS 15m và relative volume; có hysteresis và cooldown. |
| P0 | §8 Phí và fill | Maker 0,02% không cố định theo tài khoản. OHLC xuyên mức không mô tả queue position hoặc adverse selection. | Snapshot maker/taker fee theo cấu hình tài khoản; live dùng GTX/post-only; backtest chạy cả fill optimistic và conservative. |
| P0 | Backtest perpetual futures | Backtest chưa trừ funding khi giữ qua chu kỳ thanh toán và chưa định nghĩa parity giữa mark price với contract price. | Tính funding theo thời điểm thanh toán; chọn rõ `workingType`; dùng chuỗi giá tương ứng trong backtest. |
| P0 | Open/correlated exposure | Cùng symbol còn có thể mở nhiều setup. Công thức tương quan chỉ xét cùng chiều và dùng risk ban đầu. | Chặn setup mới cùng symbol; xét tương quan dương cùng chiều và tương quan âm ngược chiều; tính risk còn lại tới stop hiện tại. |
| P1 | §4 Chọn chiều | So sánh tổng điểm hai chiều bị pha loãng bởi nhiều điểm không phụ thuộc chiều. Margin 8 điểm có thể loại lệnh tùy ý. | So sánh `DirectionalScore`; Range xác định chiều từ vị trí active range trước rồi mới chấm. |
| P1 | §4 Range position | “Biên độ 20 phiên” chưa được định nghĩa và có thể quá rộng so với setup intraday. | Dùng active balance từ pivot 4h/15m đã xác nhận; định nghĩa chính xác dữ liệu đóng và thời điểm tính. |
| P1 | §2 H&S neckline | `max/min` của hai đoạn vẫn biến neckline thành mức ngang, không phải đường qua hai trough/peak. | Nội suy neckline theo chỉ số nến; giới hạn độ dốc theo ATR mỗi bar. |
| P1 | §2 Pattern age | Một TTL 12 nến dùng chung cho mọi pattern không phản ánh vòng đời tín hiệu. | Breakout 3–4 nến; retest 6–8; double/H&S tối đa 12; staircase là trạng thái và cần freshness riêng. |
| P1 | §2 RSI/Fibonacci | Hai tín hiệu này xuất hiện thường xuyên và dễ nâng setup yếu qua ngưỡng. | Chỉ dùng làm confluence sau structure trigger; đứng riêng không được quyền làm setup đủ điều kiện. |
| P1 | §4.4 Volume | Body ratio 0,5 chưa kiểm tra vị trí đóng và follow-through; một nến mạnh trong ba nến vẫn có thể đã bị phủ định. | Long phải đóng gần 25% trên, Short gần 25% dưới; decay theo tuổi; kiểm tra không có nến sau phủ định. |
| P1 | §7.4 Time stop | Time stop giải phóng concurrent risk nhưng không trả lại quota lệnh đã mở trong ngày. | Sửa mô tả và đo riêng expectancy, MFE/MAE, số vốn-rủi-ro được giải phóng. |
| P1 | §7 Partial/runner | Chốt 50% cố định có thể tăng số lệnh thắng nhỏ nhưng làm expectancy giảm. | Chọn phần chốt để khóa tối thiểu một mức net R sau phí; runner trailing theo pivot xác nhận và stop không bao giờ nới. |
| P1 | §9 Acceptance | 20 lệnh/mode quá nhỏ; yêu cầu expectancy tăng ở mọi bước loại nhầm các thay đổi giảm drawdown. | Tối thiểu 100 lệnh/mode, ưu tiên 200; dùng confidence interval và non-inferiority cho expectancy. |
| P1 | §9 OOS | 2024–2025 đã được xem và chỉnh nhiều lần nên không còn độc lập; dùng 2022–2023 làm OOS đảo chiều thời gian. | Walk-forward theo thời gian, thêm symbol/giai đoạn chưa xem và ghi số lần thử tham số. |

## Thiết kế vào lệnh đề xuất

### Range Fade

```text
Active range hợp lệ
    → giá chạm/sweep biên
    → nến đóng lại trong range
    → rejection + volume không xác nhận breakout
    → post-only limit tại retest
    → filled hoặc expired sau 3–4 nến
```

Không vào giữa range. Nếu nến đóng ngoài biên với thân lớn và relative volume mạnh thì hủy setup
fade; chờ điều kiện chuyển regime hoặc breakout retest.

### Trend Pullback

```text
HTF/regime thuận chiều
    → BOS đóng nến và có acceptance
    → impulse volume mạnh
    → pullback volume co lại
    → retest giữ mức + reclaim
    → post-only limit
```

EMA20 chỉ được dùng khi trùng mức cấu trúc. Không đặt limit chỉ vì giá chạm EMA20.

### Strong Trend scale-in

| Tranche | Risk budget gợi ý | Điều kiện |
|---|---:|---|
| 1 | 40% | Trigger đã xác nhận. |
| 2 | 35% | Retest cấu trúc giữ được. |
| 3 | 25% | Pullback sâu có sweep/reclaim; thesis chưa bị phủ định. |

Với mỗi tranche:

```text
riskBudget[i] = totalRiskBudget × riskWeight[i]
quantity[i]   = riskBudget[i] / abs(entry[i] - sharedStop)
```

Phải bảo đảm tổng lỗ tại shared stop sau phí không vượt `FinalSizeR`. Hủy mọi tranche chưa khớp khi
TP1, stop hoặc invalidation xảy ra.

## Sửa các mâu thuẫn giữa tài liệu và code

| Nội dung tài liệu | Trạng thái code/nhận xét |
|---|---|
| §1.1 Fibonacci `continue` | Đã sửa trong `PriceActionAnalyzer`. |
| §1.2 Xác nhận neckline bằng close | Đã sửa. |
| §1.3 Tách `AtrPeriod` | Đã sửa. |
| §1.4 `NetConfluence` | Đã có. |
| §1.5 `LeaderCorrelation` luôn null | Không còn đúng; code hiện đã tính Pearson từ 96 nến ghép theo `CloseTime`. |
| §1.6 `TradesToday` không đếm open | Mô tả không còn đúng; backtest đếm cả open và closed, nhưng vẫn thiếu gate cùng symbol. |
| §4.4 `MinBodyRatio` | Đã triển khai một phần. |
| §7.3 Strong Trend giữ score ≥70 | Mâu thuẫn với `TradeExecutionPlanner` hiện tại vì code không còn kiểm score ≥70. |
| §7.4 Time stop trả lại ngân sách số lệnh | Không đúng với cách `TradesToday` hiện được tính. |

## Chỉ số backtest bắt buộc

| Nhóm | Chỉ số |
|---|---|
| Hiệu quả | Net win rate, expectancy, profit factor, average win/loss, payoff ratio. |
| Rủi ro | Max drawdown theo R và theo % vốn, longest loss streak, tail loss/CVaR. |
| Hành vi lệnh | MAE, MFE, TP1 hit rate, time-stop rate, runner contribution. |
| Khớp lệnh | Pending count, fill rate, expired rate, partial-fill rate, maker/taker ratio. |
| Chẩn đoán | `Mode × Regime × Direction × ExitReason`, score bands và criterion lift. |
| Chi phí | Maker/taker fee, slippage, funding, tổng cost theo R. |

Score bands tối thiểu: `55–59`, `60–64`, `65–69`, `70–74`, `75+`. Nếu score cao hơn không tạo
win rate hoặc expectancy tốt hơn một cách tương đối đơn điệu thì phải sửa trọng số; không chỉ tăng
ngưỡng vào lệnh.

## Điều kiện chấp nhận đề xuất

1. Expectancy sau phí, slippage và funding phải dương hoặc ít nhất không kém baseline ngoài biên
   non-inferiority đã định trước.
2. Net win rate phải tăng nhưng không được đổi bằng payoff ratio hoặc max drawdown xấu hơn.
3. Mỗi mode cần tối thiểu 100 lệnh để đánh giá sơ bộ; ưu tiên 200 lệnh.
4. Báo cáo confidence interval cho win rate và expectancy; không kết luận từ một con số điểm.
5. Kết quả phải tồn tại ở cả hai mô hình fill limit optimistic và conservative.
6. Phân rã riêng BTCUSDT/ETHUSDT, Long/Short, regime và năm.
7. Dùng walk-forward và một tập cuối chưa được xem; không tiếp tục chỉnh trên 2024–2025 tới khi đẹp.
8. Live trading tiếp tục tắt; chỉ xem xét paper/shadow sau khi vượt toàn bộ cổng.

## Thứ tự triển khai đề xuất

| Bước | Nội dung | Kết quả cần đo |
|---:|---|---|
| 0 | Đóng băng baseline V1.4: commit, settings snapshot, archive hash. | Có mốc so sánh tái lập được. |
| 1 | Sửa backtest: maker/taker, pending order, funding, mark/contract price, cùng-symbol gate, tranche risk. | Kết quả đúng về mô phỏng trước khi tối ưu. |
| 2 | Thêm MAE/MFE, TP1, runner contribution, score calibration, Mode/ExitReason. | Xác định chính xác nguồn thua. |
| 3 | Tách `RangeFade`, `TrendPullback`, `StrongTrend` và core gate riêng. | Win rate theo mode tăng, số lệnh giảm có kiểm soát. |
| 4 | Thêm state machine xác nhận–retest–khớp. | Giảm false entry và đo fill/expiry. |
| 5 | Stop/target neo vào invalidation riêng của setup. | Stop-out sau setup đúng giảm; expectancy tăng. |
| 6 | Intraday regime override có hysteresis. | Bắt được ngày chuyển sang trend mạnh mà không flip-flop. |
| 7 | Siết H&S, double, RSI, Fibonacci, volume freshness. | Win rate tăng thêm nhưng không overfit. |
| 8 | Walk-forward, OOS và paper/shadow. | Xác nhận độ bền trước khi cân nhắc live. |

## Tham chiếu ngoài

- [Binance — cách tính phí và maker/taker](https://academy.binance.com/ur-PK/articles/how-to-calculate-transaction-fees-on-binance)
- [Binance Futures — định nghĩa GTX/post-only](https://developers.binance.com/zh-CN/docs/products/derivatives-trading-usds-futures/common-definition)
- [Fill Probabilities in a Limit Order Book](https://arxiv.org/abs/2403.02572)
- [Pseudo-Mathematics and Financial Charlatanism: The Effects of Backtest Overfitting](https://papers.ssrn.com/sol3/papers.cfm?abstract_id=2308659)

---

## Cập nhật sau triển khai và backtest cuối — 2026-08-04

Phần review ban đầu ở trên được giữ để bảo toàn lập luận trước triển khai. Bảng dưới đây là đánh
giá sau khi đã xử lý các blocker, sửa thước đo R và chạy lại dữ liệu đầy đủ.

| Hạng mục review | Trạng thái sau triển khai | Đánh giá cuối |
|---|---|---|
| Structural room 1,6R từng cắt 94,5% | Đã tách TP1 khỏi runner/final structural target; full giữ 92,3%, OOS giữ 93,3%. | Blocker đã đóng; không hạ ngưỡng. |
| Limit entry / fill parity | Pending order thật trong backtest, expiry, maker/taker riêng; OOS optimistic và conservative lệch 1 lệnh. | Đã đóng; fill assumption không cứu kết quả. |
| Tranche risk | Quantity theo risk weight và khoảng cách riêng tới shared stop; partial fill chỉ dùng risk đã khớp. | Đã đóng bằng invariant và test. |
| Time-stop / fee breakeven / runner | Đã có time-stop, stop hoà vốn gồm phí + 0,05R, trailing pivot hiệu lực từ nến kế tiếp. | Đã triển khai; chưa tạo expectancy dương. |
| Intraday regime override | Range → trend một chiều bằng breakout + relative volume, hysteresis/release và cooldown, chỉ nến đóng. | Mã/test xong; chưa có A/B cô lập nên chưa chứng minh hiệu quả. |
| Direction margin 8 điểm | A/B chỉ cho 5 material block, không đổi số lệnh; gate đã bị xoá. | Bằng chứng âm được giữ, không duy trì nhánh chết. |
| H&S cross-constraint | Nhánh không thể chạm đã bị xoá. | Nợ kỹ thuật đã đóng. |
| CI / ExitReason / trial ledger | Đã lưu và hiển thị CI, Mode, ExitReason, structural distribution, comparable trial number. | Đã đóng các điều kiện đo lường. |
| Chuẩn hoá R | Phát hiện `RMultiple` phụ thuộc size và oversize tự co size; đã sửa, thêm regression test. | Run #18–#27 không còn hợp lệ về expectancy/cost; dùng #28 trở đi. |

### Bảng kết quả hợp lệ

| Run | Mẫu | Fill | Lệnh | Win rate | Expectancy R (CI95%) | Kết luận |
|---:|---|---|---:|---:|---:|---|
| #28 | 2022–2023 OOS | Conservative | 1.524 | 30,05% | **−0,361** [−0,449; −0,274] | Âm rõ. |
| #30 | 2022–2023 OOS | Optimistic | 1.525 | 30,03% | **−0,362** [−0,450; −0,275] | Gần như trùng #28. |
| #29 | 2020–2026-08 full | Conservative | 4.799 | 29,03% | **−0,312** [−0,362; −0,261] | Âm rõ, đủ mẫu cả ba mode. |

| Mode | OOS conservative | Full conservative | Đánh giá |
|---|---:|---:|---|
| RangeQuick | 276; −0,351R | 835; −0,225R | Bác bỏ. |
| Standard | 1.157; −0,394R | 3.664; −0,347R | Bác bỏ; nguồn lỗ chính. |
| StrongTrendRunner | 91; +0,028R, CI qua 0 | 300; −0,121R, CI qua 0 | Chưa có edge được chứng minh; OOS thiếu 9 lệnh. |

### Kết luận review cập nhật

Mức sẵn sàng triển khai live là **0/10 tại thời điểm này**, dù phần kỹ thuật mô phỏng đã tốt hơn
rõ rệt. Lý do không phải win rate 29–30% tự thân, mà là expectancy sau phí âm có ý nghĩa thống kê
và max drawdown rất lớn. Full #29 có 3.628/4.799 lệnh thoát stop; chi phí trung bình 0,2218R/lệnh.

Không tiếp tục sửa threshold trên cùng mẫu. RangeQuick và Standard phải quay lại thiết kế trigger
và confirmation ở một vòng nghiên cứu mới. StrongTrendRunner chỉ đáng thu thêm dữ liệu shadow
riêng; chưa đủ bằng chứng để paper có risk, càng không đủ để live.
