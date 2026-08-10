# Adaptive scoring & execution V1

Mục tiêu của vòng này là chuyển kinh nghiệm giao dịch thực tế thành quy tắc tất định có thể
kiểm thử, không biến một mô hình kỹ thuật đơn lẻ thành lời tiên tri. Live trading vẫn mặc định
tắt; mọi thay đổi phải qua backtest trước.

## Nguyên tắc

1. Gate cứng chỉ dành cho sai hướng khung lớn rõ ràng, tin mạnh, thiếu stop và vi phạm rủi ro.
2. Bậc thang, vai-đầu-vai, hai đáy/đỉnh, RSI phân kỳ và Fibonacci là **hợp lưu điểm mềm**.
3. Hai hoặc ba điểm vào là các tranche được lập trước trong cùng vùng setup. Tổng `FinalSizeR`
   không tăng; không khớp thêm tranche nào sau khi stop bị chạm hoặc cấu trúc mất hiệu lực.
4. Ngày range lấy mục tiêu ngắn và rút toàn bộ; ngày trend mạnh mới được giữ runner.
5. Thứ Bảy/Chủ nhật không bị cấm tuyệt đối: hạ rủi ro tối đa còn 0,5 và tối đa 2 setup/ngày.
6. Cửa sổ CPI/PPI/PCE/NFP/FOMC vẫn là gate cứng; điểm đẹp không được vượt TimeGuard.

## Bộ chấm điểm

Giữ nguyên 13 tiêu chí và tổng tối đa 85 điểm để không phá ngưỡng 55/70/85 hiện hữu:

- `technical.market_structure` (10): BOS/retest cộng hợp lưu bậc thang, hai đáy/đỉnh,
  vai-đầu-vai hoặc vai-đầu-vai ngược.
- `technical.entry_location` (8): khoảng cách EMA20/VWAP là nền; vùng hồi 38,2–61,8% của
  nhịp gần nhất chỉ cộng điểm khi thuận chiều cấu trúc.
- `technical.momentum` (7): RSI/MACD là nền; phân kỳ RSI tại hai pivot gần nhất thay đổi mức
  tin cậy nhưng không tự sinh lệnh.
- `technical.volume_confirmation` (5): chỉ đạt tối đa khi volume mở rộng và thân nến đóng
  thuận chiều. Volume lớn nhưng nến ngược chiều không được coi là xác nhận.

## Kế hoạch thực thi

### Range

- Một điểm vào, không bình quân giá.
- Chốt toàn bộ tại 1R.
- Không runner.

### Bình thường

- Một điểm vào.
- Chốt toàn bộ theo R:R cấu hình (mặc định 1,5R).

### Trend mạnh

Điều kiện đồng thời: regime ngày thuận chiều, điểm ≥70, structure ≥8/10 và volume =5/5.

- Ba tranche tại 0R, hồi 0,25R và hồi 0,5R; mỗi tranche chiếm 1/3 kích thước dự kiến.
- Chốt 50% tại 1,5R.
- Dời stop phần còn lại về giá vốn bình quân.
- Runner chốt tại 3R.

Nếu chỉ một hoặc hai tranche khớp thì phần chưa khớp bị huỷ khi TP1 hoặc stop xảy ra; không
đuổi theo để bù đủ vị thế.

## Điều kiện đánh giá

So với baseline #7 trên cùng BTCUSDT 2024-01-01→2025-12-31:

- Expectancy sau phí/trượt giá phải tăng và ưu tiên dương.
- Max drawdown và chuỗi thua không được xấu hơn chỉ để đổi lấy win rate đẹp.
- Báo cáo phải phân rã đúng regime, không gán mặc định mọi lệnh thành `Range`.
- Gate số lệnh/ngày phải dùng trạng thái mô phỏng; một vị thế đang mở không được tạo thêm
  setup độc lập cùng symbol.
- Nếu kết quả không cải thiện, giữ bằng chứng âm và bỏ thay đổi tham số thay vì tối ưu tiếp
  trên cùng một mẫu đến khi đẹp.
