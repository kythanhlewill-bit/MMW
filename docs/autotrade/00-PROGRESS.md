# Tiến độ — Kế hoạch autotrade cá nhân cho MMW

**Bắt đầu**: 2026-07-31
**Câu hỏi**: Tối ưu MMW cho **một người dùng (chính tác giả)**, mục tiêu sinh lợi từ **autotrade futures**.
**Bối cảnh**: Đợt thẩm định trước (`docs/strategy/`) kết luận không thương mại hoá. Chủ dự án đã
quyết định chuyển hướng sang tối ưu cho cá nhân. Loạt tài liệu này **không phản biện lại quyết định đó**,
mà trả lời: đi hướng nào thì khả thi nhất và sinh lợi tốt nhất có thể.

**Cách chạy**: 5 agent **chạy tuần tự** (không song song, theo yêu cầu). Mỗi agent ghi file `.md`
ngay khi xong — quy trình đã crash 3 lần ở đợt trước, file trên đĩa là thứ duy nhất sống sót.

Run ID: `wf_1815e912-237`

## Bảng tiến độ

| # | Agent | Vai | File output | Trạng thái |
|---|---|---|---|---|
| 1 | Quantitative Trading Researcher | Thiết kế hệ đo edge | `01-edge-measurement.md` | ✅ **DONE** (1.186 dòng) |
| 2 | Systematic Strategy Architect | Hướng chiến lược khả thi | `02-strategy-direction.md` | ✅ **DONE** (1.052 dòng) |
| 3 | Execution Cost Analyst | Mô hình chi phí → ngưỡng edge | `03-cost-model-and-threshold.md` | RUNNING |
| 4 | Senior Solution Architect | Lộ trình kỹ thuật autotrade | `04-technical-roadmap.md` | TODO |
| 5 | Red-team | Premortem đường autotrade | `05-redteam.md` | TODO |
| 6 | — | Tổng hợp + quyết định (main thread) | `06-PLAN.md` | TODO |

Mỗi vòng phụ thuộc toàn bộ các vòng trước → bắt buộc tuần tự.

## Ràng buộc kiến trúc đã xác minh (quyết định không gian chiến lược)

| # | Sự thật | Bằng chứng | Hệ quả |
|---|---|---|---|
| A | **Không có WebSocket** | `grep -rli "websocket\|ClientWebSocket\|wss://" src/` = 0 | Chỉ REST polling 5 phút, khung 1h. Loại thẳng scalping, market-making, arbitrage, mọi chiến lược cần phản ứng dưới phút |
| B | **`SignalGenerator` thuần quy tắc bị bỏ không** | Đăng ký DI `DependencyInjection.cs:72`, 5 test, không luồng production nào gọi | Đây là thành phần **duy nhất** backtest được. Đường LLM đang chạy thì không |
| C | **`AiSignalScanRecord` đủ dữ liệu đo edge hồi cứu** | Lưu Price, Rsi, Ema20/50, MacdHistogram, Atr, Entry, SL, TP, RR, Action, Score cho **mọi** quyết định AI kể cả bị từ chối | Đo được edge mà không cần backtest engine đầy đủ |
| D | **Không có backtest engine** | `grep -rli "backtest" src/` = 0 | Chưa từng đo edge. Đây là khoảng trống lớn nhất |

## Nguyên tắc cho toàn loạt tài liệu

- **Tuyệt đối không bịa kết quả backtest, không hứa lợi nhuận.** Edge chưa từng được đo.
  Mọi con số hiệu suất phải là (a) trích nguồn công khai, hoặc (b) phép tính minh hoạ có ghi rõ giả định.
- Mọi số liệu ngoài phải có `[nguồn, ngày]`. Số tự ước lượng ghi rõ `(ước lượng)`.
- Không đưa lời khuyên đầu tư cá nhân — chỉ phân tích khả thi kỹ thuật và cấu trúc chi phí.
- Mọi nhận định về code neo vào `file.cs:line`.
