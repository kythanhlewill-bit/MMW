# MMW — Trading Assistant System Overview

**Cập nhật:** 2026-08-03
**Nền tảng:** .NET 8 · ASP.NET Core MVC · EF Core · SQL Server · Hangfire · Tabler
**Kiến trúc:** Web → Application → Domain ← Infrastructure; Shared dùng xuyên tầng

## Mục tiêu

MMW là nhật ký và trợ lý kỷ luật giao dịch crypto. Hệ thống có hai đường phân tích:

- **Deterministic Intraday Trading Engine** là đường quyết định chính: lập kế hoạch ngày, chặn theo thời gian, chấm 100 điểm, áp kỷ luật và tính kích thước lệnh bằng số học có thể tái lập.
- **AI shadow/context** chỉ bổ sung bối cảnh, veto hoặc giảm kích thước. AI không được tạo lệnh, chọn hướng cho engine tất định hay làm lệnh lớn hơn.

Giao dịch thật mặc định tắt bằng `LiveTrading:Enabled=false`; testnet mặc định bật.

## Kiến trúc

```text
MMW.Web
  Controllers, Razor Views, Hangfire job wrappers
        │
        ▼
MMW.Application
  DailyPlanning → TimeGuard → Scoring → Discipline → Sizing
  AI context, Backtest, Services, DTOs
        │
        ▼
MMW.Domain ◄──────── MMW.Infrastructure
  Entities/Enums       EF Core repositories, SQL Server,
                       Binance, macro feeds, LLM adapters
        ▲
        └──────── MMW.Shared (repository/result/helpers)
```

Controllers chỉ gọi service. Business rules nằm trong service/engine handler; truy cập dữ liệu đi qua repository và `IUnitOfWork`.

## Deterministic Intraday Trading Engine

Luồng một cơ hội:

```text
DailyPlan
   ↓
TimeGuard ── blackout? ──► Veto
   ↓
13 IScoreCriterion ──► EntryScorecard (0–100)
   ↓
6 IDisciplineGate ──► block / reduce
   ↓
PositionSizer = base × day × discipline × AI[0..1]
   ↓
EntryScorecard được lưu dù vào hay không vào lệnh
```

Các bất biến chính:

- Không có `DailyPlan` hợp lệ thì không vào lệnh.
- Thiếu dữ liệu chỉ làm giảm điểm/rủi ro, không làm quyết định mạnh hơn.
- AI multiplier luôn trong `[0,1]`.
- Mọi phép tính thời gian qua `IClock`; tầng quyết định không dùng `Random` hay gọi LLM.
- Backtest dùng cùng scorer/gate/sizer với chạy mô phỏng và không được nhìn trước dữ liệu.

## Job nền

### Bốn job lõi

| Job | Lịch UTC | Vai trò |
|---|---:|---|
| `daily-plan` | `30 23 * * *` | Lập kế hoạch tất định cho ngày kế tiếp, sau đó làm giàu ba trường `Ai*` |
| `signal-eval` | phút `1,16,31,46` | Chấm mọi symbol sau khi nến 15 phút đóng; không gọi LLM |
| `position-manage` | mỗi phút | Giảm rủi ro vị thế trước blackout; không gọi LLM |
| `news-scan` | mỗi 15 phút | Phân loại headline mới, có ngân sách gọi AI |

### Job hỗ trợ

| Job | Lịch | Vai trò |
|---|---:|---|
| `market-scan-shadow` | mỗi 15 phút | Chạy đường AI cũ chỉ để so sánh; không có quyền tạo/gửi lệnh |
| `archive-snapshot` | mỗi giờ | Dựng dần kho nến và funding cho backtest |
| `calendar-freshness` | 23:00 UTC | Cảnh báo lịch sự kiện kinh tế quá hạn |
| `trade-result-sync`, `macro-event-scan`, `notification-email`, `retry-pending-sltp` | định kỳ | Chức năng nền của hệ thống baseline |

## Các bảng engine mới

| Bảng | Mục đích |
|---|---|
| `EngineSettings` | Ngưỡng chấm điểm, sizing, kỷ luật, AI budget và backtest |
| `SessionQualityRows` | Bảng chất lượng phiên 0–24 UTC |
| `BlackoutRules` | Biên trước/sau theo loại sự kiện |
| `ScheduledEvents` | Lịch seed, sự kiện suy ra và shock AI |
| `DailyPlans` | Kế hoạch bất biến theo tài khoản/ngày UTC |
| `EntryScorecards`, `EntryScorecardLines` | Quyết định và phân rã từng tiêu chí/gate |
| `MarketContextRecords` | Bối cảnh AI có TTL và trường bị từ chối |
| `KlineArchives`, `FundingRateArchives` | Dữ liệu offline cho backtest |
| `BacktestRuns` | Báo cáo, limitations và tham số một lần chạy |
| `AiSignalScanRecords` | Audit đường AI shadow và điểm bất đồng với engine tất định |

## Các trang vận hành

- `/DailyPlan`: kế hoạch hôm nay/ngày mai và đầu vào bị thiếu.
- `/TimeGuard`: lịch, cửa sổ chặn 48 giờ và độ tươi lịch.
- `/Scorecard`: mọi phiếu chấm điểm, kể cả phiếu bị veto.
- `/Backtest`: tình trạng kho và báo cáo kiểm thử lịch sử.
- `/ShadowComparison`: số đề xuất hai đường, bất đồng và R giả định của AI.
- `/Audit`: request/response AI và audit API sàn.

## Cấu hình và bí mật

- Cấu hình thường nằm trong `appsettings.json` và các bảng setting.
- API key/secret phải đặt bằng User Secrets hoặc biến môi trường; file cấu hình chỉ chứa chuỗi rỗng.
- `EntryScorecard.InputSnapshotJson` chỉ lưu dữ liệu thị trường/điểm số.
- `MarketContextRecord.RawResponseJson` chỉ lưu output của LLM, không lưu request có thông tin tài khoản.
- Audit Binance che khoá và loại trường có tên `secret`/`apiKey` trước khi lưu.

## Build và kiểm thử

```powershell
dotnet build MMW.sln --configuration Release
dotnet test MMW.sln --configuration Release --no-build
```

Các nhóm test bắt buộc gồm Constitution, TimeGuard, DailyPlanning, Scoring, Discipline, Backtest, AI guard/shadow và LiveOrder safety.

## Hạn chế đã biết

- Backfill nến và funding phụ thuộc mạng/Binance; lệnh có thể chạy lại an toàn sau khi bị gián đoạn.
- Backtest lịch sử chưa có đủ open interest/depth nên báo cáo phải nêu `Limitations`.
- Nghiệm thu vận hành 7 ngày chỉ hoàn tất sau khi thu đủ dữ liệu chạy mô phỏng liên tục.
