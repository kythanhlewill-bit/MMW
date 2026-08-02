# Tiến độ thẩm định chiến lược MMW

**Bắt đầu**: 2026-07-28 · **Chạy lại lần 2**: 2026-07-29
**Mục tiêu**: Thẩm định độ khả thi + chiến lược phát triển MMW dưới 3 lens: Crypto Financial Analyst, Web3/Solution Architect, Venture Capitalist.
**Cách chạy**: tối đa **2 agent song song**. Mỗi agent xong ghi ngay 1 file `.md` trong thư mục này (chống mất dữ liệu khi crash).

### Lịch sử các lần chạy

| Lần | Ngày | Cấu hình | Kết quả |
|---|---|---|---|
| 1 | 2026-07-28 | 2 agent song song | Crash ở vòng 1, **không file output** |
| 2 | 2026-07-29 | 3 agent song song (`wf_d515f2c5-214`) | Tiến trình thoát khi vòng 1 đang chạy, **không file output** |
| 3 | 2026-07-29 | **2 agent song song** (`wf_691c49ca-e3e`) | ✅ Vòng 1 xong (file 01 + 02), thoát khi vòng 2 đang chạy |
| 4 | 2026-07-29 | Resume `wf_691c49ca-e3e` — vòng 1 lấy cache, chạy tiếp từ vòng 2 | Đang chạy |

### Sự cố công cụ đã biết

`WebSearch`/`WebFetch` **lỗi backend** trong lần chạy 3. Agent A xoay sang browser automation
(`mcp__Claude_Browser__*` + DuckDuckGo) và đọc được 60+ trang nguồn thật. Từ vòng 2 trở đi prompt
đã báo trước sự cố này để agent không mất thời gian retry.

> **Resume**: file `.md` nào đã tồn tại trên đĩa thì coi như DONE. Script của lần 3 nằm ở
> `.claude/projects/D--KYLT-MMW/<session>/workflows/scripts/mmw-strategy-appraisal-v2-wf_691c49ca-e3e.js`
> — resume bằng `Workflow({scriptPath, resumeFromRunId: "wf_691c49ca-e3e"})`, agent đã xong sẽ trả cache.

## Nền tảng đầu vào

- Hiến chương: `.specify/memory/constitution.md` (v1.0.0, 7 nguyên tắc + 7 cổng chất lượng)
- Baseline spec: `specs/001-mmw-system-baseline/spec.md` (50 FR, 12 SC, Phụ lục B: 8 khoảng trống)
- Mã nguồn: 210 file `.cs`, 5 project (.NET 8, MVC + EF Core + Hangfire + SignalR + SQL Server)
- **LOC thật ≈ 12.3k** (Domain 1.3k · Application 6.3k · Infrastructure 2.2k + 16.7k migrations · Web 2.4k · Shared 0.1k)
- Test: 12 file, ~69 `[Fact]/[Theory]` (LiveOrderTests 19, RuleTests 11, BehaviorTests 9, IndicatorTests 9)
- Thực trạng: single-tenant, self-host, 1 trader, **không có blockchain/token/smart contract**

## Bảng tiến độ

| Vòng | Agent | Vai | File output | Trạng thái |
|---|---|---|---|---|
| 1 | A | Nghiên cứu thị trường & đối thủ (2026) | `01-market-landscape.md` | ✅ **DONE** (829 dòng) |
| 1 | B | Kiểm kê thực trạng sản phẩm & mã nguồn | `02-product-reality.md` | ✅ **DONE** (628 dòng) |
| 2 | C | Senior Crypto Financial Analyst | `03-financial-analyst.md` | ✅ **DONE** (1.171 dòng) |
| 2 | D | Senior Web3/Crypto Solution Architect | `04-solution-architect.md` | ✅ **DONE** (1.152 dòng) |
| 3 | E | Venture Capitalist — Risk/Reward, runway, exit | `05-vc-assessment.md` | RUNNING |
| 3 | F | Lộ trình tính năng → doanh thu | `06-monetization-roadmap.md` | TODO |
| 4 | G | Premortem / red-team (phản biện đối kháng) | `07-premortem-redteam.md` | TODO |
| 5 | — | Tổng hợp + quyết định (main thread) | `08-SYNTHESIS.md` | TODO |

## Phụ thuộc giữa các vòng

```
Vòng 1 (2 agent): A (thị trường) ┐
                  B (sản phẩm)   ┴─> Vòng 2 (2 agent): C (tài chính, cần A+B)
                                                       D (kiến trúc, cần B)
                                     Vòng 3 (2 agent): E (VC, cần A→D)
                                                       F (roadmap, cần A→D)
                                     Vòng 4 (1 agent): G (red-team, cần tất cả)
                                     Vòng 5: Tổng hợp (main thread)
```

## Ghi chú

- Đây là phân tích **chiến lược sản phẩm/kinh doanh**, không phải tư vấn đầu tư cá nhân.
- Kiến thức mô hình chốt ở 05/2026; mọi số liệu thị trường phải lấy qua web search và ghi rõ **nguồn + ngày truy cập**.
- Mọi con số không có nguồn phải đánh dấu rõ là **ước lượng/giả định**, không được trình bày như dữ kiện.
