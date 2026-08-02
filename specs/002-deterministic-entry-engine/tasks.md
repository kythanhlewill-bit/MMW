---

description: "Task list for Deterministic Intraday Trading Engine"
---

# Tasks: Deterministic Intraday Trading Engine

**Input**: Design documents from `/specs/002-deterministic-entry-engine/`

**Prerequisites**: [plan.md](./plan.md) · [spec.md](./spec.md) · [research.md](./research.md) · [data-model.md](./data-model.md) · [contracts/](./contracts/)

**Tests**: Theo Nguyên tắc VI của hiến chương, test là **BẮT BUỘC — viết đỏ trước** cho: công thức tính chỉ số rủi ro, quy tắc kỷ luật, bộ phát hiện hành vi, mọi lớp chặn của luồng đặt lệnh thật, và bộ phân tích cú pháp phản hồi AI. Feature này nằm gần như trọn trong vùng đó, nên hầu hết task đều đi theo cặp `[TEST]` → cài đặt. Task gắn nhãn `[TEST]` **phải đỏ trước khi** task cài đặt tương ứng bắt đầu.

**Organization**: Tasks nhóm theo user story để mỗi story cài đặt và kiểm chứng độc lập được.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: chạy song song được (khác tệp, không phụ thuộc task chưa xong)
- **[TEST]**: task viết test, phải đỏ trước khi cài đặt
- **[Story]**: user story tương ứng (US1…US7)

## Path Conventions

Monolith 5 tầng hiện có: `src/MMW.{Domain,Application,Infrastructure,Shared,Web}` và `tests/MMW.RuleEngine.Tests`.

---

## Phase 1: Setup

**Purpose**: chuẩn bị cây thư mục và chốt các điểm chưa xác minh

- [x] T001 Đối chiếu 7 endpoint Binance liệt kê ở R-003 với tài liệu USDⓈ-M Futures hiện hành; cập nhật đường dẫn, ràng buộc `period` và giới hạn lịch sử vào `specs/002-deterministic-entry-engine/research.md`
- [x] T002 [P] Tạo cây thư mục `src/MMW.Application/Trading/{DailyPlanning,TimeGuard,Scoring/Criteria,Discipline/Gates,Sizing,Structure}`
- [x] T003 [P] Tạo cây thư mục `src/MMW.Application/{Abstractions,Backtest/Models}` và `src/MMW.Infrastructure/{Abstractions,MarketSentiment}`
- [x] T004 [P] Tạo cây thư mục `tests/MMW.RuleEngine.Tests/{Scoring,Discipline,TimeGuard,DailyPlanning,Backtest,Ai,Constitution}`

### Kết quả T001 — ba điểm thiết kế bị đổi

Đối chiếu bằng cách **gọi thẳng API thật**, không đọc tài liệu. Cả 7 endpoint sống, đường dẫn đúng nguyên văn. Ba phát hiện làm đổi kế hoạch:

1. **`/fapi/v1/fundingRate` có đủ ≥ 2 năm lịch sử** — không bị giới hạn 30 ngày như đã giả định. Kiểm thử lịch sử mất **10/100 điểm** thay vì 14. Kéo theo một thực thể mới `FundingRateArchive` (T020b) và các task nạp/đọc kho phí vốn.
2. **`period` sai trả HTTP 200 + mảng rỗng**, không phải lỗi → một lỗi đánh máy sẽ giết một tiêu chí trong im lặng. Cần danh sách trắng phía client, sai thì **ném ngoại lệ** (bẫy B1 ở R-003).
3. **`/fapi/v1/fundingRate?limit=1001` trả HTTP 200 kèm phong bì lỗi phi tiêu chuẩn** `{"status":"ERROR","code":"99099990",...}` → bộ bóc tách phải phát hiện đối tượng ở nơi chờ mảng (bẫy B3).

Sửa kéo theo: `research.md` R-003/R-004 + bảng rủi ro · `data-model.md` mục 9b · `contracts/market-data.md` ràng buộc 3/5/6 · `contracts/backtest.md` · `quickstart.md`.

*Quy ước ID*: task phát sinh sau khi tasks.md đã chốt dùng hậu tố chữ (`T020b`) thay vì đánh số lại toàn bộ — đánh số lại làm hỏng mọi tham chiếu chéo giữa các tài liệu.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: hạ tầng bắt buộc phải xong trước MỌI user story

**⚠️ CRITICAL**: không user story nào bắt đầu được cho tới khi phase này xong

### 2A — Enum và cổng thời gian

- [x] T005 Thêm 9 enum mới (`DayRegime`, `VolatilityRegime`, `AllowedDirections`, `ScheduledEventKind`, `ScheduledEventOrigin`, `ScoreGroup`, `ScorecardOutcome`, `MarketContextKind`, `VetoReason`) vào `src/MMW.Domain/Enums/TradingEnums.cs` theo quy ước đánh số 3xx ở [data-model.md](./data-model.md) mục 12
- [x] T006 [P] Tạo `IClock` trong `src/MMW.Application/Abstractions/IClock.cs`
- [x] T007 [P] Tạo `SystemClock` trong `src/MMW.Infrastructure/Abstractions/SystemClock.cs`
- [x] T008 Đăng ký `IClock` → `SystemClock` (singleton) trong `src/MMW.Infrastructure/DependencyInjection.cs`

### 2B — Nến đã đóng (điều kiện tiên quyết của toàn bộ feature)

- [x] T009 [TEST] Viết `tests/MMW.RuleEngine.Tests/CandleExtensionsTests.cs`: `ClosedOnly()` cắt đúng nến chưa đóng, **giữ lại** nến vừa đóng đúng mốc, xử lý chuỗi rỗng và chuỗi toàn nến hở
- [x] T010 Thêm `IsClosed` và `CandleExtensions.ClosedOnly(IClock)` vào `src/MMW.Application/MarketData/Models/Candle.cs`
- [x] T011 [TEST] Mở rộng `tests/MMW.RuleEngine.Tests/IndicatorTests.cs`: chỉ báo tính giữa chu kỳ nến **bằng** chỉ báo tính lại sau khi nến đóng (SC-004)
- [x] T012 Sửa `src/MMW.Application/MarketData/MarketAnalyzer.cs` gọi `ClosedOnly()` trước mọi phép tính; giá hiện tại nhận qua tham số thay vì lấy từ `closes[^1]`
- [x] T013 Cập nhật `tests/MMW.RuleEngine.Tests/MarketAnalyzerTests.cs` cho chữ ký mới, giữ toàn bộ assertion cũ xanh

### 2C — Thực thể và migration

- [x] T014 [P] Tạo `src/MMW.Domain/Entities/EngineSetting.cs` theo [data-model.md](./data-model.md) mục 1
- [x] T015 [P] Tạo `src/MMW.Domain/Entities/SessionQualityRow.cs` và `BlackoutRule.cs` (mục 2, 3)
- [x] T016 [P] Tạo `src/MMW.Domain/Entities/ScheduledEvent.cs` (mục 4)
- [x] T017 [P] Tạo `src/MMW.Domain/Entities/DailyPlan.cs` (mục 5)
- [x] T018 [P] Tạo `src/MMW.Domain/Entities/EntryScorecard.cs` và `EntryScorecardLine.cs` (mục 6, 7)
- [x] T019 [P] Tạo `src/MMW.Domain/Entities/MarketContextRecord.cs` (mục 8)
- [x] T020 [P] Tạo `src/MMW.Domain/Entities/KlineArchive.cs` (mục 9)
- [x] T020b [P] Tạo `src/MMW.Domain/Entities/FundingRateArchive.cs` (mục 9b) — *phát sinh từ T001*
- [x] T021 [P] Tạo `src/MMW.Domain/Entities/BacktestRun.cs` (mục 10)
- [x] T022 Thêm `EntryScorecardId` vào `src/MMW.Domain/Entities/Trade.cs`; thêm `DeterministicEngineEnabled` và `ShadowComparisonEnabled` vào `src/MMW.Domain/Entities/AppSetting.cs` (cả hai công tắc mặc định an toàn theo mục 11)
- [x] T023 Tạo 11 lớp cấu hình trong `src/MMW.Domain/DbContext/Configurations/`: precision `(18,8)` cho giá và khối lượng, `(9,4)` cho tỷ lệ, `(9,8)` cho `FundingRateArchive.FundingRate`; khoá duy nhất `(Symbol, Interval, OpenTimeUtc)` cho `KlineArchive`, `(Symbol, FundingTimeUtc)` cho `FundingRateArchive`, `(Symbol, CandleCloseTimeUtc, IsBacktest)` cho `EntryScorecard`, `(TradingAccountId, PlanDateUtc)` cho `DailyPlan`
- [x] T024 Thêm 11 `DbSet` vào `src/MMW.Domain/DbContext/MmwDbContext.cs`
- [x] T025 Sinh migration EF Core trong `src/MMW.Infrastructure/Persistence/Migrations/`; xác nhận áp dụng được trên cơ sở dữ liệu **sạch** (Cổng chất lượng 6)
- [x] T026 Mở rộng `src/MMW.Web/Data/SeedData.cs` (seeder runtime, không phải `HasData`): `EngineSetting` + 6 `SessionQualityRow` + **12** `BlackoutRule` cho mỗi `TradingAccount`, idempotent theo tài khoản, dùng đúng giá trị mặc định trong [data-model.md](./data-model.md)
- [x] T027 [TEST] `tests/MMW.RuleEngine.Tests/EngineSettingTests.cs`: ràng buộc `MinScoreToEnter ≤ ScoreThresholdFull ≤ ScoreThresholdMax`, tổng 3 trọng số nhóm = 85, bảng phiên phủ kín 0–24 không chồng lấn

### 2D — Mở rộng nguồn dữ liệu thị trường

- [x] T028 [P] Tạo `src/MMW.Application/MarketData/Models/FuturesMetrics.cs`: `FundingSnapshot`, `OpenInterestPoint`, `OpenInterestSeries`, `LongShortRatio`, `DepthLevel`, `DepthSnapshot`, `TakerFlow` theo [contracts/market-data.md](./contracts/market-data.md)
- [x] T029 [P] Tạo `src/MMW.Application/MarketData/IMarketSentimentProvider.cs`
- [x] T030 Thêm 5 phương thức mới vào `src/MMW.Application/MarketData/IMarketDataProvider.cs`; **hợp đồng lỗi: trả `null`, không ném ngoại lệ** — *ngoại lệ duy nhất: `period` sai là lỗi lập trình và phải ném (R-003 bẫy B1)*
- [x] T031 [TEST] `tests/MMW.RuleEngine.Tests/BinanceFuturesDataParserTests.cs`: bóc tách 5 dạng phản hồi bằng JSON thật đã ghi lại ở T001, cộng các ca: mảng rỗng, thiếu trường, trường lạ (`rateType`, `CMCCirculatingSupply`) phải bỏ qua được, và **phong bì lỗi phi tiêu chuẩn** `{"status":"ERROR","code":"99099990","errorData":"illegal params."}` trả về ở nơi đang chờ mảng (R-003 bẫy B3)
- [x] T032 Tạo `src/MMW.Infrastructure/Exchanges/Binance/BinanceFuturesDataParser.cs`; phát hiện phản hồi dạng **đối tượng** ở nơi chờ **mảng** và coi là lỗi
- [x] T033 Cài 5 phương thức mới trong `src/MMW.Infrastructure/Exchanges/Binance/BinanceMarketDataProvider.cs`; có timeout, lỗi trả `null` kèm log có cấu trúc mang symbol; `period` kiểm tra với danh sách trắng `5m,15m,30m,1h,2h,4h,6h,12h,1d` và **ném `ArgumentException`** khi sai; `depth` dùng `limit=100`; không bao giờ truyền `startTime` quá 30 ngày cho nhóm `/futures/data/*` (sẽ nhận `400 -1130`)
- [x] T034 [P] Tạo `src/MMW.Infrastructure/MarketSentiment/AlternativeMeFearGreedProvider.cs`; không truy cập được trả `null`
- [x] T035 Đăng ký provider mới trong `src/MMW.Infrastructure/DependencyInjection.cs`

### 2E — Chỉ báo và cấu trúc thị trường

- [x] T036 [TEST] `tests/MMW.RuleEngine.Tests/PercentileTests.cs`: nearest-rank theo R-009, biên 25/75/90, dưới 60 mẫu trả `null`
- [x] T037 Thêm `Percentile` và `PercentileOf` vào `src/MMW.Application/Indicators/IndicatorService.cs` + `IIndicatorService.cs`
- [x] T038 [TEST] `tests/MMW.RuleEngine.Tests/VwapTests.cs`: VWAP neo theo ngày UTC, khởi động lại đúng 00:00, bỏ qua nến chưa đóng
- [x] T039 Thêm `AnchoredVwap` và `VolumeSma` vào `IndicatorService`
- [x] T040 [TEST] `tests/MMW.RuleEngine.Tests/SwingDetectorTests.cs`: điểm xoay fractal `N` nến hai bên, **xác nhận trễ đúng `N` nến**, và một test khẳng định không nhìn trước tương lai — điểm xoay tại `i` chỉ xuất hiện khi đã có `i+N` nến
- [x] T041 Tạo `src/MMW.Application/Trading/Structure/ISwingDetector.cs` + `SwingDetector.cs` theo R-007
- [x] T042 [TEST] `tests/MMW.RuleEngine.Tests/MarketStructureTests.cs`: phá vỡ cấu trúc tăng/giảm, kiểm định lại thành công trong `M` nến, kiểm định lại thất bại, không có phá vỡ
- [x] T043 Tạo `src/MMW.Application/Trading/Structure/MarketStructureAnalyzer.cs`

### 2F — Test gác hiến chương (giữ ranh giới không trôi)

- [x] T044 [TEST] `tests/MMW.RuleEngine.Tests/Constitution/DeterminismGuardTests.cs`: quét reflection toàn bộ `MMW.Application.Trading` và `MMW.Application.Backtest`, khẳng định **không** tham chiếu `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow`, `Random`
- [x] T045 [TEST] `tests/MMW.RuleEngine.Tests/Constitution/NoAiInTradingTests.cs`: không constructor nào trong `MMW.Application.Trading` nhận `ILlmService`
- [x] T046 [TEST] `tests/MMW.RuleEngine.Tests/Constitution/BlockerCountTests.cs`: đếm số lớp chặn trong `LiveOrderService`, chốt con số baseline làm mốc so sánh (SC-010)

**Checkpoint**: nền đã sẵn sàng — các user story có thể bắt đầu

---

## Phase 3: User Story 1 — Chặn theo khung giờ tin mạnh (P1) 🎯 MVP

**Goal**: hệ thống tự biết các khung giờ nguy hiểm từ lịch nội bộ và tự đứng ngoài, kể cả khi không có AI và không có mạng.

**Independent Test**: nạp lịch, tua đồng hồ qua từng mốc trước/trong/sau cửa sổ, xác nhận trạng thái chặn đúng. Không cần tầng chấm điểm, không cần kế hoạch ngày.

### Tests for User Story 1 ⚠️ viết trước, phải đỏ

- [ ] T047 [TEST] [P] [US1] `tests/MMW.RuleEngine.Tests/TimeGuard/DerivedEventGeneratorTests.cs`: 3 mốc thanh toán phí vốn/ngày, đáo hạn tuần thứ Sáu, đáo hạn tháng thứ Sáu **cuối cùng**, khoảng trống Chủ nhật; biên: tháng có 5 thứ Sáu, tuần bắc cầu giao thừa, năm nhuận
- [ ] T048 [TEST] [P] [US1] `tests/MMW.RuleEngine.Tests/TimeGuard/BlackoutWindowTests.cs`: **8 loại sự kiện × 2 test** — một chứng minh chặn thật trong biên, một chứng minh không chặn nhầm ngay ngoài biên (SC-006, 16 test)
- [ ] T049 [TEST] [P] [US1] `tests/MMW.RuleEngine.Tests/TimeGuard/WindowMergeTests.cs`: hai cửa sổ chồng lấn hợp nhất thành một khoảng liên tục (FR-012)
- [ ] T050 [TEST] [P] [US1] `tests/MMW.RuleEngine.Tests/TimeGuard/EmptyCalendarTests.cs`: lịch nạp tay rỗng ⟹ cửa sổ sinh bằng công thức **vẫn cưỡng chế đủ 100%**, và hệ thống phát cảnh báo lịch thiếu (SC-009, FR-014)
- [ ] T051 [TEST] [P] [US1] `tests/MMW.RuleEngine.Tests/TimeGuard/SessionQualityTests.cs`: dùng bảng chuẩn khi dưới 50 lệnh đóng, chuyển sang thống kê cá nhân khi đạt 50, `IsPersonalised` phản ánh đúng
- [ ] T052 [TEST] [P] [US1] `tests/MMW.RuleEngine.Tests/TimeGuard/AiWindowCapTests.cs`: cửa sổ AI đề xuất dài 20 tiếng bị cắt về `AiBlackoutMaxMinutes` (FR-011)

### Implementation for User Story 1

- [ ] T053 [P] [US1] Tạo `src/MMW.Application/Trading/TimeGuard/BlackoutDecision.cs`: `BlackoutDecision`, `BlackoutWindow` theo [contracts/timeguard.md](./contracts/timeguard.md)
- [ ] T054 [P] [US1] Tạo `IDerivedEventGenerator.cs` + `DerivedEventGenerator.cs` trong `src/MMW.Application/Trading/TimeGuard/` — **hàm thuần**, không I/O, không đồng hồ
- [ ] T055 [P] [US1] Tạo `IScheduledEventCalendar.cs` + `ScheduledEventCalendar.cs`; `ImportAsync` bất biến theo `SourceKey`
- [ ] T056 [US1] Tạo `ISessionQualityProvider.cs` + `SessionQualityProvider.cs`; đọc ngưỡng `PersonalStatsMinClosedTrades` từ `EngineSetting`
- [ ] T057 [US1] Tạo `ITimeGuardService.cs` + `TimeGuardService.cs`: `CheckAsync`, `GetWindowsAsync` (có hợp nhất), `GetUpcomingAsync`; mọi thời gian qua `IClock`
- [ ] T058 [US1] Ghi vết cấu trúc mọi lần chặn — loại sự kiện, thời điểm, biên cửa sổ, thời điểm đánh giá (FR-015); thông điệp tiếng Việt nêu giờ Việt Nam
- [ ] T059 [US1] Cảnh báo lịch quá hạn khi `MAX(OccursAtUtc) WHERE Origin = Seeded` đã ở quá khứ, phát qua `INotificationService` (FR-014)
- [ ] T060 [US1] Seed `ScheduledEvent` phần còn lại của năm 2026 từ lịch công bố của BLS và Fed vào `src/MMW.Infrastructure/Persistence/SeedData.cs` (R-005)
- [ ] T061 [TEST] [US1] `tests/MMW.RuleEngine.Tests/TimeGuard/PositionManageTests.cs`: vị thế lãi ≥0.5R khi sắp vào blackout → kéo dừng lỗ về hoà vốn; lãi dưới ngưỡng → đóng một nửa; **không trường hợp nào để nguyên trạng** (FR-013)
- [ ] T062 [US1] Tạo `src/MMW.Application/Services/PositionManageService.cs`: rà soát vị thế đang mở, xử lý giảm rủi ro khi blackout sắp bắt đầu, phát thông báo; cảnh báo khi đồng hồ máy chủ lệch sàn quá 30 giây
- [ ] T063 [US1] Đăng ký job Hangfire `position-manage` cron `*/1 * * * *` với `[DisableConcurrentExecution]` trong `src/MMW.Web/Program.cs`
- [ ] T064 [US1] Đăng ký DI cho toàn bộ service TimeGuard trong `src/MMW.Application/DependencyInjection.cs`
- [ ] T065 [US1] Tạo `src/MMW.Web/Controllers/TimeGuardController.cs` + view: xem lịch sự kiện, các cửa sổ chặn 48 giờ tới, và trạng thái quá hạn của lịch

**Checkpoint**: US1 hoạt động độc lập — chạy `dotnet test --filter "FullyQualifiedName~TimeGuard"` phải xanh toàn bộ. Kịch bản 1 của [quickstart.md](./quickstart.md) đạt.

---

## Phase 4: User Story 2 — Kế hoạch ngày ràng buộc cả ngày (P1)

**Goal**: mỗi ngày UTC có đúng một bản kế hoạch quyết định chiều được phép, hệ số rủi ro và số lệnh tối đa.

**Independent Test**: cho dữ liệu giá của các ngày có tính chất khác nhau, xác nhận kế hoạch khớp bảng FR-019. Không cần tầng chấm điểm.

### Tests for User Story 2 ⚠️

- [ ] T066 [TEST] [P] [US2] `tests/MMW.RuleEngine.Tests/DailyPlanning/RegimeClassifierTests.cs`: đủ 5 dòng bảng FR-019, mỗi dòng một test
- [ ] T067 [TEST] [P] [US2] `tests/MMW.RuleEngine.Tests/DailyPlanning/RegimeMergeTests.cs`: nhiều dòng cùng khớp → lấy `MIN` hệ số, `MIN` số lệnh, **giao** của các chiều được phép (FR-020); ca kiểm chứng TrendUp + Extreme + ngày có tin ⟹ `0.3 / 2 lệnh / LongOnly`
- [ ] T068 [TEST] [P] [US2] `tests/MMW.RuleEngine.Tests/DailyPlanning/MissingInputPenaltyTests.cs`: thiếu bất kỳ đầu vào nào ⟹ `RiskMultiplier ≤ 0.5`; `Classify` **không ném ngoại lệ** khi thiếu dữ liệu (bất biến 6)
- [ ] T069 [TEST] [P] [US2] `tests/MMW.RuleEngine.Tests/DailyPlanning/DailyPlanIdempotencyTests.cs`: gọi `GenerateAsync` hai lần cùng ngày trả cùng `Id`, không ghi đè
- [ ] T070 [TEST] [P] [US2] `tests/MMW.RuleEngine.Tests/DailyPlanning/NoPlanBlocksTests.cs`: chưa có kế hoạch hợp lệ ⟹ mọi lệnh mới bị chặn với `VetoReason.NoDailyPlan`; **không tồn tại đường dẫn nào trả kế hoạch mặc định cho phép giao dịch** (FR-023)

### Implementation for User Story 2

- [ ] T071 [P] [US2] Tạo `src/MMW.Application/Trading/DailyPlanning/DailyPlanInputs.cs`: `DailyPlanInputs`, `RegimeClassification`
- [ ] T072 [US2] Tạo `IDayRegimeClassifier.cs` + `DayRegimeClassifier.cs` — **hàm thuần**; 5 bước theo [contracts/daily-plan.md](./contracts/daily-plan.md)
- [ ] T073 [US2] Tạo `IDailyPlanService.cs` + `DailyPlanService.cs`: thu thập đầu vào (nguồn nào lỗi thì ghi `MissingInputs`, không đổ vỡ), gọi classifier, lưu bất biến theo `(TradingAccountId, PlanDateUtc)`
- [ ] T074 [US2] Đăng ký job Hangfire `daily-plan` cron `30 23 * * *` trong `src/MMW.Web/Program.cs`; kèm một lần chạy bù khi ứng dụng khởi động giữa ngày mà chưa có kế hoạch
- [ ] T075 [US2] Đăng ký DI trong `src/MMW.Application/DependencyInjection.cs`
- [ ] T076 [US2] Tạo `src/MMW.Web/Controllers/DailyPlanController.cs` + view: kế hoạch hôm nay, các đầu vào đã dùng, thành phần bị thiếu, và lịch sử 30 ngày

**Checkpoint**: US1 + US2 cùng hoạt động độc lập. Kịch bản 2 phần kế hoạch ngày đạt.

---

## Phase 5: User Story 3 — Chấm điểm tất định (P1)

**Goal**: điểm 0–100 từ 13 tiêu chí quyết định vào lệnh và kích thước, không mô hình ngôn ngữ nào tham gia.

**Independent Test**: cho tập trạng thái dựng sẵn, xác nhận điểm đúng bảng trọng số và cùng đầu vào luôn cho cùng đầu ra. Chạy offline.

### Tests for User Story 3 ⚠️

- [ ] T077 [TEST] [P] [US3] `tests/MMW.RuleEngine.Tests/Scoring/TechnicalCriteriaTests.cs`: 5 tiêu chí nhóm Technical, mỗi tiêu chí 3 ca (điểm tối đa / 0 điểm / thiếu dữ liệu); riêng `entry_location` thêm ca giá đã chạy quá 1.5 ATR ⟹ **0 điểm** (FR-027); `htf_alignment` thêm ca ngược kế hoạch ngày ⟹ **veto cứng**
- [ ] T078 [TEST] [P] [US3] `tests/MMW.RuleEngine.Tests/Scoring/MarketCriteriaTests.cs`: 5 tiêu chí nhóm Market, mỗi tiêu chí 3 ca; `day_regime_match` thêm ca veto cứng
- [ ] T079 [TEST] [P] [US3] `tests/MMW.RuleEngine.Tests/Scoring/LiquidityCriteriaTests.cs`: 3 tiêu chí nhóm Liquidity; `zone_position` luôn trả `IsApproximation = true`; ca cụm thanh khoản nằm ngay ngoài dừng lỗ ⟹ trừ về 0
- [ ] T080 [TEST] [P] [US3] `tests/MMW.RuleEngine.Tests/Scoring/MissingDataTests.cs`: mọi tiêu chí có `DataAvailable = false` ⟹ `AwardedPoints = 0`, **không** phải điểm trung bình hay điểm tối đa (FR-006)
- [ ] T081 [TEST] [P] [US3] `tests/MMW.RuleEngine.Tests/Scoring/EntryScorerTests.cs`: dừng sớm khi gặp veto cứng đầu tiên — các tiêu chí sau **không chạy**, phiếu ghi đúng **một** lý do; thứ tự duyệt tất định
- [ ] T082 [TEST] [P] [US3] `tests/MMW.RuleEngine.Tests/Scoring/DeterminismTests.cs`: chấm cùng một `ScoringContext` **100 lần** cho ra 100 kết quả giống hệt đến từng chữ số (SC-002)
- [ ] T083 [TEST] [P] [US3] `tests/MMW.RuleEngine.Tests/Scoring/PositionSizerTests.cs`: bảng ngưỡng 55/70/85; `finalSizeR ≤ baseSizeR` luôn đúng; điểm dưới ngưỡng hoặc bị veto ⟹ `finalSizeR = 0`; ca 88 điểm × hệ số ngày 0.3 ⟹ 0.45R
- [ ] T084 [TEST] [P] [US3] `tests/MMW.RuleEngine.Tests/Scoring/NoAiTests.cs`: `ILlmService.IsConfigured = false` ⟹ `SignalEvalService` chạy trọn một chu kỳ, sinh phiếu đầy đủ, không ngoại lệ (SC-001)
- [ ] T085 [TEST] [P] [US3] `tests/MMW.RuleEngine.Tests/Scoring/NoThresholdRelaxationTests.cs`: cả ngày không setup nào đạt 55 điểm ⟹ **0 lệnh**, và không bản ghi nào cho thấy ngưỡng bị hạ (FR-038)
- [ ] T086 [TEST] [P] [US3] `tests/MMW.RuleEngine.Tests/Scoring/ScorecardPersistenceTests.cs`: phiếu chấm điểm được lưu **kể cả khi không vào lệnh**; khoá duy nhất `(Symbol, CandleCloseTimeUtc)` chặn sinh trùng khi job chạy chồng lấn (FR-051, SC-012)

### Implementation for User Story 3

- [ ] T087 [US3] Tạo `src/MMW.Application/Trading/Scoring/IScoreCriterion.cs`, `ScoringContext.cs`, `CriterionResult.cs` theo [contracts/scoring.md](./contracts/scoring.md)
- [ ] T088 [P] [US3] `Criteria/HtfAlignmentCriterion.cs` — `technical.htf_alignment`, 10đ, veto cứng khi ngược kế hoạch ngày
- [ ] T089 [P] [US3] `Criteria/MarketStructureCriterion.cs` — `technical.market_structure`, 10đ, dùng `MarketStructureAnalyzer`
- [ ] T090 [P] [US3] `Criteria/EntryLocationCriterion.cs` — `technical.entry_location`, 8đ, dùng VWAP neo ngày và EMA20
- [ ] T091 [P] [US3] `Criteria/MomentumCriterion.cs` — `technical.momentum`, 7đ
- [ ] T092 [P] [US3] `Criteria/VolumeConfirmationCriterion.cs` — `technical.volume_confirmation`, 5đ
- [ ] T093 [P] [US3] `Criteria/DayRegimeMatchCriterion.cs` — `market.day_regime_match`, 10đ, veto cứng khi ngược trạng thái ngày
- [ ] T094 [P] [US3] `Criteria/VolatilityRegimeCriterion.cs` — `market.volatility_regime`, 6đ, phân vị 30–70 được điểm tối đa
- [ ] T095 [P] [US3] `Criteria/SessionQualityCriterion.cs` — `market.session_quality`, 6đ, dùng `ISessionQualityProvider` từ US1
- [ ] T096 [P] [US3] `Criteria/LeaderCorrelationCriterion.cs` — `market.leader_correlation`, 4đ
- [ ] T097 [P] [US3] `Criteria/FundingCrowdingCriterion.cs` — `market.funding_crowding`, 4đ, funding cực đoan **cùng chiều lệnh** thì trừ
- [ ] T098 [P] [US3] `Criteria/OpenInterestCriterion.cs` — `liquidity.open_interest`, 5đ
- [ ] T099 [P] [US3] `Criteria/LiquidityZoneCriterion.cs` — `liquidity.zone_position`, 5đ, luôn `IsApproximation = true` theo R-010
- [ ] T100 [P] [US3] `Criteria/SpreadDepthCriterion.cs` — `liquidity.spread_depth`, 5đ
- [ ] T101 [US3] Tạo `src/MMW.Application/Trading/Scoring/IEntryScorer.cs` + `EntryScorer.cs`: nhận `IEnumerable<IScoreCriterion>` từ DI, duyệt theo `(Group, Key)`, **dừng ngay ở veto cứng đầu tiên**
- [ ] T102 [US3] Tạo `src/MMW.Application/Trading/Sizing/IPositionSizer.cs` + `ScoreBasedPositionSizer.cs`; ba hệ số nhân đều `≤ 1.0` để bất biến `finalSizeR ≤ baseSizeR` thành tính chất số học
- [ ] T103 [US3] Tạo `src/MMW.Application/Services/SignalEvalService.cs`: dựng `ScoringContext` (gọi `ClosedOnly()`), gọi TimeGuard → EntryScorer → PositionSizer, lưu `EntryScorecard` + `EntryScorecardLine` **mọi lần**, tạo lệnh khi đạt ngưỡng
- [ ] T104 [US3] Đăng ký job Hangfire `signal-eval` cron `1,16,31,46 * * * *` (trễ 1 phút theo R-011) với `[DisableConcurrentExecution]`; gỡ `market-scan` `*/5` khỏi vai trò sinh lệnh trong `src/MMW.Web/Program.cs`
- [ ] T105 [US3] Đăng ký 13 tiêu chí + scorer + sizer trong `src/MMW.Application/DependencyInjection.cs`
- [ ] T106 [US3] Tạo `src/MMW.Web/Controllers/ScorecardController.cs` + view: danh sách phiếu chấm điểm, chi tiết điểm từng tiêu chí, bộ lọc theo lý do từ chối; tra được lý do một cơ hội bị từ chối trong dưới 30 giây (SC-013)

**Checkpoint**: US1 + US2 + US3 — lõi tất định đã chạy được đầu-cuối, hoàn toàn không cần AI. Kịch bản 2, 3, 7 của quickstart đạt.

---

## Phase 6: User Story 4 — Kỷ luật chặn cứng (P2)

**Goal**: nâng ba bộ phát hiện hành vi từ cảnh báo lên rào chắn thật, thêm ba gate mới.

**Independent Test**: dựng lịch sử lệnh giả với các mẫu hành vi, xác nhận từng gate chặn đúng và không chặn nhầm ở ngay dưới ngưỡng.

### Tests for User Story 4 ⚠️

- [ ] T107 [TEST] [P] [US4] `tests/MMW.RuleEngine.Tests/Discipline/LossStreakGateTests.cs`: 2 thua liên tiếp ⟹ nhân 0.5; 3 thua ⟹ dừng ngày; 1 thua ⟹ không tác động
- [ ] T108 [TEST] [P] [US4] `tests/MMW.RuleEngine.Tests/Discipline/DailyLossLimitGateTests.cs`: chạm ngưỡng ⟹ dừng ngày; ngay dưới ngưỡng ⟹ cho qua
- [ ] T109 [TEST] [P] [US4] `tests/MMW.RuleEngine.Tests/Discipline/RevengeWindowGateTests.cs`: 10 phút sau lệnh thua ⟹ chặn; 20 phút ⟹ không chặn
- [ ] T110 [TEST] [P] [US4] `tests/MMW.RuleEngine.Tests/Discipline/OversizedGateTests.cs`: vượt 1.5× trung bình 20 lệnh ⟹ chặn; đúng 1.5× ⟹ không chặn
- [ ] T111 [TEST] [P] [US4] `tests/MMW.RuleEngine.Tests/Discipline/MaxTradesGateTests.cs`: đủ `DailyPlan.MaxTradesToday` ⟹ chặn; thiếu một lệnh ⟹ cho qua
- [ ] T112 [TEST] [P] [US4] `tests/MMW.RuleEngine.Tests/Discipline/WorstHoursGateTests.cs`: đủ 50 lệnh và giờ nằm trong top-2 tệ nhất ⟹ trừ 10 điểm; dưới 50 lệnh ⟹ `Allow` với phạt 0, **không** thưởng điểm
- [ ] T113 [TEST] [P] [US4] `tests/MMW.RuleEngine.Tests/Discipline/GateInvariantTests.cs`: quét mọi `IDisciplineGate` đăng ký, khẳng định **không gate nào** trả `SizeMultiplier > 1.0`
- [ ] T114 [TEST] [P] [US4] `tests/MMW.RuleEngine.Tests/Discipline/DayResetTests.cs`: bộ đếm số lệnh và trạng thái dừng-ngày reset tại 00:00 UTC; vị thế đang mở **không** bị ảnh hưởng

### Implementation for User Story 4

- [ ] T115 [US4] Tạo `src/MMW.Application/Trading/Discipline/IDisciplineGate.cs`, `DisciplineContext.cs`, `GateResult.cs`, `GateAggregate.cs`
- [ ] T116 [P] [US4] `Gates/LossStreakGate.cs` — tái dùng logic `LossStreakDetector` hiện có, đọc ngưỡng từ `EngineSetting`
- [ ] T117 [P] [US4] `Gates/DailyLossLimitGate.cs` — đọc `RiskSetting.MaxDailyLossPercent`
- [ ] T118 [P] [US4] `Gates/RevengeWindowGate.cs` — đọc `EngineSetting.RevengeBlockMinutes` (tách khỏi ngưỡng cảnh báo 30 phút của `RiskSetting`)
- [ ] T119 [P] [US4] `Gates/OversizedGate.cs` — tái dùng logic `OversizedAfterLossDetector`
- [ ] T120 [P] [US4] `Gates/MaxTradesGate.cs`
- [ ] T121 [P] [US4] `Gates/WorstHoursGate.cs` — chỉ hoạt động khi đủ `PersonalStatsMinClosedTrades`
- [ ] T122 [US4] Tạo `DisciplineGateRunner.cs`: gộp kết quả các gate, `StopForDay` và `BlockTrade` thắng mọi thứ, `SizeMultiplier` lấy tích
- [ ] T123 [US4] Cắm `DisciplineGateRunner` vào `SignalEvalService` trước bước sizing; ghi kết quả từng gate thành `EntryScorecardLine` nhóm `Discipline`
- [ ] T124 [US4] Đăng ký 6 gate + runner trong `src/MMW.Application/DependencyInjection.cs`
- [ ] T125 [US4] Hiển thị trạng thái dừng-ngày và các gate đang kích hoạt trên trang chủ, kèm số liệu thực tế so với ngưỡng (Nguyên tắc I)

**Checkpoint**: kịch bản 5 của quickstart đạt — 12 test gate + 1 test bất biến xanh.

---

## Phase 7: User Story 5 — Kiểm thử lịch sử tái lập được (P2)

**Goal**: chạy lại tầng 1–3 trên dữ liệu lịch sử, hoàn toàn offline, dùng đúng cùng bộ mã với chạy thật.

**Independent Test**: chuỗi quyết định từ kiểm thử lịch sử và từ chế độ mô phỏng trên cùng dữ liệu phải trùng khớp.

### Tests for User Story 5 ⚠️

- [ ] T126 [TEST] [P] [US5] `tests/MMW.RuleEngine.Tests/Backtest/KlineArchiveTests.cs`: `BackfillAsync` bất biến (nạp lại cùng khoảng không sinh trùng); `FindGapsAsync` phát hiện đúng nến thiếu
- [ ] T127 [TEST] [P] [US5] `tests/MMW.RuleEngine.Tests/Backtest/NoLookAheadTests.cs`: `ArchiveMarketDataProvider.GetCandlesAsync` **không bao giờ** trả nến có `CloseTime > clock.UtcNow`; `BacktestClock.Advance` lùi về quá khứ ⟹ ném ngoại lệ
- [ ] T128 [TEST] [P] [US5] `tests/MMW.RuleEngine.Tests/Backtest/BacktestParityTests.cs`: chuỗi `EntryScorecard` từ `BacktestEngine` **trùng khớp mọi trường** với chuỗi từ `SignalEvalService` chạy chế độ mô phỏng trên cùng dữ liệu (SC-003) — *test đắt nhất và giá trị nhất của feature*. Danh sách loại trừ **có tên rõ ràng** và chỉ đúng một phần tử: `market.funding_crowding` (dự phóng vs đã thanh toán, mục 9b). Loại trừ bằng cách nới lỏng phép so sánh là **sai** — phải là danh sách khoá tường minh, và một test riêng khẳng định danh sách đó không dài hơn một phần tử
- [ ] T129 [TEST] [P] [US5] `tests/MMW.RuleEngine.Tests/Backtest/ReportTests.cs`: `Limitations` **không được rỗng**; phí và trượt giá được tính vào kết quả; ca một nến chạm cả dừng lỗ và chốt lời ⟹ giả định **dừng lỗ khớp trước**

### Implementation for User Story 5

- [ ] T130 [P] [US5] Tạo `src/MMW.Application/Backtest/BacktestClock.cs`; `Advance` chỉ tiến, lùi thì ném
- [ ] T131 [P] [US5] Tạo `IKlineArchiveService.cs` + `KlineArchiveService.cs`: `BackfillAsync`, `GetRangeAsync`, `FindGapsAsync`
- [ ] T131b [P] [US5] Thêm `BackfillFundingAsync` và `GetFundingAtAsync` vào `KlineArchiveService`: nạp `/fapi/v1/fundingRate` theo trang **500 bản ghi/lần** (không phải 1000), bất biến chống trùng theo `(Symbol, FundingTimeUtc)` — *phát sinh từ T001*
- [ ] T132 [US5] Tạo `src/MMW.Application/Backtest/ArchiveMarketDataProvider.cs` cài `IMarketDataProvider`; lọc `CloseTime <= clock.UtcNow`; **`GetFundingAsync` đọc từ `FundingRateArchive`**; `GetOpenInterestHistAsync` / `GetGlobalLongShortRatioAsync` / `GetDepthAsync` / `GetTakerBuySellRatioAsync` trả `null` theo R-003
- [ ] T133 [US5] Tạo `src/MMW.Application/Backtest/Models/BacktestReport.cs`
- [ ] T134 [US5] Tạo `IBacktestEngine.cs` + `BacktestEngine.cs` theo vòng lặp trong [contracts/backtest.md](./contracts/backtest.md); **từ chối chạy** khi `FindGapsAsync` trả về khoảng thiếu
- [ ] T135 [US5] Cài mô hình phí và trượt giá theo R-012, đọc từ `EngineSetting`
- [ ] T136 [US5] Sinh `Limitations` tự động: **10/100 điểm** bị mất (`liquidity.open_interest` + `liquidity.spread_depth`), phí vốn dùng tỷ lệ **đã thanh toán** thay cho tỷ lệ **dự phóng**, giả định dừng lỗ khớp trước, phí và trượt giá đã dùng, số nến thiếu
- [ ] T137 [US5] Thêm lệnh CLI `backfill` vào `src/MMW.Web/Program.cs` để nạp kho nến **và kho phí vốn** theo tham số
- [ ] T138 [US5] Tạo `src/MMW.Web/Controllers/BacktestController.cs` + view: chạy kiểm thử, xem báo cáo, phân rã theo giờ và theo trạng thái ngày; **hiển thị `Limitations` nổi bật ngay cạnh các con số kết quả**
- [ ] T139 [US5] Job snapshot dữ liệu phái sinh hàng giờ để dựng dần kho lịch sử cho kiểm thử đầy đủ 100 điểm về sau (giảm thiểu rủi ro R-003)

**Checkpoint**: kịch bản 6 của quickstart đạt. Đây là lúc **lần đầu tiên** biết được thuật toán có lợi thế hay không.

---

## Phase 8: User Story 6 — AI chỉ được nói "không" (P2)

**Goal**: đưa AI về vai trò lớp bối cảnh, cưỡng chế bằng kiểu dữ liệu và số học chứ không bằng lời nhắc trong prompt.

**Independent Test**: cho AI trả mọi kiểu phản hồi dị thường, xác nhận không phản hồi nào làm quyết định rủi ro hơn.

### Tests for User Story 6 ⚠️

- [ ] T140 [TEST] [P] [US6] `tests/MMW.RuleEngine.Tests/Ai/MarketContextApplierTests.cs`: đủ **12 trường hợp** trong [contracts/ai-context.md](./contracts/ai-context.md); đặc biệt ca 10 — bối cảnh `critical` **thuận chiều** lệnh ⟹ hệ số đúng `1.0`, không lớn hơn
- [ ] T141 [TEST] [P] [US6] `tests/MMW.RuleEngine.Tests/Ai/DailyBriefValidationTests.cs`: 6 kiểm chứng phía nhận — cắt `confidence` về 0.8, loại sự kiện bịa, cắt cửa sổ quá dài, loại toàn bộ phản hồi khi có khoá gợi ý lệnh, JSON hỏng ⟹ bối cảnh trung tính
- [ ] T142 [TEST] [P] [US6] `tests/MMW.RuleEngine.Tests/Ai/NewsClassifierValidationTests.cs`: `isRumor` ⟹ severity trần `medium`; `halfLifeMinutes` cắt về `[0, 1440]`; không rõ ràng ⟹ `noise`
- [ ] T143 [TEST] [P] [US6] `tests/MMW.RuleEngine.Tests/Ai/EnrichGuardTests.cs`: `EnrichAsync` với phản hồi cố tình vi phạm ⟹ `RiskMultiplier`, `MaxTradesToday`, `AllowedDirections` **không đổi** (FR-041)
- [ ] T144 [TEST] [P] [US6] `tests/MMW.RuleEngine.Tests/Ai/CallBudgetTests.cs`: mô phỏng một ngày giao dịch ⟹ tổng lần gọi AI **< 30** (SC-005); vòng `signal-eval` và `position-manage` gọi **0** lần (FR-049)

### Implementation for User Story 6

- [ ] T145 [P] [US6] Tạo `src/MMW.Infrastructure/Ai/ClaudeLlmService.cs` cài `ILlmService`; thêm nhánh `Claude` vào switch provider trong `src/MMW.Infrastructure/DependencyInjection.cs`; khoá đọc từ User Secrets, không vào `appsettings.json`
- [ ] T146 [P] [US6] Tạo `src/MMW.Application/Ai/Prompts/DailyBriefPrompt.cs` với đủ 5 ràng buộc tuyệt đối trong contract
- [ ] T147 [P] [US6] Tạo `src/MMW.Application/Ai/Prompts/NewsClassifierPrompt.cs`
- [ ] T148 [US6] Tạo `src/MMW.Application/Ai/MarketContextApplier.cs`: trả hệ số trong `[0, 1]`; chỉ áp khi bối cảnh liên quan symbol **và ngược chiều lệnh**
- [ ] T149 [US6] Tạo `IMarketContextService.cs` + `MarketContextService.cs`: `GetActiveAsync` lọc theo `ExpiresAtUtc`, `RunDailyBriefAsync`, `ClassifyNewsAsync` (chỉ xử lý `SourceKey` mới)
- [ ] T150 [US6] Cài 6 bước kiểm chứng phản hồi Daily Brief và 4 bước của News Classifier; mọi trường bị loại ghi vào `MarketContextRecord.RejectedFields`
- [ ] T151 [US6] Tạo `IDailyBriefEnricher` + cài đặt; chữ ký nhận `DailyPlan` **đã hoàn chỉnh**, chỉ ghi 3 trường `Ai*`
- [ ] T152 [US6] Cắm `MarketContextApplier` vào `SignalEvalService` như hệ số thứ tư của công thức sizing
- [ ] T153 [US6] Đăng ký job Hangfire `news-scan` cron `*/15 * * * *` trong `src/MMW.Web/Program.cs`; nối vào job `daily-plan` bước làm giàu bằng AI
- [ ] T154 [US6] Đăng ký DI cho lớp AI trong `src/MMW.Application/DependencyInjection.cs`

**Checkpoint**: kịch bản 4 của quickstart đạt — 12 test chống lạm quyền xanh.

---

## Phase 9: User Story 7 — So sánh song song (P3)

**Goal**: đường AI cũ vẫn chạy nhưng chỉ ghi nhật ký, tạo dữ liệu so sánh khách quan.

**Independent Test**: chạy một chu kỳ, xác nhận cả hai đường đều để lại bản ghi và **chỉ** đường tất định tạo lệnh.

### Tests for User Story 7 ⚠️

- [ ] T155 [TEST] [P] [US7] `tests/MMW.RuleEngine.Tests/Ai/ShadowModeTests.cs`: đường AI để lại `AiSignalScanRecord` nhưng **không** tạo `Trade` nào; tắt `ShadowComparisonEnabled` ⟹ không lần gọi AI nào cho mục đích sinh tín hiệu
- [ ] T156 [TEST] [P] [US7] `tests/MMW.RuleEngine.Tests/Ai/DisagreementTests.cs`: AI đề xuất vào lệnh còn đường tất định từ chối ⟹ điểm bất đồng được ghi nhận

### Implementation for User Story 7

- [ ] T157 [US7] Sửa `src/MMW.Application/Services/MarketScanService.cs`: gỡ `AutoCreateTradeFromSignalAsync` và lời gọi `_liveOrders.PlaceForTradeAsync` khỏi đường AI; giữ nguyên `GenerateAiSignalAsync` và bản ghi kiểm toán (FR-057)
- [ ] T158 [US7] Thêm cột ghi nhận điểm bất đồng giữa hai đường vào `AiSignalScanRecord`, kèm migration
- [ ] T159 [US7] Bọc toàn bộ đường AI sau công tắc `AppSetting.ShadowComparisonEnabled`
- [ ] T160 [US7] Tạo trang báo cáo so sánh: số đề xuất mỗi bên, số điểm bất đồng, kết quả giả định của các đề xuất bên AI nếu đã thực thi (FR-060)

**Checkpoint**: toàn bộ 7 user story hoạt động độc lập.

---

## Phase 10: Polish & Cross-Cutting

- [ ] T161 [P] Cập nhật `SYSTEM_OVERVIEW.md` — tài liệu này đã lạc hậu từ trước feature (ghi nhận trong Sync Impact Report của hiến chương); bổ sung engine mới, 4 job, và các bảng mới
- [ ] T162 [P] Cập nhật `README.md`: hướng dẫn nạp kho nến và chạy kiểm thử lịch sử
- [ ] T163 Chạy toàn bộ [quickstart.md](./quickstart.md) — cả 8 kịch bản phải đạt
- [ ] T164 Kiểm tra hiệu năng: `signal-eval` < 10 giây, `position-manage` < 5 giây, kiểm thử 2 năm < 5 phút, `daily-plan` < 30 giây
- [ ] T165 Rà bí mật: không khoá nào trong migration, seed, `EntryScorecard.InputSnapshotJson`, `MarketContextRecord.RawResponseJson`, hay log (Cổng chất lượng 5)
- [ ] T166 Xác nhận `LiveTrading:Enabled = false` và `UseTestnet = true` sau toàn bộ thay đổi (SC-014)
- [ ] T167 Chạy `dotnet build --configuration Release` — không lỗi, **không cảnh báo mới** (Cổng chất lượng 2)
- [ ] T168 Bật `AppSetting.DeterministicEngineEnabled = true` và chạy **7 ngày mô phỏng** theo mục Nghiệm thu vận hành của quickstart

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 Setup** — không phụ thuộc, bắt đầu ngay
- **Phase 2 Foundational** — phụ thuộc Phase 1, **CHẶN toàn bộ user story**
- **Phase 3–9 User Stories** — đều phụ thuộc Phase 2
- **Phase 10 Polish** — phụ thuộc mọi story đã chọn làm

### User Story Dependencies

| Story | Phụ thuộc | Ghi chú |
|---|---|---|
| US1 TimeGuard (P1) | Chỉ Phase 2 | **Độc lập hoàn toàn** — đây là MVP |
| US2 DailyPlan (P1) | Chỉ Phase 2 | Độc lập với US1 |
| US3 EntryScore (P1) | Phase 2 + **US1** (chất lượng phiên) + **US2** (kế hoạch ngày) | Phụ thuộc thật, không tránh được |
| US4 Discipline (P2) | Phase 2 + US3 (cần chỗ cắm vào) | |
| US5 Backtest (P2) | Phase 2 + US1 + US2 + US3 | Cần đủ tầng 1–3 để chạy lại |
| US6 AI context (P2) | Phase 2 + US2 (làm giàu kế hoạch) + US3 (hệ số sizing) | |
| US7 Shadow (P3) | Phase 2 + US3 | Chỉ cần đường tất định tồn tại để so sánh |

### Bên trong mỗi story

Test đỏ trước → thực thể → service → cắm vào luồng → giao diện.

### Parallel Opportunities

- Phase 1: T002, T003, T004 song song
- Phase 2: T014–T021 (8 thực thể) song song; T028/T029, T034 song song; các cặp test/impl của 2E chạy tuần tự trong cặp nhưng các cặp song song với nhau
- Phase 3: T047–T052 (6 tệp test) song song; T053, T054, T055 song song
- Phase 4: T066–T070 (5 tệp test) song song
- Phase 5: T077–T086 (10 tệp test) song song; **T088–T100 (13 tiêu chí) song song hoàn toàn** — mỗi tiêu chí một tệp, không phụ thuộc nhau
- Phase 6: T107–T114 song song; T116–T121 (6 gate) song song
- Phase 7: T126–T129 song song; T130, T131 song song
- Phase 8: T140–T144 song song; T145, T146, T147 song song

**US1 và US2 làm song song được hoàn toàn** nếu có hai người.

---

## Parallel Example: Phase 5 — 13 tiêu chí chấm điểm

```bash
# Sau khi T087 (hợp đồng IScoreCriterion) xong, cả 13 tiêu chí chạy song song:
Task: "Criteria/HtfAlignmentCriterion.cs"        # T088
Task: "Criteria/MarketStructureCriterion.cs"     # T089
Task: "Criteria/EntryLocationCriterion.cs"       # T090
Task: "Criteria/MomentumCriterion.cs"            # T091
Task: "Criteria/VolumeConfirmationCriterion.cs"  # T092
Task: "Criteria/DayRegimeMatchCriterion.cs"      # T093
Task: "Criteria/VolatilityRegimeCriterion.cs"    # T094
Task: "Criteria/SessionQualityCriterion.cs"      # T095
Task: "Criteria/LeaderCorrelationCriterion.cs"   # T096
Task: "Criteria/FundingCrowdingCriterion.cs"     # T097
Task: "Criteria/OpenInterestCriterion.cs"        # T098
Task: "Criteria/LiquidityZoneCriterion.cs"       # T099
Task: "Criteria/SpreadDepthCriterion.cs"         # T100
```

Đây là lợi ích trực tiếp của hợp đồng plug-in ở Nguyên tắc V: 13 đơn vị công việc hoàn toàn không đụng nhau.

---

## Implementation Strategy

### MVP trước (chỉ US1)

1. Phase 1 Setup
2. Phase 2 Foundational — **bắt buộc, chặn mọi thứ**
3. Phase 3 US1 TimeGuard
4. **DỪNG VÀ KIỂM CHỨNG**: kịch bản 1 của quickstart

Dừng ở đây vẫn có giá trị thật: một cái chặn tự động 60 phút quanh giờ CPI dùng được ngay, kể cả khi vẫn vào lệnh tay.

### Giao tăng dần

| Mốc | Nội dung | Giá trị đạt được |
|---|---|---|
| 1 | Setup + Foundational | Nền tất định, nến đã đóng, dữ liệu mở rộng |
| 2 | + US1 | **MVP** — chặn theo khung giờ, dùng được ngay |
| 3 | + US2 | Kế hoạch ngày ràng buộc |
| 4 | + US3 | **Lõi tất định chạy đầu-cuối, không cần AI** |
| 5 | + US4 | Kỷ luật thành rào chắn |
| 6 | + US5 | **Lần đầu biết được có lợi thế hay không** |
| 7 | + US6 | AI trở lại đúng vai trò |
| 8 | + US7 | Dữ liệu so sánh khách quan |

Mốc 6 là mốc quan trọng nhất về mặt ra quyết định: trước nó, mọi phán đoán về hiệu quả chiến lược đều là phỏng đoán.

### Ước lượng thời gian (làm ngoài giờ)

| Phase | Ước lượng |
|---|---|
| 1–2 Setup + Foundational | 1.5–2 tuần |
| 3 US1 | 1 tuần |
| 4 US2 | 1 tuần |
| 5 US3 | 1.5 tuần |
| 6 US4 | 3–4 ngày |
| 7 US5 | 1.5–2 tuần |
| 8 US6 | 3–4 ngày |
| 9 US7 | 2–3 ngày |
| 10 Polish | 3 ngày + 7 ngày chạy mô phỏng |
| **Tổng** | **~7–8 tuần** |

---

## Notes

- `[P]` = khác tệp, không phụ thuộc lẫn nhau
- Task `[TEST]` **phải đỏ** trước khi task cài đặt tương ứng bắt đầu — Nguyên tắc VI, không phải khuyến nghị
- Commit sau mỗi task hoặc mỗi nhóm hợp lý
- Dừng ở bất kỳ Checkpoint nào để kiểm chứng story độc lập
- Ba task dễ bị bỏ qua nhưng có giá trị cao nhất: **T044** (gác tính tất định), **T128** (tương đương kiểm thử ↔ chạy thật), **T140 ca 10** (bối cảnh AI thuận chiều không được tăng size)
- Không task nào trong danh sách này bật giao dịch thật. Feature kết thúc ở trạng thái mô phỏng.
