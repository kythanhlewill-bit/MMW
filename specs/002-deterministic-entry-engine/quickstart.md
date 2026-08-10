# Quickstart — Kiểm chứng Deterministic Intraday Trading Engine

**Feature**: `002-deterministic-entry-engine`
**Mục đích**: các kịch bản chạy được để chứng minh feature hoạt động đúng, dùng khi nghiệm thu từng phần và khi nghiệm thu toàn bộ.

Tài liệu này **không** chứa mã cài đặt. Chi tiết thiết kế xem [data-model.md](./data-model.md) và [contracts/](./contracts/).

---

## Điều kiện tiên quyết

| Mục | Yêu cầu |
|---|---|
| .NET SDK | 8.0 |
| SQL Server | Bản cục bộ, chuỗi kết nối `ConnectionStrings:Default` |
| Khoá AI | **Không bắt buộc** — phần lớn kịch bản dưới đây chạy không cần AI. Đó là điểm mấu chốt của feature. |
| Khoá sàn | **Không bắt buộc** — mọi nguồn dữ liệu mới đều công khai |
| Mạng | Chỉ cần cho việc nạp kho nến. Kiểm thử lịch sử chạy offline. |

---

## Thiết lập

```bash
dotnet restore
```

```bash
dotnet ef database update --project src/MMW.Infrastructure --startup-project src/MMW.Web
```

Seeder runtime tạo `EngineSetting` + 6 dòng bảng phiên + 12 luật cửa sổ chặn cho mỗi tài khoản. Lịch NFP được seed theo quy tắc; lịch CPI/PPI/PCE/FOMC 2026 được nạp từ lịch chính thức của BLS, BEA và Federal Reserve.

Nạp kho nến (cần mạng, chạy một lần, mất vài phút):

```bash
dotnet run --project src/MMW.Web -- backfill --symbols BTCUSDT,ETHUSDT --intervals 15m,4h,1d --from 2024-01-01
```

---

## Kịch bản 1 — Chặn theo khung giờ hoạt động khi không có AI, không có mạng

Chứng minh User Story 1 và SC-009.

```bash
dotnet test tests/MMW.RuleEngine.Tests --filter "FullyQualifiedName~TimeGuard"
```

**Kỳ vọng**:

- 8 loại cửa sổ chặn, mỗi loại 2 test: một chứng minh chặn thật trong biên, một chứng minh không chặn nhầm ngay ngoài biên → **16 test xanh** (SC-006)
- Test "lịch nạp tay rỗng" xanh: cửa sổ sinh bằng công thức vẫn cưỡng chế đủ
- Test "lịch quá hạn" xanh: hệ thống phát cảnh báo

---

## Kịch bản 2 — Chấm điểm tất định tuyệt đối

Chứng minh User Story 3, SC-002 và Nguyên tắc II.

```bash
dotnet test tests/MMW.RuleEngine.Tests --filter "FullyQualifiedName~Scoring|FullyQualifiedName~Determinism"
```

**Kỳ vọng**:

- 13 tiêu chí, mỗi tiêu chí tối thiểu 3 test: đạt điểm tối đa, đạt 0 điểm, thiếu dữ liệu → **≥ 39 test xanh**
- Test lặp lại: chấm cùng một `ScoringContext` 100 lần cho ra 100 kết quả giống hệt
- **Test reflection** khẳng định không lớp nào trong `MMW.Application.Trading` chạm `DateTime.UtcNow` hay `Random`
- **Test reflection** khẳng định không constructor nào trong `Trading` nhận `ILlmService`

Hai test reflection cuối là thứ giữ cho ranh giới không trôi theo thời gian. Chúng phải xanh, không phải "nên xanh".

---

## Kịch bản 3 — Vòng quyết định chạy được khi AI chết hoàn toàn

Chứng minh SC-001 và Nguyên tắc II.

```bash
dotnet test tests/MMW.RuleEngine.Tests --filter "FullyQualifiedName~NoAi"
```

**Kỳ vọng**: với `AiService:ApiKey` rỗng, `SignalEvalService` chạy trọn một chu kỳ, sinh phiếu chấm điểm đầy đủ, và ra quyết định bình thường. Không ngoại lệ, không cảnh báo mức lỗi.

Kiểm chứng thủ công: xoá khoá AI khỏi User Secrets, chạy ứng dụng một chu kỳ 15 phút, mở `/Scorecard` — vẫn thấy phiếu chấm điểm mới.

---

## Kịch bản 4 — AI không thể vượt quyền

Chứng minh User Story 6 và SC-008.

```bash
dotnet test tests/MMW.RuleEngine.Tests --filter "FullyQualifiedName~.Ai."
```

**Kỳ vọng**: đủ **12 trường hợp** liệt kê trong [contracts/ai-context.md](./contracts/ai-context.md) đều xanh.

Đặc biệt chú ý trường hợp 10 — bối cảnh AI cực kỳ lạc quan **thuận chiều** lệnh phải cho hệ số đúng `1.0`, không phải giá trị lớn hơn. Đây là ranh giới thật của nguyên tắc "AI chỉ được nói không".

---

## Kịch bản 5 — Kỷ luật chặn thật, không chỉ cảnh báo

Chứng minh User Story 4 và SC-007.

```bash
dotnet test tests/MMW.RuleEngine.Tests --filter "FullyQualifiedName~Discipline"
```

**Kỳ vọng**: 6 gate, mỗi gate 2 test (kích hoạt và ngay-dưới-ngưỡng) → **12 test xanh**, cộng một test khẳng định mọi gate đều trả `SizeMultiplier ≤ 1.0`.

---

## Kịch bản 6 — Kiểm thử lịch sử tái lập đúng chạy thật

Chứng minh User Story 5 và SC-003. **Đây là kịch bản quan trọng nhất.**

```bash
dotnet test tests/MMW.RuleEngine.Tests --filter "FullyQualifiedName~BacktestParity"
```

**Kỳ vọng**: chuỗi phiếu chấm điểm từ `BacktestEngine` và từ `SignalEvalService` chạy chế độ mô phỏng trên **cùng dữ liệu, cùng khoảng thời gian** trùng khớp ở mọi trường — điểm từng tiêu chí, điểm tổng, lý do veto, kích thước cuối cùng.

Nạp kho và chạy kiểm thử thật bằng CLI:

```bash
dotnet run --project src/MMW.Web -- backtest --account 1 --symbol BTCUSDT --from 2024-01-01 --to 2025-12-31
```

Mở `/Backtest` để xem báo cáo và kiểm tra khoảng trống kho. CLI được dùng vì một request web không thể thay `IClock` và market-data provider an toàn cho cả vòng lặp.

**Kỳ vọng**:

- Hoàn thành dưới 5 phút
- Báo cáo có đủ: số lệnh, tỷ lệ thắng, kỳ vọng theo R, sụt giảm vốn lớn nhất, chuỗi thua dài nhất, phân rã theo giờ và theo trạng thái ngày
- Mục **`Limitations` không rỗng** và nêu rõ 10/100 điểm bị mất do giới hạn 30 ngày của `/futures/data/*`, cộng ghi chú phí vốn dùng tỷ lệ đã thanh toán thay cho tỷ lệ dự phóng
- Ngắt mạng rồi chạy lại → vẫn chạy hết (SC-011)

Nếu `Limitations` rỗng, feature **chưa đạt** — bất kể các con số khác đẹp đến đâu.

---

## Kịch bản 7 — Không tự nới ngưỡng để có lệnh

Chứng minh FR-038 và Nguyên tắc I.

```bash
dotnet test tests/MMW.RuleEngine.Tests --filter "FullyQualifiedName~NoThresholdRelaxation"
```

**Kỳ vọng**: cho một chuỗi dữ liệu cả ngày không setup nào đạt 55 điểm → số lệnh trong ngày là **0**, và không có bản ghi nào cho thấy ngưỡng bị hạ.

Đây là test bảo vệ chống lại chính mình trong tương lai. Cám dỗ nới ngưỡng để "có lệnh mà đánh" sẽ xuất hiện, và nó cần một test đỏ để chặn lại.

---

## Kịch bản 8 — Số lớp chặn không giảm

Chứng minh SC-010, SC-014 và Nguyên tắc III.

```bash
dotnet test tests/MMW.RuleEngine.Tests --filter "FullyQualifiedName~BlockerCount|FullyQualifiedName~LiveOrder"
```

**Kỳ vọng**:

- Số lớp chặn trước khi gửi lệnh **≥** số lớp của hệ thống baseline
- Toàn bộ test `LiveOrderTests` hiện có vẫn xanh, không sửa
- `LiveTrading:Enabled` mặc định `false`, `UseTestnet` mặc định `true`

---

## Nghiệm thu toàn bộ

```bash
dotnet build --configuration Release
```

```bash
dotnet test
```

| Cổng chất lượng (hiến chương) | Cách kiểm |
|---|---|
| 1 — Hiến chương | Mục Constitution Check trong [plan.md](./plan.md), cả hai vòng đều PASS |
| 2 — Build | `dotnet build` không lỗi, không cảnh báo mới |
| 3 — Test | `dotnet test` xanh toàn bộ, không test nào bị bỏ qua |
| 4 — An toàn tiền thật | Kịch bản 8 |
| 5 — Bí mật | Không khoá nào trong migration, seed, hay bản ghi mới |
| 6 — Migration | Áp dụng được trên cơ sở dữ liệu **sạch** |
| 7 — Đồng bộ spec | Hành vi khác spec ⟹ cập nhật spec trong cùng thay đổi |

---

## Nghiệm thu vận hành — 1 tuần chạy mô phỏng

Trước khi coi feature là xong, cho chạy 7 ngày liên tục với `LiveTrading:Enabled = false` và xác nhận:

1. Vào `/Settings`, bật **Deterministic engine**. Giữ `LiveTrading:Enabled=false` và `UseTestnet=true` trong cấu hình.

2. Đủ **7 bản `DailyPlan`**, không thiếu ngày nào
3. Mỗi ngày ~**192 phiếu chấm điểm** (2 symbol × 96 nến 15 phút), kể cả những phiếu kết luận không vào lệnh
4. Tổng lần gọi AI **< 30/ngày** — đếm trong bảng kiểm toán (SC-005)
5. Có ít nhất một ngày ra **0 lệnh**, và điều đó **không** sinh cảnh báo lỗi nào
6. Mọi lần chặn theo khung giờ đều tra được lý do trong dưới 30 giây qua giao diện (SC-013)
7. Bảng so sánh song song có dữ liệu của cả hai đường (User Story 7)

Điểm 4 dễ bị hiểu nhầm là hỏng hóc. Nó là bằng chứng feature hoạt động đúng.
