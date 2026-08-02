# Implementation Plan: Deterministic Intraday Trading Engine

**Branch**: `002-deterministic-entry-engine` | **Date**: 2026-08-02 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-deterministic-entry-engine/spec.md`

## Summary

Đảo ngược quan hệ giữa thuật toán và mô hình ngôn ngữ trong luồng vào lệnh. Hiện tại AI quyết định hướng lệnh và tự đặt giá vào/cắt lỗ/chốt lời; sau feature này một thuật toán tất định 4 tầng ra toàn bộ quyết định, còn AI chỉ được **veto hoặc giảm** kích thước.

Bốn tầng: **Kế hoạch ngày** (23:30 UTC, ràng buộc cả ngày) → **Chặn theo khung giờ** (lịch sự kiện nội bộ + công thức) → **Chấm điểm 0–100** (4 nhóm tiêu chí dạng plug-in) → **Thực thi** (kích thước theo điểm, gate kỷ luật chặn cứng).

Ba quyết định kỹ thuật chi phối toàn bộ thiết kế:

1. **`IClock` + `ArchiveMarketDataProvider`** — kiểm thử lịch sử chạy **đúng cùng một bộ mã** với chạy thật bằng cách thay hai cổng phụ thuộc thời gian và dữ liệu, không có nhánh mã riêng. Đây là cách duy nhất thoả FR-053/FR-054.
2. **Nến đã đóng là đơn vị tính duy nhất** — thêm cờ `IsClosed`, chỉ báo chỉ ăn nến đóng. Không có bước này thì mọi con số kiểm thử là vô nghĩa.
3. **`IScoreCriterion` / `IDisciplineGate` dạng plug-in** — thêm một tiêu chí chấm điểm là thêm một lớp, không sửa vòng lặp tổng hợp. Bắt buộc theo Nguyên tắc V.

Hạ tầng baseline (nhật ký lệnh, chuỗi lớp chặn trước sàn, thông báo, ghi vết, phát hiện hành vi) được tái sử dụng nguyên trạng.

## Technical Context

**Language/Version**: C# 12 / .NET 8

**Primary Dependencies**: ASP.NET Core MVC + Razor · EF Core 8 · Hangfire (SQL Server storage) · SignalR · Serilog · AutoMapper — toàn bộ đã có sẵn, feature này **không thêm package mới ở tầng Application/Domain**

**Storage**: SQL Server, EF Core code-first. 7 bảng mới + 1 bảng cấu hình mới + mở rộng enum. Kho nến lịch sử là bảng nặng nhất (~140k dòng cho 2 symbol × 2 năm × khung 15m)

**Testing**: xUnit 2.9.2 + `Microsoft.EntityFrameworkCore.InMemory` 8.0.8, dự án hiện có `tests/MMW.RuleEngine.Tests`

**Target Platform**: Tiến trình thường trú trên Windows (Kestrel + Hangfire server trong cùng process)

**Project Type**: Web application — monolith MVC 5 tầng `Web → Application → Infrastructure → Domain`, `Shared` chứa hợp đồng dùng chung

**Performance Goals**:
- Chu kỳ đánh giá cơ hội (2 symbol × 3 khung thời gian) hoàn thành **< 10 giây**, tức nằm gọn trong khe giữa hai nến 15 phút
- Chu kỳ quản lý vị thế hoàn thành **< 5 giây** để chạy an toàn mỗi phút
- Kiểm thử lịch sử 2 năm dữ liệu 15 phút (~70.000 nến/symbol) hoàn thành **< 5 phút**
- Sinh kế hoạch ngày **< 30 giây** kể cả khi phải chờ timeout của các nguồn phụ

**Constraints**:
- Vòng quyết định vào lệnh: **0 lần gọi AI** (FR-049)
- Toàn bộ chấm điểm phải **tất định tuyệt đối** — cùng đầu vào, cùng đầu ra, không phụ thuộc đồng hồ hệ thống ngoài `IClock`
- Kiểm thử lịch sử phải chạy được **hoàn toàn offline**
- Tổng lần gọi AI **< 30/ngày** (FR-046)
- Giao dịch thật **vẫn tắt** khi feature kết thúc

**Scale/Scope**: 1 người dùng · 2 symbol · 3 khung thời gian · ~96 lần đánh giá/ngày/symbol · ~1.440 lần quản lý vị thế/ngày · 13 tiêu chí chấm điểm · 6 gate kỷ luật · 8 loại sự kiện lịch

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*Nguồn: `.specify/memory/constitution.md` v1.0.0.*

### Vòng 1 — trước Phase 0

| # | Cổng | Câu hỏi kiểm tra | Kết quả |
|---|------|------------------|---------|
| 1 | I. Kỷ luật hơn dự đoán | Thay đổi này giúp trader giữ kỷ luật ra sao? Có ngưỡng nào bị hardcode thay vì đọc từ cấu hình tài khoản không? | **PASS có điều kiện** — xem ghi chú 1 |
| 2 | II. Deterministic trước, AI sau | Có con số quyết định nào phụ thuộc AI không? Mọi luồng gọi AI đã có nhánh dự phòng và kiểm chứng đầu ra bằng luật cứng chưa? | **PASS** — xem ghi chú 2 |
| 3 | III. An toàn mặc định (KHÔNG THƯƠNG LƯỢNG) | Có chạm tới đường dẫn đặt lệnh thật không? Số lớp chặn có giảm không? Nhật ký còn khớp 1-1 với sàn không? | **PASS** — xem ghi chú 3 |
| 4 | IV. Ghi vết toàn bộ | Quyết định mới có được ghi kiểm toán không? Có bí mật nào lọt vào log/audit không? | **PASS** — xem ghi chú 4 |
| 5 | V. Kiến trúc phân tầng | Chiều phụ thuộc có bị vi phạm không? Quy tắc/detector mới có thêm được mà không sửa engine không? | **PASS** — xem ghi chú 5 |
| 6 | VI. Test tương xứng rủi ro | Vùng bắt buộc test có test đỏ trước không? Mỗi lớp chặn mới có test chứng minh nó chặn thật không? | **PASS** — xem ghi chú 6 |
| 7 | VII. Bí mật an toàn | Có bí mật nào vào mã, migration, cấu hình đã commit, hay log không? | **PASS** — xem ghi chú 7 |

**Ghi chú 1 — điều kiện để cổng I đạt.** Bản spec chứa nhiều hằng số: trọng số 4 nhóm (40/30/15), ngưỡng điểm (55/70/85), hệ số kích thước (0.5/1.0/1.5), bảng chất lượng phiên, độ rộng từng cửa sổ chặn, ngưỡng 50 lệnh để bật thống kê cá nhân, trần 120 phút cho cửa sổ AI đề xuất. Nguyên tắc I nói **"hardcode ngưỡng trong logic phát hiện là vi phạm hiến chương"**.

→ **Quyết định thiết kế**: toàn bộ các hằng số trên đi vào một thực thể cấu hình mới `EngineSetting` (1:1 với `TradingAccount`, cùng khuôn mẫu `RiskSetting` đã có) cộng hai bảng con `SessionQualityRow` và `BlackoutRule`. Giá trị trong spec trở thành **giá trị seed mặc định**, không phải hằng số trong mã. Không có con số nào ở trên được viết thẳng vào lớp tính toán.

Đây là điều kiện bắt buộc, không phải tuỳ chọn — nếu bỏ qua thì cổng I chuyển thành VIOLATION.

**Ghi chú 2.** Toàn bộ FR-025 → FR-039 là số học thuần trên dữ liệu giá và cấu hình. `EntryScorer` **không nhận** `ILlmService` vào constructor — ràng buộc này kiểm tra được bằng test phản chiếu (reflection). Bối cảnh AI đi vào qua `MarketContextRecord` đọc từ cơ sở dữ liệu, và lớp áp dụng nó cưỡng chế `min()` một chiều: giá trị AI chỉ được áp khi làm quyết định thận trọng hơn hoặc bằng. Không có AI → bối cảnh rỗng → hệ số 1.0 → vòng chạy bình thường.

**Ghi chú 3.** Feature này **không sửa** `LiveOrderService` và chuỗi 13 lớp chặn trước sàn. Nó thêm veto ở tầng trên (`EntryScorer` + `DisciplineGateRunner`), tức thêm lớp chứ không bớt. `LiveTrading.Enabled` giữ `false`, `UseTestnet` giữ `true`. Bổ sung một test đếm số lớp chặn để chứng minh không giảm (SC-010).

**Ghi chú 4.** Ba loại bản ghi kiểm toán mới: `EntryScorecard` (lưu **kể cả khi không vào lệnh**), `DailyPlan`, `MarketContextRecord`. Tất cả chỉ chứa số liệu thị trường và điểm số — không chạm khoá bí mật. Bản ghi kiểm toán AI hiện có (`AiSignalScanRecord`) được giữ nguyên cho chế độ so sánh song song.

**Ghi chú 5.** Hai hợp đồng plug-in mới: `IScoreCriterion` và `IDisciplineGate`. Vòng tổng hợp điểm duyệt qua danh sách được tiêm từ DI; thêm một tiêu chí = thêm một lớp + một dòng đăng ký. `IMarketDataProvider` mở rộng nằm ở `Application`; cài đặt Binance ở `Infrastructure`. `Domain` không thêm phụ thuộc nào.

**Ghi chú 6.** Vùng bắt buộc test đỏ trước theo Nguyên tắc VI: 13 tiêu chí chấm điểm, 6 gate kỷ luật, 8 loại cửa sổ chặn (mỗi loại 2 test: trong biên chặn thật, ngoài biên không chặn nhầm), bảng ánh xạ kế hoạch ngày, lớp chống lạm quyền của AI, và kiểm thử tương đương lịch sử ↔ mô phỏng.

**Ghi chú 7.** Toàn bộ nguồn dữ liệu mới (phí vốn, lượng hợp đồng mở, tỷ lệ mua/bán, độ sâu sổ lệnh, chỉ số tâm lý) là endpoint **công khai không cần khoá**. Khoá dịch vụ AI vẫn ở User Secrets như hiện tại.

### Vòng 2 — sau Phase 1 design

| # | Cổng | Kết quả sau thiết kế | Bằng chứng trong artifact |
|---|------|------|---|
| 1 | I | **PASS** | `EngineSetting` + `SessionQualityRow` + `BlackoutRule` trong [data-model.md](./data-model.md); không hằng số nào nằm trong lớp tính |
| 2 | II | **PASS** | `IEntryScorer` trong [contracts/](./contracts/) không có tham số AI; `MarketContextApplier` chỉ có phép `min()` |
| 3 | III | **PASS** | Không artifact nào sửa `LiveOrderService`; `EntryDecision` là đầu vào **thêm** cho luồng tạo lệnh |
| 4 | IV | **PASS** | `EntryScorecard` có ràng buộc lưu mọi lần đánh giá; `DailyPlan` một bản/ngày |
| 5 | V | **PASS** | `IScoreCriterion`/`IDisciplineGate` trong [contracts/](./contracts/); vòng tổng hợp không biết tiêu chí cụ thể nào |
| 6 | VI | **PASS** | [quickstart.md](./quickstart.md) liệt kê nhóm test bắt buộc và lệnh chạy |
| 7 | VII | **PASS** | Không nguồn dữ liệu mới nào cần khoá |

**Kết luận: không có VIOLATION. Bảng Complexity Tracking để trống.**

## Project Structure

### Documentation (this feature)

```text
specs/002-deterministic-entry-engine/
├── spec.md              # Đặc tả (đã có)
├── plan.md              # Tệp này
├── research.md          # Phase 0 — 12 quyết định kỹ thuật
├── data-model.md        # Phase 1 — 8 thực thể mới + mở rộng
├── quickstart.md        # Phase 1 — kịch bản kiểm chứng
├── contracts/           # Phase 1 — hợp đồng nội bộ
│   ├── scoring.md
│   ├── timeguard.md
│   ├── daily-plan.md
│   ├── market-data.md
│   ├── backtest.md
│   └── ai-context.md
├── checklists/
│   └── requirements.md  # Đã có, đạt toàn bộ
└── tasks.md             # Phase 2 — do /speckit-tasks sinh
```

### Source Code (repository root)

```text
src/MMW.Domain/
├── Entities/
│   ├── DailyPlan.cs                     # MỚI
│   ├── ScheduledEvent.cs                # MỚI
│   ├── EntryScorecard.cs                # MỚI
│   ├── EntryScorecardLine.cs            # MỚI — điểm từng tiêu chí
│   ├── MarketContextRecord.cs           # MỚI
│   ├── KlineArchive.cs                  # MỚI
│   ├── BacktestRun.cs                   # MỚI
│   ├── EngineSetting.cs                 # MỚI — mọi ngưỡng của engine
│   ├── SessionQualityRow.cs             # MỚI — bảng phiên, con của EngineSetting
│   └── BlackoutRule.cs                  # MỚI — độ rộng cửa sổ theo loại sự kiện
├── Enums/TradingEnums.cs                # MỞ RỘNG — DayRegime, VolatilityRegime,
│                                        #   ScheduledEventKind, VetoReason, ScoreGroup
└── DbContext/
    ├── MmwDbContext.cs                  # MỞ RỘNG — 10 DbSet mới
    └── Configurations/                  # MỚI — 10 tệp cấu hình

src/MMW.Application/
├── Abstractions/
│   └── IClock.cs                        # MỚI — cổng thời gian, nền của tính tái lập
├── Trading/                             # MỚI — toàn bộ engine
│   ├── DailyPlanning/
│   │   ├── IDailyPlanService.cs · DailyPlanService.cs
│   │   ├── IDayRegimeClassifier.cs · DayRegimeClassifier.cs
│   │   └── DailyPlanInputs.cs
│   ├── TimeGuard/
│   │   ├── ITimeGuardService.cs · TimeGuardService.cs
│   │   ├── IScheduledEventCalendar.cs · ScheduledEventCalendar.cs
│   │   ├── IDerivedEventGenerator.cs · DerivedEventGenerator.cs
│   │   └── BlackoutDecision.cs
│   ├── Scoring/
│   │   ├── IEntryScorer.cs · EntryScorer.cs        # vòng tổng hợp, KHÔNG sửa khi thêm tiêu chí
│   │   ├── IScoreCriterion.cs · ScoringContext.cs · CriterionResult.cs
│   │   └── Criteria/                    # 13 lớp, mỗi lớp một tiêu chí
│   ├── Discipline/
│   │   ├── IDisciplineGate.cs · DisciplineGateRunner.cs
│   │   └── Gates/                       # 6 lớp
│   ├── Sizing/
│   │   └── IPositionSizer.cs · ScoreBasedPositionSizer.cs
│   └── Structure/
│       ├── ISwingDetector.cs · SwingDetector.cs
│       └── MarketStructureAnalyzer.cs
├── Indicators/
│   ├── IIndicatorService.cs             # MỞ RỘNG — Percentile, Vwap, VolumeSma
│   └── IndicatorService.cs              # MỞ RỘNG
├── MarketData/
│   ├── IMarketDataProvider.cs           # MỞ RỘNG — 5 phương thức mới
│   ├── IMarketSentimentProvider.cs      # MỚI
│   └── Models/
│       ├── Candle.cs                    # MỞ RỘNG — cờ IsClosed
│       └── FuturesMetrics.cs            # MỚI — funding, OI, long/short, depth, taker flow
├── Backtest/
│   ├── IBacktestEngine.cs · BacktestEngine.cs
│   ├── IKlineArchiveService.cs · KlineArchiveService.cs
│   ├── ArchiveMarketDataProvider.cs     # cùng interface, đọc từ kho → bảo đảm parity
│   ├── BacktestClock.cs                 # IClock do backtest điều khiển
│   └── Models/BacktestReport.cs
├── Ai/
│   ├── IMarketContextService.cs · MarketContextService.cs
│   ├── MarketContextApplier.cs          # cưỡng chế một chiều: chỉ được giảm
│   └── Prompts/DailyBriefPrompt.cs · NewsClassifierPrompt.cs
└── Services/
    ├── SignalEvalService.cs             # MỚI — thay nhánh AI trong MarketScanService
    ├── PositionManageService.cs         # MỚI
    └── MarketScanService.cs             # SỬA — đường AI chuyển sang chỉ-ghi-nhật-ký

src/MMW.Infrastructure/
├── Abstractions/SystemClock.cs          # MỚI
├── Exchanges/Binance/
│   ├── BinanceMarketDataProvider.cs     # MỞ RỘNG — 5 endpoint công khai
│   └── BinanceFuturesDataParser.cs      # MỚI
├── MarketSentiment/
│   └── AlternativeMeFearGreedProvider.cs # MỚI
├── Ai/ClaudeLlmService.cs               # MỚI
└── Persistence/
    ├── Migrations/                      # 1 migration cho toàn bộ bảng mới
    └── SeedData.cs                      # MỞ RỘNG — EngineSetting + lịch sự kiện 2026

src/MMW.Web/
├── Program.cs                           # SỬA — 4 job thay 1 job market-scan
├── Controllers/
│   ├── DailyPlanController.cs           # MỚI
│   ├── ScorecardController.cs           # MỚI
│   └── BacktestController.cs            # MỚI
└── Views/DailyPlan · Scorecard · Backtest   # MỚI

tests/MMW.RuleEngine.Tests/
├── Scoring/                             # 13 tệp — một tiêu chí một tệp
├── Discipline/                          # 6 tệp
├── TimeGuard/                           # 3 tệp
├── DailyPlanning/                       # 2 tệp
├── Backtest/                            # 2 tệp, gồm test tương đương
├── Ai/MarketContextApplierTests.cs      # test chống lạm quyền AI
└── Constitution/BlockerCountTests.cs     # SC-010
```

**Structure Decision**: giữ nguyên monolith 5 tầng hiện có. Toàn bộ engine mới nằm trong một namespace con `MMW.Application.Trading` để ranh giới rõ ràng và để dễ tách ra thư viện riêng về sau nếu cần. Không tạo dự án mới — thêm một dự án chỉ để chứa engine sẽ làm nặng chuỗi tham chiếu mà không đổi được gì về ranh giới, vì `Application` vốn đã không được phép chạm SDK bên ngoài.

Kiểm thử vẫn dồn vào `tests/MMW.RuleEngine.Tests` (dự án duy nhất hiện có), chia theo thư mục con thay vì tạo dự án test mới.

## Complexity Tracking

> Không có vi phạm hiến chương nào cần biện minh. Bảng để trống.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| — | — | — |
