# Phase 1 — Data Model

**Feature**: Deterministic Intraday Trading Engine
**Date**: 2026-08-02
**Input**: [spec.md](./spec.md) · [research.md](./research.md)

Quy ước kế thừa từ hệ thống hiện có:

- Mọi thực thể kế thừa `BaseEntity` (`Id: long`, `CreatedDate`, `CreatedUser`, `UpdatedDate`, `UpdatedUser`)
- Giá và khối lượng dùng `[Precision(18, 8)]`; tỷ lệ phần trăm và hệ số dùng `[Precision(9, 4)]` — theo Ràng Buộc Kỹ Thuật của hiến chương
- Mọi mốc thời gian nghiệp vụ lưu **UTC**; quy đổi giờ Việt Nam chỉ ở tầng hiển thị
- Mọi thay đổi lược đồ đi qua migration EF Core

---

## 1. `EngineSetting` — mọi ngưỡng của engine

**1:1 với `TradingAccount`**, cùng khuôn mẫu `RiskSetting` đã có. Tồn tại để thoả Nguyên tắc I: không hằng số nào của thuật toán được viết thẳng vào lớp tính toán.

| Trường | Kiểu | Mặc định | Nguồn yêu cầu |
|---|---|---|---|
| `TradingAccountId` | `long` | — | — |
| **Ngưỡng điểm** | | | FR-033 |
| `MinScoreToEnter` | `int` | 55 | |
| `ScoreThresholdFull` | `int` | 70 | |
| `ScoreThresholdMax` | `int` | 85 | |
| `SizeMultiplierLow` | `decimal(9,4)` | 0.5 | |
| `SizeMultiplierFull` | `decimal(9,4)` | 1.0 | |
| `SizeMultiplierMax` | `decimal(9,4)` | 1.5 | |
| **Trọng số nhóm** | | | FR-025 |
| `WeightTechnical` | `int` | 40 | |
| `WeightMarket` | `int` | 30 | |
| `WeightLiquidity` | `int` | 15 | |
| **Tham số kỹ thuật** | | | R-007, R-008 |
| `SwingPivotBars` | `int` | 2 | Số nến hai bên để xác nhận điểm xoay |
| `RetestWindowBars` | `int` | 6 | Số nến tối đa chờ kiểm định lại |
| `MaxAtrFromConfirmation` | `decimal(9,4)` | 1.5 | Quá ngưỡng này thì "vị trí vào lệnh" = 0 điểm |
| `EntryTimeframe` | `string(8)` | `"15m"` | |
| `BiasTimeframe` | `string(8)` | `"4h"` | |
| **Chuyển sang thống kê cá nhân** | | | FR-030 |
| `PersonalStatsMinClosedTrades` | `int` | 50 | |
| `WorstHoursPenalty` | `int` | 10 | |
| **Kỷ luật** | | | FR-035 |
| `LossStreakSizeHalveAt` | `int` | 2 | |
| `RevengeBlockMinutes` | `int` | 15 | Tách khỏi `RiskSetting.RevengeTradeWindowMinutes` (30) vì đây là ngưỡng **chặn**, không phải ngưỡng **cảnh báo** |
| `OversizeBlockMultiple` | `decimal(9,4)` | 1.5 | |
| `OversizeLookbackTrades` | `int` | 20 | |
| **Lớp AI** | | | FR-011, FR-044 |
| `AiBlackoutMaxMinutes` | `int` | 120 | Trần độ dài cửa sổ chặn do AI đề xuất |
| `AiContextDefaultTtlMinutes` | `int` | 240 | |
| **Kiểm thử** | | | R-012 |
| `BacktestTakerFeePercent` | `decimal(9,4)` | 0.05 | |
| `BacktestEntrySlippageBps` | `decimal(9,4)` | 1 | |
| `BacktestStopSlippageBps` | `decimal(9,4)` | 3 | |
| **Chế độ so sánh** | | | FR-059 |
| `ShadowAiComparisonEnabled` | `bool` | `true` | |

**Ràng buộc**: `MinScoreToEnter ≤ ScoreThresholdFull ≤ ScoreThresholdMax`; `SizeMultiplierLow ≤ SizeMultiplierFull ≤ SizeMultiplierMax`; tổng ba trọng số nhóm = 85 (15 điểm còn lại thuộc nhóm kỷ luật chỉ-trừ).

---

## 2. `SessionQualityRow` — bảng chất lượng phiên

Con của `EngineSetting`. Cấu hình được thay vì hardcode (FR-031).

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `EngineSettingId` | `long` | |
| `FromHourUtc` | `int` | 0–23, bao gồm |
| `ToHourUtc` | `int` | 0–24, không bao gồm |
| `Score` | `int` | 0–6 |
| `Label` | `string(40)` | "Phiên Á", "Chồng lấn New York"… |

**Seed 6 dòng**: (0,7,2,"Phiên Á") · (7,9,5,"Mở cửa London") · (9,13,5,"London") · (13,16,6,"Chồng lấn New York") · (16,21,4,"New York chiều") · (21,24,1,"Đêm mỏng")

**Ràng buộc**: các khoảng phải phủ kín 0–24 và không chồng lấn. Kiểm tra khi lưu, không phải khi đọc.

---

## 3. `BlackoutRule` — độ rộng cửa sổ chặn theo loại sự kiện

Con của `EngineSetting`. Cấu hình được (FR-010).

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `EngineSettingId` | `long` | |
| `EventKind` | `ScheduledEventKind` | |
| `MinutesBefore` | `int` | |
| `MinutesAfter` | `int` | |
| `BlocksNewEntries` | `bool` | Mặc định `true` |
| `RequiresPositionAction` | `bool` | Có kích hoạt xử lý vị thế đang mở không (FR-013) |

**Seed 12 dòng**. FR-010 liệt kê **8 nhóm** sự kiện, nhưng khoá duy nhất của bảng là `(EngineSettingId, EventKind)` nên phải trải thành một dòng cho mỗi loại; các loại cùng nhóm dùng chung độ rộng.

Ba loại có `MinutesBefore = MinutesAfter = 0` là có chủ ý — họp báo chính sách và khoảng trống cuối tuần được chặn theo **độ dài của chính sự kiện** (`DurationMinutes`), không theo biên trước/sau.

---

## 4. `ScheduledEvent` — cuốn lịch nội bộ

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `Kind` | `ScheduledEventKind` | |
| `Title` | `string(200)` | |
| `OccursAtUtc` | `DateTime` | |
| `DurationMinutes` | `int?` | Dùng cho sự kiện có độ dài (họp báo) |
| `Impact` | `MacroEventImpact` | Tái dùng enum hiện có |
| `Origin` | `ScheduledEventOrigin` | `Seeded` / `Derived` / `AiDetected` |
| `Currency` | `string(8)?` | |
| `SourceKey` | `string(120)?` | Chống nạp trùng |
| `Notes` | `string(500)?` | |

**Chỉ mục**: `(OccursAtUtc)` và `(SourceKey)` duy nhất khi khác null.

**Trạng thái**: không có vòng đời — sự kiện là bất biến sau khi tạo. Sửa lịch = xoá và nạp lại.

**Ghi chú vận hành**: hệ thống cảnh báo khi `MAX(OccursAtUtc) WHERE Origin = Seeded` đã ở quá khứ (FR-014).

---

## 5. `DailyPlan` — kế hoạch một ngày

**Một bản duy nhất cho mỗi `(TradingAccountId, PlanDateUtc)`.**

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `TradingAccountId` | `long` | |
| `PlanDateUtc` | `DateOnly` | Mốc 00:00 UTC (FR-024) |
| `GeneratedAtUtc` | `DateTime` | |
| **Kết quả phân loại** | | FR-018 |
| `DayRegime` | `DayRegime` | |
| `VolatilityRegime` | `VolatilityRegime` | |
| `AllowedDirections` | `AllowedDirections` | `LongOnly` / `ShortOnly` / `Both` / `None` |
| `RiskMultiplier` | `decimal(9,4)` | |
| `MaxTradesToday` | `int` | |
| **Mức giá tham chiếu** | | |
| `PreviousDayHigh` · `PreviousDayLow` | `decimal(18,8)?` | |
| `WeeklyOpen` · `DailyOpen` | `decimal(18,8)?` | |
| **Đầu vào đã dùng** (lưu để truy vết) | | FR-017 |
| `BtcStructure` | `string(20)?` | |
| `AtrPercentile` | `decimal(9,4)?` | |
| `FundingRate` | `decimal(18,8)?` | |
| `OpenInterestChange24hPercent` | `decimal(9,4)?` | |
| `LongShortAccountRatio` | `decimal(9,4)?` | |
| `FearGreedIndex` | `int?` | |
| **Chất lượng dữ liệu** | | FR-022 |
| `MissingInputs` | `string(500)?` | Danh sách thành phần không lấy được, phân tách bằng dấu phẩy |
| `IsComplete` | `bool` | `false` khi có `MissingInputs` |
| **Bối cảnh AI** | | FR-040 |
| `AiDayRiskLevel` | `string(20)?` | |
| `AiNarrative` | `string(500)?` | Tiếng Việt (FR-047) |
| `AiConfidence` | `decimal(9,4)?` | Trần 0.8 |
| `AiAnswered` | `bool` | |

**Bất biến quan trọng**: khi `IsComplete = false`, `RiskMultiplier` **không được** cao hơn giá trị lẽ ra có nếu đủ dữ liệu (FR-022). Cưỡng chế bằng test.

**Trạng thái**: `DailyPlan` bất biến sau khi sinh. Job chạy lại trong cùng ngày phải **không** ghi đè một bản đã có (idempotent theo `(TradingAccountId, PlanDateUtc)`).

---

## 6. `EntryScorecard` — phiếu chấm điểm

**Lưu mọi lần đánh giá, kể cả khi không vào lệnh** (FR-039, SC-012). Đây là bản ghi kiểm toán trung tâm của feature.

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `TradingAccountId` | `long` | |
| `DailyPlanId` | `long?` | Null khi bị veto vì chưa có kế hoạch |
| `Symbol` | `string(30)` | |
| `Interval` | `string(8)` | |
| `CandleCloseTimeUtc` | `DateTime` | **Khoá logic chống trùng** cùng với Symbol (FR-051) |
| `EvaluatedAtUtc` | `DateTime` | |
| `Direction` | `TradeDirection?` | Null khi không xác định được hướng |
| **Điểm** | | |
| `TechnicalScore` · `MarketScore` · `LiquidityScore` | `int` | |
| `DisciplinePenalty` | `int` | Số âm hoặc 0 |
| `TotalScore` | `int` | 0–100 |
| **Quyết định** | | |
| `Outcome` | `ScorecardOutcome` | `Entered` / `BelowThreshold` / `Vetoed` |
| `VetoReason` | `VetoReason?` | |
| `VetoDetail` | `string(300)?` | |
| **Kích thước** | | FR-034 |
| `BaseSizeR` | `decimal(9,4)` | Từ bảng ngưỡng điểm |
| `DayRiskMultiplier` | `decimal(9,4)` | Từ `DailyPlan` |
| `DisciplineMultiplier` | `decimal(9,4)` | Từ gate kỷ luật |
| `AiMultiplier` | `decimal(9,4)` | Luôn ≤ 1.0 (FR-042) |
| `FinalSizeR` | `decimal(9,4)` | Tích của bốn giá trị trên |
| **Mức giá đề xuất** | | |
| `SuggestedEntry` · `SuggestedStopLoss` · `SuggestedTakeProfit` | `decimal(18,8)?` | |
| `RiskReward` | `decimal(9,4)?` | |
| **Truy vết** | | |
| `TradeId` | `long?` | Lệnh sinh ra, nếu có |
| `InputSnapshotJson` | `string(max)` | Toàn bộ đầu vào để tái lập lại phép tính |
| `IsBacktest` | `bool` | Tách bản ghi kiểm thử khỏi bản ghi chạy thật |
| `BacktestRunId` | `long?` | |

**Chỉ mục**: `(Symbol, CandleCloseTimeUtc, IsBacktest)` duy nhất — chính là cơ chế chống sinh trùng của FR-051.

**Bất biến**: `FinalSizeR ≤ BaseSizeR` luôn đúng (FR-034). Cưỡng chế bằng test và bằng ràng buộc kiểm tra khi lưu.

---

## 7. `EntryScorecardLine` — điểm từng tiêu chí

Con của `EntryScorecard`. Một dòng cho mỗi tiêu chí trong 13 tiêu chí + 6 gate.

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `EntryScorecardId` | `long` | |
| `CriterionKey` | `string(60)` | Định danh ổn định, ví dụ `technical.htf_alignment` |
| `Group` | `ScoreGroup` | |
| `MaxPoints` | `int` | |
| `AwardedPoints` | `int` | Có thể âm với nhóm kỷ luật |
| `IsHardVeto` | `bool` | |
| `Reason` | `string(300)` | Tiếng Việt, nêu **số liệu thực tế so với ngưỡng** (Nguyên tắc I) |
| `DataAvailable` | `bool` | `false` → `AwardedPoints = 0` theo FR-006 |
| `IsApproximation` | `bool` | `true` cho tiêu chí vùng thanh khoản (R-010) |

**Lý do tách bảng thay vì nhét JSON**: cần truy vấn được "tiêu chí nào hay về 0 điểm nhất trong 3 tháng qua" — đây chính là dữ liệu để cải tiến thuật toán, và là lý do tồn tại của Nguyên tắc IV.

---

## 8. `MarketContextRecord` — bối cảnh do AI sinh

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `Kind` | `MarketContextKind` | `DailyBrief` / `NewsItem` |
| `Severity` | `string(20)` | `noise`/`low`/`medium`/`high`/`critical` |
| `Leaning` | `MarketBias` | Tái dùng enum hiện có |
| `AffectedSymbols` | `string(200)?` | Phân tách bằng dấu phẩy |
| `Narrative` | `string(500)?` | Tiếng Việt |
| `IsRumor` | `bool` | Tin đồn → `Severity` trần ở `medium` |
| `RecordedAtUtc` | `DateTime` | |
| `ExpiresAtUtc` | `DateTime` | Từ `halfLifeMinutes` (FR-044) |
| `SourceKey` | `string(120)?` | Chống xử lý lại cùng một tin |
| `RawResponseJson` | `string(max)?` | Ghi vết phản hồi thô (Nguyên tắc IV) |
| `RejectedFields` | `string(300)?` | Các trường bị loại vì AI vượt quyền (FR-041, FR-043) |

**Quy tắc đọc**: `WHERE ExpiresAtUtc > clock.UtcNow`. Bản ghi hết hạn coi như không tồn tại — **không xoá**, giữ lại để truy vết.

---

## 9. `KlineArchive` — kho nến lịch sử

| Trường | Kiểu |
|---|---|
| `Symbol` | `string(30)` |
| `Interval` | `string(8)` |
| `OpenTimeUtc` · `CloseTimeUtc` | `DateTime` |
| `Open` · `High` · `Low` · `Close` · `Volume` | `decimal(18,8)` |
| `QuoteVolume` | `decimal(18,8)?` |
| `TradeCount` | `int?` |

**Khoá duy nhất**: `(Symbol, Interval, OpenTimeUtc)` — chống nạp trùng khi nạp bổ sung (FR-005).

**Chỉ mục truy vấn**: cùng bộ ba, dùng cho quét theo khoảng thời gian.

**Ước lượng dung lượng**: 2 symbol × 3 khung × 2 năm ≈ **160k dòng**. Không đáng kể.

**Không lưu cờ đã đóng** — suy ra từ `CloseTimeUtc` theo R-002, giữ cho kho và sàn hành xử giống nhau.

---

## 9b. `FundingRateArchive` — kho lịch sử phí vốn

> Thực thể này **được thêm sau khi T001 kiểm chứng API thật**. Bản nháp trước giả định phí vốn cũng bị giới hạn 30 ngày như nhóm `/futures/data/*`; thực tế `/fapi/v1/fundingRate` có đủ ≥ 2 năm. Lưu lại được nghĩa là tiêu chí `market.funding_crowding` (4 điểm) kiểm thử lịch sử được, thay vì mất trắng.

| Trường | Kiểu |
|---|---|
| `Symbol` | `string(30)` |
| `FundingTimeUtc` | `DateTime` |
| `FundingRate` | `decimal(9,8)` |
| `MarkPrice` | `decimal(18,8)?` |

**Khoá duy nhất**: `(Symbol, FundingTimeUtc)`.

**Dung lượng**: 2 symbol × 3 mốc/ngày × 2 năm ≈ **4.4k dòng**. Không đáng kể.

**Ngữ nghĩa khi đọc ở chế độ kiểm thử**: bản ghi tại mốc `T` là tỷ lệ **đã thanh toán** cho chu kỳ kết thúc tại `T`. `ArchiveMarketDataProvider.GetFundingAsync(t)` trả bản ghi có `FundingTimeUtc` **lớn hơn hoặc bằng** `t` gần nhất — tức tỷ lệ của chu kỳ đang chạy tại thời điểm `t`.

⚠️ Chạy thật dùng `lastFundingRate` (dự phóng), kiểm thử dùng tỷ lệ đã thanh toán. **Đây là một sai lệch có thật giữa hai chế độ** và là ngoại lệ duy nhất được phép của nguyên tắc tương đương ở R-001 — vì phương án thay thế là chấm 0 điểm, còn tệ hơn. Phải ghi trong `BacktestRun.Limitations`, và test tương đương (SC-003) **loại trừ riêng tiêu chí này** bằng một danh sách loại trừ có tên rõ ràng, không phải bằng cách nới lỏng phép so sánh.

---

## 10. `BacktestRun` — một lần chạy kiểm thử

| Trường | Kiểu | Ghi chú |
|---|---|---|
| `Name` | `string(120)` | |
| `FromUtc` · `ToUtc` | `DateTime` | |
| `Symbols` | `string(200)` | |
| `EngineSettingSnapshotJson` | `string(max)` | **Chụp lại toàn bộ cấu hình** — không có nó thì kết quả cũ không diễn giải được sau khi đổi tham số |
| `StartedAtUtc` · `CompletedAtUtc` | `DateTime?` | |
| `Status` | `string(20)` | `Running` / `Completed` / `Failed` |
| **Chỉ số tổng kết** | | FR-055 |
| `TradeCount` | `int` | |
| `WinRate` | `decimal(9,4)` | |
| `ExpectancyR` | `decimal(9,4)` | |
| `MaxDrawdownPercent` | `decimal(9,4)` | |
| `LongestLossStreak` | `int` | |
| `TotalFees` · `TotalSlippage` | `decimal(18,8)` | |
| `BreakdownByHourJson` | `string(max)` | |
| `BreakdownByRegimeJson` | `string(max)` | |
| `Limitations` | `string(500)` | **Bắt buộc điền** — ví dụ "thiếu 10/100 điểm do giới hạn 30 ngày của `/futures/data/*`" (R-003) |

`Limitations` không được để trống. Một báo cáo kiểm thử không nêu hạn chế của chính nó là một báo cáo gây hiểu nhầm.

---

## 11. Mở rộng thực thể hiện có

### `Candle` (record trong `Application/MarketData/Models`)

```
+ IsClosed  →  suy ra: clock.UtcNow >= CloseTime
```

Không phải trường lưu trữ. Kèm phương thức mở rộng `ClosedOnly()`.

### `Trade`

```
+ EntryScorecardId : long?    // liên kết ngược lệnh ↔ phiếu chấm điểm
```

### `AppSetting`

```
+ DeterministicEngineEnabled : bool = false   // công tắc chuyển từ đường AI sang đường tất định
+ ShadowComparisonEnabled    : bool = true    // FR-059
```

Công tắc mặc định `false` cho phép triển khai từng phần mà không đổi hành vi hệ thống đang chạy — cùng tinh thần với Nguyên tắc III.

---

## 12. Enum mới

Thêm vào `MMW.Domain/Enums/TradingEnums.cs`, theo quy ước đánh số hiện có (`FlagType` dùng 1xx cho rule, 2xx cho behavior → engine dùng 3xx).

```
DayRegime            : TrendUp=1, TrendDown=2, Range=3, HighVolatility=4, EventDay=5
VolatilityRegime     : Low=1, Normal=2, High=3, Extreme=4
AllowedDirections    : None=0, LongOnly=1, ShortOnly=2, Both=3
ScheduledEventKind   : Cpi=1, Ppi=2, Nfp=3, FomcStatement=4, FomcPressConference=5,
                       Pce=6, Gdp=7, JoblessClaims=8, OptionsExpiry=20,
                       FundingSettlement=21, WeekendGap=22, AiDetectedShock=90
ScheduledEventOrigin : Seeded=1, Derived=2, AiDetected=3
ScoreGroup           : Technical=1, Market=2, Liquidity=3, Discipline=4
ScorecardOutcome     : Entered=1, BelowThreshold=2, Vetoed=3
MarketContextKind    : DailyBrief=1, NewsItem=2
VetoReason           : NoDailyPlan=300, DirectionNotAllowed=301, HtfMisaligned=302,
                       InBlackoutWindow=303, LossStreakStop=304, DailyLossStop=305,
                       RevengeWindow=306, Oversized=307, MaxTradesReached=308,
                       InsufficientData=309, DuplicateCandle=310
```

`VetoReason` là enum chứ không phải chuỗi tự do vì nó sẽ được đếm và xếp hạng: "3 tháng qua lý do từ chối phổ biến nhất là gì" là câu hỏi trader sẽ hỏi.

---

## Sơ đồ quan hệ

```
TradingAccount ─1:1─ RiskSetting          (đã có)
               └─1:1─ EngineSetting        (MỚI)
                        ├─1:n─ SessionQualityRow
                        └─1:n─ BlackoutRule

TradingAccount ─1:n─ DailyPlan             (duy nhất theo ngày UTC)
                        └─1:n─ EntryScorecard
                                 ├─1:n─ EntryScorecardLine
                                 └─0:1─ Trade

ScheduledEvent      (độc lập, tra theo thời gian)
MarketContextRecord (độc lập, tra theo thời gian + hạn dùng)
KlineArchive        (độc lập, tra theo symbol + khung + thời gian)
BacktestRun ─1:n─ EntryScorecard   (qua BacktestRunId, IsBacktest = true)
```

---

## Chiến lược migration

**Một migration duy nhất** cho toàn bộ 11 bảng mới + 3 cột thêm vào bảng cũ. Lý do: các bảng phụ thuộc lẫn nhau qua khoá ngoại, tách nhiều migration chỉ tạo ra thứ tự áp dụng dễ sai mà không được gì.

Seed đi kèm — **không dùng `HasData` trong migration** mà chạy ở seeder runtime `src/MMW.Web/Data/SeedData.cs`, theo đúng khuôn mẫu đã có của `RiskSetting`:

1. `EngineSetting` + 6 `SessionQualityRow` + 12 `BlackoutRule` cho mỗi `TradingAccount` đang có
2. `ScheduledEvent` cho phần còn lại của năm 2026 (`Origin = Seeded`)
3. Không seed `KlineArchive` / `FundingRateArchive` — nạp bằng job riêng theo yêu cầu

Seeder **idempotent theo tài khoản**: chỉ tạo cho tài khoản còn thiếu, không đụng cấu hình đã có. Người dùng chỉnh ngưỡng rồi mà seeder ghi đè lúc khởi động lại thì Nguyên tắc I chỉ còn là hình thức.

**Kiểm tra bắt buộc**: migration phải áp dụng được trên một cơ sở dữ liệu **sạch** (Cổng chất lượng số 6 của hiến chương).

### Ghi chú phát sinh khi cài đặt

`MacroEventImpact` trước đây nằm ở `MMW.Application.Models`, nhưng `ScheduledEvent` là thực thể Domain mà Domain không tham chiếu ngược lên Application được. Enum đã được **chuyển sang `MMW.Domain.Enums`**; hai nơi dùng nó ở Application và Infrastructure chỉ cần thêm `using`.
