# 02 — Hiện Trạng Sản Phẩm MMW: Kiểm Kê Kỹ Thuật Độc Lập

**Ngày kiểm kê**: 2026-07-29
**Phạm vi**: toàn bộ mã nguồn tại `D:/KYLT/MMW` (branch `main`, working tree hiện tại — **không phải** HEAD)
**Phương pháp**: đọc mã nguồn trực tiếp, đo LOC bằng script, chạy build + test thật. Không suy diễn từ tài liệu.
**Vai trò người viết**: Principal Engineer được thuê kiểm kê độc lập trước vòng gọi vốn.

> **Cảnh báo về phạm vi**: tài liệu này chỉ nói về **mã nguồn và trạng thái kỹ thuật**. Mọi con số
> thị trường, quy mô TAM, định giá so sánh, mức funding của đối thủ đều **KHÔNG có trong tài liệu này**
> — tôi không thực hiện nghiên cứu thị trường trong lượt kiểm kê này và sẽ không bịa số. Những phần
> đó thuộc về tài liệu chiến lược riêng.
> Mọi con số ước lượng công sức trong tài liệu này đều được đánh dấu **(ước lượng)** và dựa trên
> kinh nghiệm chủ quan, không phải đo đạc.

---

## 0. Tóm Tắt Điều Hành — Nói Thẳng

MMW **không phải** một dự án Web3, không token, không smart contract, không blockchain. Đây là một
**ứng dụng web ASP.NET Core single-tenant, self-host, đọc dữ liệu từ Binance CEX**, hiện đang phục vụ
đúng **một người dùng là chính tác giả**, với **zero khách trả tiền**.

Ba câu quan trọng nhất:

1. **Chất lượng kỹ thuật của phần lõi cao hơn mức mong đợi của một dự án cá nhân.** Rule Engine và
   Behavior Detector là plug-in thật, kiến trúc phân tầng là thật (không phải "clean trên giấy"),
   luồng đặt lệnh thật có **18 lớp chặn tuần tự** và build sạch với 69/69 test xanh trong 3 giây.
2. **Nhưng độ phủ test ở đúng vùng đắt tiền nhất chỉ đạt 50%**: 9 trên 18 lớp chặn của
   `LiveOrderService` **không có bất kỳ test nào chứng minh chúng chặn**. Hiến chương v1.0.0 Nguyên tắc
   VI yêu cầu 100%. Toàn bộ ~1.900 dòng phân tích cú pháp phản hồi AI (`MarketScanService` +
   `TradePreflightAnalysisService`) có **0 test**, dù hiến chương nêu đích danh vùng này là bắt buộc.
3. **Rủi ro vận hành lớn nhất hiện nay không nằm trong code, mà nằm ở git.** Commit cuối cùng là
   **2026-06-01**. Hôm nay là **2026-07-29**. Working tree đang có **64 file sửa đổi + 78 mục chưa
   theo dõi**, riêng phần file đã theo dõi đã lệch **+4.719 dòng** so với HEAD. Toàn bộ
   `LiveOrderService.cs`, `TradePreflightAnalysisService.cs`, `docs/`, `specs/`, `.specify/` **chưa
   từng được commit**. Gần hai tháng công sức đang nằm trên một ổ đĩa duy nhất, không lịch sử,
   không thể bisect, không thể rollback.

Kết luận ngắn: **sản phẩm ở mức "prototype nội bộ chạy được, chưa phải sản phẩm"**. Về mặt thương mại
hoá, khoảng cách tới một SaaS multi-tenant có thu phí là **lớn hơn phần đã làm** — chi tiết ở Mục 6.

---

## 1. Số Liệu Đo Được (không phải ước lượng)

### 1.1 Quy mô mã nguồn

Đo bằng `find` + `wc -l`, loại trừ `obj/`, `bin/` và thư mục `Migrations/`:

| Project | Số file `.cs` | LOC | Vai trò |
|---|---:|---:|---|
| `MMW.Domain` | 44 | 1.299 | Entity, enum, DbContext, EF configuration |
| `MMW.Application` | 88 | 6.343 | Nghiệp vụ: service, rule engine, behavior, indicator, port |
| `MMW.Infrastructure` | 21 | 2.264 | Adapter Binance, adapter LLM, email, macro, repository |
| `MMW.Web` | 26 | 2.355 | Controller, view model, hub, Program.cs |
| `MMW.Shared` | 4 | 107 | `IBaseRepository`, `IUnitOfWork`, `Result`, `PaginatedResult` |
| **Tổng mã sản xuất** | **183** | **12.368** | |
| EF Core migration (sinh tự động) | 27 | 16.710 | không tính là mã người viết |
| Razor view `.cshtml` | 23 | 3.287 | |
| Test | 12 | 1.787 | |

**Nhận xét thẳng**: 12.368 dòng mã sản xuất là quy mô của **một dự án cá nhân 2–4 tháng**, không phải
một nền tảng. Con số 27 file migration / 16.710 dòng dễ gây ảo giác quy mô — đó là mã do `dotnet ef`
sinh ra, giá trị kỹ thuật gần bằng 0.

### 1.2 File lớn nhất

| File | LOC | Đánh giá |
|---|---:|---|
| `src/MMW.Application/Services/MarketScanService.cs` | 1.028 | **God-class** — xem Mục 3.3 |
| `src/MMW.Application/Services/TradePreflightAnalysisService.cs` | 849 | **God-class** — xem Mục 3.3 |
| `src/MMW.Web/Controllers/TradesController.cs` | 662 | **Fat controller có logic nghiệp vụ** — vi phạm Nguyên tắc V |
| `src/MMW.Infrastructure/Exchanges/Binance/BinanceFuturesOrderProvider.cs` | 551 | chấp nhận được (adapter dày là bình thường) |
| `src/MMW.Application/Services/LiveOrderService.cs` | 547 | dày nhưng có lý do — đường tiền thật |
| `src/MMW.Application/Services/TradeAdvisorService.cs` | 283 | ổn |

### 1.3 Build & test — chạy thật lúc 2026-07-29

```
dotnet test MMW.sln
→ Passed!  Failed: 0, Passed: 69, Skipped: 0, Total: 69, Duration: 3 s
→ warning NU1903: Package 'AutoMapper' 14.0.0 has a known high severity vulnerability
   (GHSA-rvv3-g6hj-g44x)  [MMW.Application.csproj]
```

- Build **thành công**, test **xanh 100%**, thời gian chạy **3 giây** — bộ test nhanh, không phụ thuộc
  hạ tầng ngoài (dùng EF InMemory).
- **Nhưng có 1 cảnh báo bảo mật mức cao chưa xử lý.** Hiến chương "Cổng chất lượng #2" yêu cầu
  *"Build không lỗi, **không cảnh báo mới**"*. Cổng này đang **fail**.

### 1.4 Trạng thái kho mã — vấn đề nghiêm trọng nhất

| Chỉ số | Giá trị |
|---|---|
| Tổng số commit toàn dự án | **4** |
| Commit đầu tiên | 2026-05-31 |
| Commit cuối cùng | **2026-06-01** |
| Ngày hôm nay | 2026-07-29 |
| **Khoảng trống không commit** | **~59 ngày** |
| File đã theo dõi bị sửa | 64 |
| File đã theo dõi bị xoá | 1 |
| Mục chưa theo dõi (untracked) | 78 |
| Chênh lệch dòng vs HEAD (chỉ tính file đã theo dõi) | **+4.719 / −225** |

Những file **chưa từng nằm trong bất kỳ commit nào**:
`LiveOrderService.cs` · `TradePreflightAnalysisService.cs` · `MacroEventService.cs` ·
`NotificationService.cs` · toàn bộ `Interfaces/I*Notification*.cs` · `LiveTradingOptions.cs` ·
`IExchangeOrderProvider.cs` · thư mục `docs/` · `specs/` · `.specify/` · `.claude/`.

**Nghĩa là**: toàn bộ tính năng *đặt lệnh tiền thật*, *preflight AI*, *notification*, *macro event*,
và cả *hiến chương + baseline spec* đang tồn tại **duy nhất trên ổ đĩa làm việc**. Một sự cố ổ cứng
xoá sạch ~2 tháng công sức và mọi tài liệu thiết kế.

Ngoài ra, kho mã đang **theo dõi 12 file trong `.vs/`** — bao gồm `CodeChunks.db`,
`SemanticSymbols.db` (chỉ mục Copilot chứa đoạn mã nguồn), `.suo`, `applicationhost.config`.
`.gitignore` đã thêm `.vs/` (commit `7b40b6c`) nhưng file cũ vẫn nằm trong index vì chưa
`git rm --cached`. Trên tổng số 195 file được theo dõi, 12 file (6%) là rác IDE.

---

## 2. Mức Độ Hoàn Thiện THẬT — Chấm Từng Tính Năng

Thang đo:
- **Xong** — hoạt động end-to-end, có test hoặc đã dùng thực tế, không còn TODO chặn.
- **Gần xong** — hoạt động, nhưng thiếu test / thiếu một nhánh xử lý / có bug đã biết.
- **Prototype** — chạy được ở happy path, chưa chịu được lỗi thật, chưa có test.
- **Khung rỗng** — có interface/entity/UI nhưng logic chưa hoàn chỉnh hoặc mặc định vô hiệu.

| # | Tính năng | Mức | Bằng chứng |
|---|---|---|---|
| 1 | **Trade journal CRUD + đóng lệnh + PnL/R-multiple** | **Xong** | `TradeService.cs:65-232`; test `CloseTradeTests` (2), `CrudTradeTests` (2), `TradeMetricsCalculatorTests` (4). Công thức PnL, hoàn số dư khi xoá, R-multiple đều có test. |
| 2 | **Rule Engine 5 quy tắc** | **Xong** | `RuleEngine/Rules/*.cs`; 11 test trong `RuleTests.cs` phủ cả trường hợp kích hoạt lẫn ngay dưới ngưỡng. Plug-in thật: `TradeRuleEngine` chỉ lặp `IEnumerable<ITradeRule>`. |
| 3 | **Behavior detector 3 loại** | **Xong** | `Behavior/Detectors/*.cs`; 9 test trong `BehaviorTests.cs`, có cả case âm (`Revenge_Passes_When_Previous_Was_Win`, `Oversized_Passes_When_Previous_Not_Loss`). |
| 4 | **Chỉ báo kỹ thuật (SMA/EMA/RSI/MACD/ATR)** | **Xong** | `IndicatorService.cs`; 9 test gồm cả biên (`Rsi_Is_100_When_Only_Gains`, `Atr_Null_When_Insufficient`). |
| 5 | **Bias thị trường deterministic** | **Xong** | `MarketAnalyzer.cs`; 3 test. |
| 6 | **Quét thị trường + sinh đề xuất bằng AI** | **Gần xong** | `MarketScanService.cs` chạy đủ luồng, có audit trail. **0 test.** Có rò rỉ transaction giữa các symbol khi lỗi (Mục 5, mục D-04). |
| 7 | **Preflight vòng 2** | **Gần xong** | `TradePreflightAnalysisService.cs:75-145` có nhánh deterministic đầy đủ khi AI hỏng. **0 test** — đây là cổng gác cho auto-đặt lệnh thật. |
| 8 | **Đặt lệnh thật (live order)** | **Gần xong** | 18 lớp chặn tuần tự đúng thứ tự hiến chương; 19 test. Nhưng chỉ 9/18 lớp có test chặn (Mục 4). |
| 9 | **Retry SL/TP (SltpPending)** | **Xong** | `LiveOrderService.cs:467-528`; 2 test (`SltpPending_Set_When_All_Retries_Exhausted`, `RetryPendingSltp_Calls_SyncLevels_And_Clears_Pending`). Đúng tinh thần "không im lặng". |
| 10 | **Trade advisor lệnh đang mở** | **Gần xong** | `TradeAdvisorService.cs`; nhánh deterministic đầy đủ, AI chỉ làm giàu. **0 test.** Chi phí LLM chưa kiểm soát (Mục 5, mục P-02). |
| 11 | **Đồng bộ kết quả từ sàn** | **Prototype** | `TradeResultSyncService.cs`; fuzzy-match ±5% giá, chưa ghép FIFO. **0 test.** Có bug testnet/mainnet (Mục 5, mục C-01). |
| 12 | **Import vị thế từ Binance** | **Prototype** | Logic nằm **trong controller** `TradesController.cs:436-538`. **0 test.** Hardcode `useTestnet: false`. |
| 13 | **Notification center (in-app + email + SignalR)** | **Gần xong** | `NotificationService.cs` (248 LOC), có chống trùng theo `Source+SourceKey`, có preference theo loại. **0 test.** |
| 14 | **Macro event scan** | **Khung rỗng (mặc định)** | `NoopMacroEventProvider.cs` trả mảng rỗng. `ConfiguredMacroEventProvider` cần `CalendarJsonUrl` / TradingEconomics API key / RSS. `appsettings.json` để `CalendarJsonUrl: ""`, TradingEconomics `Enabled: false`. Chỉ còn 2 RSS ngân hàng trung ương → **gần như luôn trả rỗng**, lớp `ApplyMacroGate` gần như không bao giờ kích hoạt. |
| 15 | **Audit trail (AI scan + Exchange API)** | **Xong** | `AiSignalScanRecord` + `ExchangeApiAuditRecord`; có redact secret (`BinanceFuturesOrderProvider.cs:518-534`). Trang tra cứu `AuditController.cs` (205 LOC). |
| 16 | **Dashboard tổng quan** | **Gần xong** | `HomeController.cs:34-90`. Có win-rate, PnL, flag count. Nhưng đọc số dư Binance đồng bộ trong request (blocking). |
| 17 | **Cấu hình rủi ro theo tài khoản + toàn cục** | **Xong** | `SettingsService.cs`; `RiskSetting` 1:1 với account, `AppSetting` là bản ghi đơn. |
| 18 | **Xác thực đăng nhập** | **Gần xong** | Cookie auth + `FallbackPolicy` bắt buộc đăng nhập toàn hệ thống; `ValidateAntiForgeryToken` trên mọi POST kiểm tra. **Nhưng** mật khẩu admin mặc định hardcode (Mục 5, mục S-02). |
| 19 | **Trang review cờ vi phạm / hành vi theo thời gian** | **Chưa có** | `grep Flag src/MMW.Web/Controllers` chỉ ra 2 dòng đếm số trong `HomeController`. Không có view, không có controller. Đây là **giá trị cốt lõi "học từ lỗi"** mà spec tuyên bố — hiện chưa tồn tại. |
| 20 | **`SignalGenerator` thuần quy tắc** | **Mã chết trên luồng chính** | Đăng ký DI (`DependencyInjection.cs`), có 5 test, nhưng `grep` cho thấy không service nào trên luồng scan gọi nó. Duy trì test cho mã không chạy. |
| 21 | **Multi-tenant / billing / onboarding / public API / mobile** | **Không tồn tại** | Không có `UserId` trên bất kỳ entity nghiệp vụ nào. Xem Mục 6. |

### Tổng kết bảng chấm

| Mức | Số tính năng | Tỷ lệ |
|---|---:|---:|
| Xong | 8 | 38% |
| Gần xong | 7 | 33% |
| Prototype | 2 | 10% |
| Khung rỗng / chưa có / mã chết | 4 | 19% |

**Đọc bảng này thế nào**: phần "Xong" tập trung **toàn bộ ở lõi kỷ luật deterministic** (journal, rule,
behavior, indicator, metrics). Phần "Gần xong" và "Prototype" tập trung **toàn bộ ở lớp AI và lớp chạm
sàn** — tức là đúng những phần khó nhất, đắt nhất và rủi ro nhất lại là những phần chưa chín.

---

## 3. Chất Lượng Kỹ Thuật

### 3.1 Kiến trúc phân tầng — CLEAN THẬT, không phải trên giấy

Tôi đã kiểm tra chiều phụ thuộc bằng cách đọc `using` và `csproj`:

| Kiểm tra | Kết quả |
|---|---|
| `Domain` phụ thuộc tầng khác? | **Không.** Chỉ `Microsoft.EntityFrameworkCore` (cho `[Precision]`, `DbContext`). |
| `Application` tham chiếu SDK sàn / LLM / SMTP? | **Không.** Mọi hệ thống ngoài đi qua port: `IMarketDataProvider`, `IExchangeOrderProvider`, `ILlmService`, `IEmailSender`, `IMacroEventProvider` — tất cả khai báo trong `Application`, cài đặt trong `Infrastructure`. |
| `Application` có `using MMW.Infrastructure`? | **Không** (`MMW.Application.csproj` không tham chiếu `MMW.Infrastructure`). |
| Rule/detector plug-in thật? | **Có.** `TradeRuleEngine.Evaluate` chỉ `foreach (var rule in _rules)`. Thêm rule = thêm 1 class + 1 dòng DI. |

**Đây là điểm mạnh thật sự.** Rất nhiều dự án cá nhân tuyên bố "Clean Architecture" rồi
`Application` gọi thẳng `HttpClient` tới Binance. MMW thì không. Port/Adapter là thật, và điều đó có
nghĩa là **thêm sàn thứ hai (Bybit, OKX) là công việc bình thường, không phải viết lại**.

### 3.2 Vi phạm Nguyên tắc V — Controller chứa logic nghiệp vụ

Hiến chương: *"Controller KHÔNG ĐƯỢC chứa logic nghiệp vụ. Chúng nhận đầu vào, gọi service, trả view."*

| Vị trí | Vi phạm |
|---|---|
| `src/MMW.Web/Controllers/TradesController.cs:436-538` | `ImportFromBinance` — **103 dòng logic đối soát vị thế**: gọi provider, so sánh entry/qty, cập nhật entity, gọi `_unitOfWork.CommitAsync` trực tiếp. Đây là một service nghiệp vụ đội lốt action. |
| `src/MMW.Web/Controllers/TradesController.cs:640-652` | `AutoSizeAsync` — **công thức tính khối lượng theo % rủi ro** nằm trong controller. Công thức này cũng bị **lặp lại** ở `TradeService.cs:98-104` và `MarketScanService.cs:563-569`. Ba bản sao của cùng một công thức rủi ro. |
| `src/MMW.Web/Controllers/TradesController.cs:137-160` | `LoadOpenOrdersAsync` — vòng lặp gọi API Binance cho từng tài khoản, ngay trong request pipeline. |
| `src/MMW.Web/Controllers/TradesController.cs:540-569` | `Symbols` — trộn watchlist với kết quả tìm kiếm sàn, sắp xếp ưu tiên, `Take(40)`. Logic hiển thị nhưng vẫn là logic. |

**Mức độ nghiêm trọng**: trung bình. Không gây mất tiền, nhưng công thức khối lượng bị nhân bản
3 lần là mầm mống lệch số liệu — nếu sửa ngưỡng ở một chỗ mà quên hai chỗ kia thì rủi ro thực tế
khác rủi ro hiển thị.

### 3.3 God-class: `MarketScanService` (1.028) và `TradePreflightAnalysisService` (849)

**Phán quyết: CÓ, cả hai đều là god-class.** Nhưng lý do và mức nghiêm trọng khác nhau.

**`MarketScanService.cs` — 1.028 dòng, ít nhất 6 trách nhiệm tách rời được:**

| Trách nhiệm | Dòng | Có thể tách thành |
|---|---|---|
| Điều phối vòng quét watchlist | 157-227 | giữ lại (đây là công việc thật của service) |
| Prompt engineering (2 prompt lớn, 62 dòng chuỗi) | 31-96 | `AiSignalPromptCatalog` |
| Dựng payload gửi AI (2 biến thể) | 344-463 | `AiSignalPayloadBuilder` |
| **Parser JSON chịu lỗi từ LLM** | 695-948 | **`LlmJsonReader` (dùng chung)** |
| Kiểm chứng đề xuất bằng luật cứng | 621-693 | `SignalValidator` |
| Auto-tạo lệnh + gọi đặt lệnh thật | 465-514 | `SignalToTradePipeline` |
| Upsert snapshot / history | 972-1027 | repository method |

Phần parser (dòng 695-948, ~253 dòng) **gần như trùng lặp hoàn toàn** với dòng 433-801 của
`TradePreflightAnalysisService.cs`. Cụ thể, các hàm sau tồn tại **hai bản gần như giống hệt** ở hai
file: `ExtractJson`, `ExtractJsonCandidates`, `ExtractBalancedObjects`, `UnwrapAiRoot`, `TryGetProperty`,
`ReadString`, `ReadInt`, `ReadDecimal`, `ReadStringList`, `NormalizeConfidence`, `TrimRaw`.

Đó là **~250 dòng mã trùng lặp** — và là mã **khó nhất, dễ sai nhất, hoàn toàn không có test** trong
toàn hệ thống. Nếu sửa một bug parser, phải nhớ sửa ở hai nơi.

**`TradePreflightAnalysisService.cs` — 849 dòng:**
Trách nhiệm: tính metrics, gọi market data, tính bias, sinh cảnh báo deterministic, dựng payload,
gọi AI, parse 3 tầng (canonical → deserialize → regex partial), kẹp dải, áp macro gate, làm sạch
SL/TP đề xuất. Thêm vào đó là 3 chiến lược parse chồng nhau, trong đó `TryParsePartialAiResult`
(dòng 515-593) dùng **regex bóc trường từ JSON hỏng** — kỹ thuật hợp lý nhưng cực kỳ cần test, và
hiện **không có test nào**.

**Mức độ nghiêm trọng**: cao — không phải vì số dòng, mà vì **đây là hai file quyết định có auto-đặt
lệnh tiền thật hay không**, và chúng là hai file duy nhất trong hệ thống vừa dài, vừa phức tạp,
vừa trùng lặp, vừa không test.

### 3.4 Vi phạm Nguyên tắc I — hardcode ngưỡng trong logic

Hiến chương: *"Ngưỡng kỷ luật PHẢI đọc từ cấu hình theo tài khoản. Hardcode ngưỡng trong logic phát
hiện là vi phạm hiến chương."*

| Vị trí | Giá trị hardcode | Đúng ra phải đọc từ |
|---|---|---|
| `MarketScanService.cs:371-372` | `defaultLeverage = 20`, `minOrderNotionalUsdt = 20` gửi cho AI | `LiveTradingOptions.DefaultLeverage`, `.MinOrderNotionalUsdt` |
| `MarketScanService.cs:583` | `Leverage = 20m` khi gọi preflight | `LiveTradingOptions.DefaultLeverage` |
| `TradeService.cs:117` | `Leverage = 20m` khi tạo lệnh từ đề xuất | như trên |
| `TradeDuplication.cs:11` | `PriceTolerancePercent = 0.5m` | nên là `RiskSetting` |
| `BehaviorAnalysisService.cs:13` | `HistoryLimit = 50` | chấp nhận được (hằng số kỹ thuật) |
| `OversizedAfterLossDetector.cs:14` | `RecentWindow = 10` | biên giới — spec mô tả "trung bình 10 lệnh gần nhất" như một ngưỡng nghiệp vụ |
| `TradeAdvisorService.cs:153-160` | `-5%`, `1%`, `3%`, `75`, `25`, `5%` trong `DetermineRiskLevel` | **rõ ràng là ngưỡng cảnh báo hướng tới người dùng, đang hardcode** |
| `TradeAdvisorService.cs:168-199` | `1m`, `3m`, `75`, `25`, `8m`, `3m`, `-5m` trong `GenerateAdvice` | như trên |
| `TradeResultSyncService.cs:185` | `0.05m` (±5% fuzzy match) | nên cấu hình được |
| `TradeResultSyncService.cs:218` | `0.9m` (ngưỡng fill 90%) | nên cấu hình được |

**Nhận xét**: `TradeAdvisorService` là vi phạm rõ nhất — nó là bộ sinh cảnh báo hướng tới người dùng,
toàn bộ ngưỡng nằm cứng trong mã. Nếu trader muốn "cảnh báo tôi khi cách SL dưới 2%" thì phải sửa mã
và deploy lại.

### 3.5 Nguyên tắc II (Deterministic trước, AI sau) — TUÂN THỦ TỐT

Đây là điểm sáng thứ hai. Kiểm chứng cụ thể:

- `TradePreflightAnalysisService.cs:91-96` — nếu `!_llm.IsConfigured`, trả thẳng kết quả deterministic.
- `TradePreflightAnalysisService.cs:114-120` — nếu AI trả JSON hỏng sau cả 2 lần, trả deterministic
  và ghi cảnh báo, **không** chặn luồng.
- `TradePreflightAnalysisService.cs:126-127` — kẹp `Score` về [0,100], `Confidence` về [0,1].
- `MarketScanService.cs:283-284` — kẹp `Score` [0,5], `Confidence` [0,1].
- `MarketScanService.cs:644-669` — **kiểm chứng đầu ra AI bằng luật cứng**: đúng phía giá theo hướng,
  RR ≥ ngưỡng tài khoản, khoảng cách rủi ro > 0. AI nói gì cũng phải qua cửa này.
- `TradeAdvisorService.cs:232` — không có AI thì vẫn có `GenerateAdvice` deterministic đầy đủ.
- `LiveOrderService.cs:90-94` — ngoại lệ có chủ đích đúng hướng an toàn: **thiếu AI thì KHÔNG đặt
  lệnh**, chứ không phải đặt lệnh mù.

Đây là thiết kế đúng, và nó là **know-how thật** (xem Mục 7).

### 3.6 Những chỗ kiến trúc bị rò

| Vấn đề | Vị trí | Hệ quả |
|---|---|---|
| **Adapter Infrastructure commit DbContext dùng chung** | `BinanceFuturesOrderProvider.cs:456`, `:510` | `AuditAsync` gọi `_unitOfWork.CommitAsync(ct)` **ngay giữa luồng đặt lệnh**. Mọi thay đổi đang tracked (kể cả chưa sẵn sàng) bị flush theo. Vi phạm "một thao tác nghiệp vụ = một lần commit nhất quán". |
| **Đăng ký DI phụ thuộc thứ tự gọi** | `Application/DependencyInjection.cs:65-67` vs `Program.cs:80-81` | `IMacroEventProvider`, `IRealtimeNotificationSender`, `INotificationEmailQueue` đăng ký bản Noop ở `AddApplication()`, rồi bị ghi đè bởi bản thật ở `AddInfrastructure()`/`Program.cs`. Đảo thứ tự 2 dòng trong `Program.cs` → **toàn bộ notification realtime và email im lặng chuyển sang Noop mà không có lỗi nào**. |
| **`TryAddSingleton(ILogger<>, NullLogger<>)`** | `Application/DependencyInjection.cs:23` | Hiện là no-op vì host đã đăng ký logging trước. Nhưng nếu thứ tự thay đổi, **toàn bộ log ứng dụng biến mất im lặng** — trong khi Nguyên tắc IV yêu cầu ghi vết toàn bộ. Đây là một quả mìn. |
| **Repository trả `AsNoTracking` rồi service gọi `Update()`** | `BaseRepository.cs:38-42` + nhiều nơi | `FindListAsync` dùng `AsNoTracking`, nhưng `TradeResultSyncService.cs:92` gọi `_trades.Update(trade)` trên entity detached → EF đánh dấu **toàn bộ property** là modified. Hoạt động, nhưng sinh UPDATE thừa cột và tạo `RemoveRange` phải tự dò identity map (`BaseRepository.cs:95-118` — 24 dòng workaround cho đúng vấn đề này). |

---

## 4. Độ Phủ Test THẬT — Đối Chiếu Với Hiến Chương

### 4.1 Bức tranh tổng quát

69 test / 12.368 LOC = **1 test cho mỗi ~179 dòng**. Nhưng con số trung bình này vô nghĩa; phân bố
mới quan trọng:

| Vùng mã | LOC (xấp xỉ) | Số test | Đánh giá |
|---|---:|---:|---|
| Rule Engine + rules | ~230 | 11 | **Rất tốt** |
| Behavior detectors + analyzer | ~180 | 9 | **Rất tốt** |
| `TradeMetricsCalculator` | 68 | 4 | **Tốt** |
| `IndicatorService` | ~150 | 9 | **Tốt** |
| `MarketAnalyzer` | ~90 | 3 | Đủ |
| `SignalGenerator` (mã chết) | ~80 | 5 | Thừa — test cho mã không chạy |
| `BinanceParser` | 42 | 2 | Đủ |
| `LiveOrderService` | 547 | 19 | **Không đủ — xem 4.2** |
| `TradeService` (create/close/update/delete) | 233 | 5 | Chấp nhận được |
| `TradeWorkflowService` (tích hợp) | 53 | 2 | Đủ |
| **`MarketScanService`** | **1.028** | **0** | **Không có gì** |
| **`TradePreflightAnalysisService`** | **849** | **0** | **Không có gì** |
| **`TradeAdvisorService`** | **283** | **0** | **Không có gì** |
| **`TradeResultSyncService`** | **233** | **0** | **Không có gì** |
| **`NotificationService`** | **248** | **0** | **Không có gì** |
| **`MacroEventService`** | **234** | **0** | **Không có gì** |
| `BinanceFuturesOrderProvider` | 551 | 0 | Không có gì |
| `TradingDayService`, `SettingsService`, `MarketImportService`, `LiveBalanceService`, `NotificationPreferenceService`, `NotificationEmailJob` | ~500 | 0 | Không có gì |
| Toàn bộ 10 controller | ~1.626 | 0 | Không có gì (chấp nhận được cho UI, **không** chấp nhận được cho `ImportFromBinance`) |

**Tổng LOC không có bất kỳ test nào: ~4.500+ dòng ≈ 36% mã sản xuất**, và đó là 36% **phức tạp nhất**.

### 4.2 Đối chiếu 18 lớp chặn của `LiveOrderService` với 19 test — CÂU HỎI TRỌNG TÂM

Hiến chương Nguyên tắc VI: *"Mỗi lớp chặn ở Nguyên tắc III PHẢI có ít nhất một test chứng minh nó
**thực sự chặn**."*

Đọc `PlaceForTradeAsync` (`LiveOrderService.cs:56-344`), tôi đếm được **18 điểm thoát/chặn tuần tự**:

| # | Lớp chặn | Dòng | Test chứng minh chặn | Trạng thái |
|---:|---|---|---|---|
| 1 | Công tắc tổng `Enabled=false` | `:59` | `Does_Nothing_When_Master_Switch_Off` | ✅ |
| 2 | Idempotency `IsLive` | `:69` | `Is_Idempotent_On_Second_Call` | ✅ |
| 3 | Trạng thái lệnh ≠ `Open` | `:77` | — | ❌ **KHÔNG CÓ** |
| 4 | Tài khoản thiếu API key | `:83` | — | ❌ **KHÔNG CÓ** |
| 5 | AI chưa cấu hình | `:90` | `Blocks_When_Ai_Not_Configured` | ✅ |
| 6 | Trùng lệnh nội bộ (DB, giá xấp xỉ) | `:101-108` | `Blocks_When_Duplicate_Open_Trade` | ✅ |
| 7 | Trùng vị thế thật trên sàn | `:111-119` | — | ❌ **KHÔNG CÓ** |
| 8 | Giá vào ≤ 0 | `:126` | — | ❌ **KHÔNG CÓ** |
| 9 | SL thiếu / sai phía | `:138-146` | `Blocks_When_StopLoss_Missing`, `Blocks_When_SL_Wrong_Side_Long` | ✅ (thiếu case Short) |
| 10 | TP thiếu / sai phía | `:148-156` | `Blocks_When_TakeProfit_Missing` | ⚠️ **thiếu case TP sai phía** |
| 11 | Cap đòn bẩy | `:167-175` | `Leverage_Cap_Blocks_Without_Override` + `Override_Risk_Bypasses_Leverage_Cap` | ✅ |
| 12 | Lỗi lấy quy tắc khối lượng của sàn | `:189-192` | — | ❌ **KHÔNG CÓ** |
| 13 | Khối lượng hợp lệ ≤ 0 | `:193-197` | — | ❌ **KHÔNG CÓ** |
| 14 | **Chấm lại rule sau khi ép tăng qty** | `:200-208` | — | ❌ **KHÔNG CÓ** (test dùng `FakeWorkflow` trả rỗng — không chứng minh gì) |
| 15 | Notional dưới min sàn | `:212-216` | — | ❌ **KHÔNG CÓ** |
| 16 | Notional vượt cap | `:218-226` | `Blocks_When_Notional_Exceeds_Cap` | ✅ |
| 17 | Giới hạn lệnh live/ngày | `:229-235` | — | ❌ **KHÔNG CÓ** |
| 18 | Vi phạm rule mức Critical | `:238-249` | `Blocks_When_Critical_Flag` + `Override_Risk_Places_Despite_Critical` | ✅ |

**Kết quả: 9/18 lớp có test chặn (50%). 9 lớp có ZERO test.**

Nếu chấm theo đúng danh sách 13 cổng mà Nguyên tắc III liệt kê tên (bỏ qua các nhánh phụ), kết quả là
**7/13 cổng (54%)** có test.

**19 test còn lại làm gì?** 6 test không phải test-chặn mà là test-hành-vi-đúng:
`Places_Entry_Sl_Tp_When_All_Clear`, `Market_Long_Sends_Correct_Entry_Sl_Tp_Data`,
`Limit_Order_Sends_Entry_Price`, `Short_Order_Flips_Sides_And_Position`, `Block_Sets_Status_Cancelled`,
và 2 test retry SL/TP. Đây là những test **tốt và cần thiết** (kiểm tra chính xác payload gửi sàn:
`positionSide`, `closePosition`, `stopPrice`) — nhưng chúng không lấp được 9 lỗ hổng ở trên.

**Kết luận thẳng cho câu hỏi "19 test trên ~13 lớp chặn, đủ chưa?"**
> **Chưa đủ, và khoảng cách là đúng một nửa.** Con số 19 nghe có vẻ nhiều so với 13, nhưng 6 test
> không kiểm tra việc chặn, và các lớp còn thiếu bao gồm những lớp **quan trọng nhất về mặt tiền**:
> giới hạn lệnh live/ngày (#17), min notional (#15), trùng vị thế thật trên sàn (#7), và đặc biệt là
> **#14 — chấm lại rule sau khi hệ thống tự tăng khối lượng lên min sàn**. Lớp #14 là lớp mà hiến
> chương gọi đích danh: *"vì rủi ro thực tế đã thay đổi"*. Hiện không có test nào chứng minh việc
> chấm lại đó xảy ra và có tác dụng.

### 4.3 Vi phạm rõ ràng nhất của Nguyên tắc VI

Hiến chương: *"Bộ phân tích cú pháp phản hồi AI PHẢI có test với đầu vào lỗi định dạng: kèm markdown,
kèm văn bản thừa, JSON lồng, thiếu trường, số ngoài dải."*

**Số test hiện có cho yêu cầu này: 0.**

Trong khi đó mã parser đã được viết để xử lý **đúng 5 trường hợp đó**:
- kèm markdown → `ExtractJson` bóc ` ``` ` (`MarketScanService.cs:781-797`)
- kèm văn bản thừa → `ExtractBalancedObjects` (`:799-847`)
- JSON lồng → `UnwrapAiRoot` (`:754-766`)
- thiếu trường → `ReadString/ReadInt/ReadDecimal` với nhiều tên thay thế (`:864-908`)
- số ngoài dải → `Math.Clamp` (`:283-284`), `NormalizeConfidence` (`:942-948`)

Tác giả đã **nghĩ đúng** về vấn đề nhưng **không chứng minh** được là mã xử lý đúng. Đây là loại nợ
kỹ thuật rẻ nhất để trả: ~15-20 test bảng (`[Theory]` + `[InlineData]`) sẽ phủ toàn bộ, **ước lượng
1,5–2 ngày-người**.

---

## 5. Nợ Kỹ Thuật & Rủi Ro — Có `file:line`

Xếp theo mức độ: 🔴 Cao (mất tiền / mất dữ liệu) · 🟠 Trung bình-cao · 🟡 Trung bình · 🟢 Thấp

### 5.1 Bảo mật

| ID | Mức | Vấn đề | Vị trí | Chi tiết |
|---|---|---|---|---|
| S-01 | 🔴 | **Khoá API sàn lưu plaintext trong SQL Server** | `src/MMW.Domain/Entities/TradingAccount.cs:29-34` | `public string? ApiKey` / `ApiSecret` là `string?` thuần, `[MaxLength(200)]`, không converter mã hoá. Comment ghi *"Lưu bằng User Secrets hoặc encrypted"* nhưng **mã không làm vậy**. Khoá này có quyền **đặt lệnh futures** (dùng ở `LiveOrderService.cs:97`). Ai đọc được DB → đặt lệnh bằng tiền của chủ tài khoản. Vi phạm trực tiếp Nguyên tắc VII. |
| S-02 | 🔴 | **Mật khẩu admin mặc định hardcode + tự seed** | `src/MMW.Web/Data/SeedData.cs:11-12` | `DefaultUsername = "admin"`, `DefaultPassword = "Admin@123"` là `public const`, được seed tự động khi DB rỗng (`:29-40`). Không có cơ chế bắt đổi mật khẩu lần đầu. Nếu instance từng được expose ra Internet dù chỉ một lần → toàn quyền, bao gồm bật Hangfire dashboard và đọc audit chứa dữ liệu lệnh. |
| S-03 | 🟠 | **Hangfire dashboard mở cho mọi user đã đăng nhập** | `src/MMW.Web/Infrastructure/HangfireAuthorizationFilter.cs:11` | `return httpContext.User.Identity?.IsAuthenticated == true;` — không kiểm tra role. Từ dashboard có thể trigger thủ công `market-scan` (đường dẫn tới auto-đặt lệnh thật), xoá/retry job. Trong mô hình 1 user thì chấp nhận được; trong multi-user đây là **leo thang đặc quyền tức thì**. |
| S-04 | 🟠 | **Phụ thuộc có lỗ hổng đã biết mức cao** | `src/MMW.Application/MMW.Application.csproj:10` | `AutoMapper 14.0.0` — `NU1903 / GHSA-rvv3-g6hj-g44x`. Cảnh báo xuất hiện mỗi lần build, chưa xử lý. |
| S-05 | 🟡 | **File `.vs/` chứa chỉ mục mã nguồn được commit** | `.vs/MMW/CopilotIndices/.../CodeChunks.db`, `SemanticSymbols.db`, `.vs/MMW/v17/.suo` | 12 file IDE nằm trong git index. `CodeChunks.db` chứa các đoạn mã nguồn. Không phải rò rỉ secret, nhưng là rác không nên có và làm nặng repo. |
| S-06 | 🟢 | Redact secret trong audit **có làm đúng** | `BinanceFuturesOrderProvider.cs:518-534` | Ghi nhận điểm tốt: `signature` → `***redacted***`, API key → `MaskKey` (4 đầu + 4 cuối). Đúng Nguyên tắc IV. |

### 5.2 Tính đúng đắn / mất tiền

| ID | Mức | Vấn đề | Vị trí | Chi tiết |
|---|---|---|---|---|
| C-01 | 🔴 | **Trộn venue testnet ↔ mainnet** | `src/MMW.Application/Services/TradeResultSyncService.cs:134`, `src/MMW.Web/Controllers/TradesController.cs:458` | Cả hai nơi hardcode `useTestnet: false`. Nghĩa là: khi `LiveTrading.UseTestnet = true` (mặc định), lệnh được **đặt trên testnet**, nhưng job đồng bộ đọc vị thế và fills từ **mainnet**. Hệ quả: (a) lệnh testnet không bao giờ được đồng bộ đóng; (b) lớp bảo vệ "vị thế vẫn còn trên sàn thì không tự đóng" (`:157-158`) mất tác dụng vì đọc nhầm sàn; (c) fuzzy-match ±5% có thể khớp fill **mainnet** vào một lệnh **testnet** và ghi PnL sai vào sổ + sai số dư. |
| C-02 | 🟠 | **Không có khoá chống job chạy chồng lấn** | toàn bộ `src/MMW.Web/Program.cs:126-155` | `grep DisableConcurrentExecution` trả về **rỗng**. `market-scan` chạy cron `*/5`, cộng thêm `BackgroundJob.Enqueue` lúc khởi động (`:132`) và nút `ScanNow` cho user bấm bất kỳ lúc nào (`MarketController.cs:53-60`). Hai lượt scan song song → hai `TradeSignal` → hai `AutoCreateTradeFromSignalAsync` chạy đồng thời. Chống trùng chỉ dựa trên đọc DB (`MarketScanService.cs:477-486`) → **TOCTOU**: cả hai đọc trước khi bên nào ghi. `clientOrderId = mmw-{tradeId}` khác nhau nên Binance **không** chặn. Đây là con đường thực tế nhất để vi phạm SC-005 ("0 vị thế trùng"). |
| C-03 | 🟠 | **Ngày giao dịch tính theo UTC, không theo múi giờ trader** | `src/MMW.Application/Services/TradingDayService.cs:36`, `src/MMW.Application/Services/RuleEvaluationService.cs:66`, `src/MMW.Application/Services/LiveOrderService.cs:228` | `DateOnly.FromDateTime(trade.OpenedAt)` với `OpenedAt` là UTC; `sinceMidnight = DateTime.UtcNow.Date`. Trader ở Việt Nam (UTC+7) → **giới hạn "5 lệnh/ngày", "lỗ tối đa 3%/ngày" và "10 lệnh live/ngày" reset lúc 07:00 sáng giờ VN**, không phải nửa đêm. Một phiên giao dịch đêm bị cắt làm hai ngày thống kê. `VietnamTimeHelper` chỉ tồn tại ở tầng hiển thị (`src/MMW.Web/Helpers/VietnamTimeHelper.cs`). Đây là lỗi **làm sai chính cơ chế kỷ luật** mà sản phẩm hứa hẹn. |
| C-04 | 🟠 | **Vốn đầu ngày là ước lượng** | `src/MMW.Application/Services/TradingDayService.cs:74` | `day.StartingEquity = account.CurrentBalance - day.NetPnl;` với comment *"đủ dùng cho MVP"*. Nhưng `CurrentBalance` bị nạp/rút và bị sửa bởi cả `CloseAsync`, `DeleteAsync`, `TradeResultSyncService`. `DailyLossLimitRule` (Critical, chặn cả live order) đang dựa trên con số này. Đã ghi trong Phụ lục B #3 — vẫn chưa xử lý. |
| C-05 | 🟡 | **Rò rỉ transaction giữa các symbol khi quét lỗi** | `src/MMW.Application/Services/MarketScanService.cs:179-224` | `_history.AddAsync` (`:179`), `UpsertSnapshotAsync` (`:180`) và `_aiSignalAudits.AddAsync` (`:239`) đưa entity vào change tracker **trước** `CommitAsync` ở `:207`. Nếu symbol N ném exception, `catch` ở `:218` bỏ qua nhưng **các entity đã tracked vẫn ở đó** và sẽ được flush bởi `CommitAsync` của symbol N+1 — với `Status` dở dang. Đúng ra mỗi symbol phải có ranh giới giao dịch riêng. |
| C-06 | 🟡 | **Không có giao dịch bao quanh "tạo lệnh + chấm rule"** | `src/MMW.Application/Services/TradeService.cs:65-75` | `CreateAsync` commit lệnh trước, rồi mới gọi `_workflow.ProcessTradeAsync`. Nếu workflow ném exception → lệnh tồn tại **chưa được chấm**, vi phạm SC-001 ("không có lệnh nào chưa được chấm"). |
| C-07 | 🟡 | **`score` thập phân từ AI bị đọc thành 0** | `src/MMW.Application/Services/MarketScanService.cs:880-893` | `ReadInt` thử `int.TryParse("3.5")` → fail, rồi `TryGetInt32` trên `3.5` → cũng fail → trả `null` → `Score = 0` → bị loại vì dưới `minScore`. LLM hoàn toàn có thể trả `"score": 4.5`. Đề xuất tốt bị vứt im lặng. |
| C-08 | 🟢 | **`TryDeserializeAiResult` nhận cả JSON rỗng** | `src/MMW.Application/Services/TradePreflightAnalysisService.cs:503-513` | `JsonSerializer.Deserialize<TradePreflightAnalysisResult>("{}")` trả về object mặc định **khác null** → được coi là "AI đã trả lời hợp lệ" (`AiAnswered = true` ở `:123`). May mắn là `Decision` mặc định rỗng → `NormalizeDecision` → `"wait"` → không auto-tạo lệnh. An toàn hiện tại **do tình cờ**, không do thiết kế. |

### 5.3 Hiệu năng & chi phí vận hành

| ID | Mức | Vấn đề | Vị trí | Chi tiết |
|---|---|---|---|---|
| P-01 | 🟠 | **Nạp toàn bộ bảng vào RAM ở 4 nơi trên đường nóng** | (xem dưới) | Không phải N+1 kinh điển, mà là "load-all-then-filter-in-memory" — tệ hơn. |
| | | ↳ `BehaviorAnalysisService.cs:60-66` | | `FindListAsync(t => t.TradingAccountId == id && t.Id != trade.Id)` → **nạp mọi lệnh của tài khoản**, rồi `.Where().OrderBy().TakeLast(50)` trong C#. Chạy **mỗi lần lưu/sửa/đóng một lệnh**. |
| | | ↳ `TradingDayService.cs:33-39` | | `FindListAsync(t => t.TradingAccountId == id)` → nạp mọi lệnh, lọc theo ngày trong C#. Chạy **mỗi lần** `ProcessTradeAsync`. |
| | | ↳ `TradeService.cs:51-56` | | `GetAllAsync()` — nạp **mọi lệnh của mọi tài khoản** kèm `Include(TradingAccount)`, không filter, không paging. |
| | | ↳ `TradesController.cs:73-78` | | Gọi `GetAllAsync()` rồi `.Skip().Take()` **trong bộ nhớ**. Phân trang là ảo — DB luôn trả toàn bộ. |
| | | ↳ `HomeController.cs:83-84` | | `FindListAsync(...RealizedPnl != null)` rồi `.Sum()` trong C# thay vì `SUM()` trong SQL. |
| P-02 | 🟠 | **Chi phí LLM không kiểm soát** | `src/MMW.Web/Program.cs:143-146` + `src/MMW.Application/Services/TradeAdvisorService.cs:87-100` | Job `trade-advisor` chạy **cron `*/1` (mỗi phút)** và gọi `EnhanceWithLlmAsync` cho **từng lệnh đang mở**. Với 5 lệnh mở → **7.200 lời gọi LLM/ngày**, mỗi lời gọi gửi lại toàn bộ ngữ cảnh dù không có gì thay đổi. Không cache, không debounce, không ngưỡng "chỉ gọi khi giá đổi > x%". Ghi chú XML trong mã ghi *"mỗi 3 phút"* nhưng đăng ký thực tế là 1 phút. Ở single-user chi phí này chấp nhận được; **ở multi-user đây là mô hình đơn vị âm**. |
| P-03 | 🟡 | **Không có rate-limit / backoff khi gọi Binance** | `src/MMW.Infrastructure/DependencyInjection.cs:44-72`, `BinanceFuturesOrderProvider.cs:372-405` | Chỉ có `client.Timeout = 10s`. Không Polly, không theo dõi weight, không xử lý HTTP 429/418, không backoff luỹ thừa. Binance ban IP tạm thời khi vượt weight. `RetryAsync` (`LiveOrderService.cs:531-546`) retry ngay 3 lần cách 500ms — **retry mù, retry cả lỗi 4xx không nên retry**. Hiện an toàn vì watchlist nhỏ; sẽ vỡ khi số symbol tăng. |
| P-04 | 🟡 | **Gọi API sàn đồng bộ trong request pipeline** | `TradesController.cs:92-97` (`LoadOpenOrdersAsync`), `HomeController.cs:61-72`, `TradesController.cs:622` | Mỗi lần tải trang Trades (trang 1) hoặc Dashboard đều gọi Binance. Binance chậm/timeout → trang chậm 10 giây. `BuildCreateViewModelAsync` gọi `GetEffectiveBalanceAsync` **trong vòng lặp qua từng tài khoản** (`:616-626`). |
| P-05 | 🟡 | **Truy vấn đồng bộ trong action async** | `src/MMW.Web/Controllers/MarketController.cs:33-48` | `query.Count()` và `.ToList()` (không `Async`) chặn thread pool. |
| P-06 | 🟢 | **Truy vấn thừa khi upsert** | `MarketScanService.cs:1005-1006`, `TradingDayService.cs:80-88`, `TradeAdvisorService.cs:255-258` | Mẫu lặp lại: `FindListAsync(...)` (AsNoTracking) rồi `FindAsync(list[0].Id)` để lấy bản tracked → 2 round-trip cho mỗi upsert. Nguyên nhân gốc: `BaseRepository` mặc định `AsNoTracking` mọi thứ. |
| P-07 | 🟢 | **Thiếu index cho truy vấn nóng** | `src/MMW.Domain/DbContext/Configurations/TradeConfiguration.cs:14-20` | Có index `(TradingAccountId, Status)`, `Symbol`, `OpenedAt`, `(TradingAccountId, ExternalId)`. **Thiếu**: `(TradingAccountId, IsLive, CreatedDate)` cho bộ đếm lệnh live/ngày (`LiveOrderService.cs:229-230`); `(TradeId, Category)` trên `Flag` cho truy vấn idempotent (`RuleEvaluationService.cs:86-88`) — hiện chỉ có index trên `TradeId` đơn. |

### 5.4 Quy trình & tài liệu

| ID | Mức | Vấn đề | Chi tiết |
|---|---|---|---|
| D-01 | 🔴 | **~59 ngày công việc chưa commit** | Xem Mục 1.4. Không backup, không lịch sử, không bisect. Đây là rủi ro **cao hơn tất cả rủi ro mã nguồn cộng lại** vì nó có thể xoá sạch mọi thứ. |
| D-02 | 🟡 | **`SYSTEM_OVERVIEW.md` lạc hậu** | Thiếu notification, live order, macro event, audit. Đã ghi Phụ lục B #7. |
| D-03 | 🟡 | **Spec ghi sai số liệu triển khai** | `spec.md:351` ghi *"EF Core code-first (14 migration)"* — thực tế **27 file / 13 migration + snapshot**. `spec.md:351` ghi *"bcrypt"* — thực tế dùng `Microsoft.AspNetCore.Identity.PasswordHasher<User>` (PBKDF2), xem `AuthService.cs:9,29`. `spec.md:321` ghi job advisor *"mỗi 1 phút"* nhưng XML doc trong `TradeAdvisorService.cs:13` ghi *"mỗi 3 phút"*. |
| D-04 | 🟢 | **Không có CI** | `.github/` tồn tại nhưng không có workflow chạy build/test. 7 cổng chất lượng của hiến chương hiện được thực thi **thủ công bằng ý chí**, không bằng máy. |
| D-05 | 🟢 | **`SignalGenerator` là mã chết được bảo trì** | 5 test cho mã không nằm trên luồng nào. Phụ lục B #8 đã nêu, chưa quyết. |

---

## 6. Khoảng Cách Tới Multi-User (SaaS)

### 6.1 Chẩn đoán gốc: hệ thống **không có khái niệm chủ sở hữu dữ liệu**

Tôi đã `grep -rn "UserId" src`. Kết quả: `UserId` **chỉ tồn tại** trên `Notification` và
`NotificationPreference`. **Không một entity nghiệp vụ nào có chủ sở hữu**:

| Entity | Có `UserId`? | Hệ quả trong multi-user |
|---|---|---|
| `TradingAccount` | ❌ | Mọi user thấy mọi tài khoản, **kèm API key** |
| `Trade` | ❌ | Mọi user thấy mọi lệnh |
| `Flag` | ❌ | Cờ vi phạm của người khác hiển thị lẫn |
| `RiskSetting` | ❌ | gắn theo account, mà account vô chủ |
| `TradingDay`, `Strategy`, `TradeTag`, `TradeAnalysis` | ❌ | như trên |
| `WatchItem` | ❌ | Watchlist **dùng chung toàn hệ thống**; job scan quét chung |
| `MarketSnapshot`, `IndicatorRecord`, `TradeSignal` | ❌ | có thể chia sẻ được (dữ liệu thị trường) — điểm cộng nhỏ |
| `AppSetting` | ❌ **và là bản ghi đơn** | `AllowOverrideRisk`, `AutoCreateTradeFromSignal`, `MinSignalScore`, `DefaultTradingAccountId` là **cấu hình toàn cục**. Một user bật "bỏ qua rủi ro" → **nới lớp chặn cho tất cả mọi người**. |
| `AiSignalScanRecord`, `ExchangeApiAuditRecord` | ❌ | audit không truy được về user |

Cộng thêm:
- **Không có `IHttpContextAccessor` trong tầng Application** — không service nào biết "user hiện tại"
  là ai. Trạng thái user chỉ tồn tại ở controller (`CurrentUserId()` xuất hiện đúng 2 chỗ:
  `NotificationsController.cs:64`, `SettingsController.cs:144`).
- **Không có global query filter** trong `MmwDbContext`.
- **IDOR sẵn có**: `HomeController.Index(long? accountId)` (`:34`) nhận `accountId` từ query string và
  hiển thị bất kỳ tài khoản nào; `TradesController.CancelOpenOrder(long accountId, ...)` (`:105`)
  nhận `accountId` và **dùng API key của tài khoản đó để huỷ lệnh trên sàn**. Trong single-user vô
  hại; trong multi-user là lỗ hổng nghiêm trọng.
- **Job nền chạy theo phạm vi toàn hệ thống**: `TradeAdvisorService.cs:54`
  (`FindListAsync(t => t.Status == Open)` — mọi user), `TradeResultSyncService.cs:40`
  (mọi tài khoản có key), `MarketScanService.cs:159` (mọi watch item).
- **LLM provider chọn một lần lúc khởi động** (`Infrastructure/DependencyInjection.cs:84-123`) —
  không thể cho user tự chọn provider/API key riêng.

### 6.2 Bảng công việc & ước lượng

> **Toàn bộ số dưới đây là ƯỚC LƯỢNG chủ quan** của người kiểm kê, đơn vị **ngày-người**, giả định
> **một dev senior .NET thạo codebase này** (tức là chính tác giả), làm việc tập trung. Không phải
> báo giá, không phải cam kết.

**Giai đoạn A — Điều kiện cần để có thể mở cho người thứ hai (không thể bỏ qua)**

| # | Hạng mục | Ngày-người (ước lượng) | Ghi chú |
|---|---|---:|---|
| A1 | Thêm `UserId`/`TenantId` vào 14 entity nghiệp vụ + migration + backfill | 4–6 | Cơ học nhưng phải cẩn thận với dữ liệu hiện có |
| A2 | `ICurrentUser` service + global query filter trong `MmwDbContext` | 3–4 | Phải rà **mọi** repository call vì `BaseRepository` generic |
| A3 | Tách `AppSetting` thành cấu hình **theo user** (hiện là bản ghi đơn) | 2–3 | Chạm `SettingsService`, `MarketScanService`, `LiveOrderService`, `SettingsController` |
| A4 | Chuyển 5 Hangfire job từ "toàn hệ thống" sang "phân mảnh theo user" + `DisableConcurrentExecution` | 5–7 | Đây là phần khó nhất — job hiện quét bảng phẳng |
| A5 | **Mã hoá khoá API tại chỗ** (EF `ValueConverter` + DPAPI/Azure Key Vault/AWS KMS) + quy trình xoay khoá | 3–5 | Bắt buộc trước khi có user thứ hai. Xem S-01 |
| A6 | Bịt IDOR: kiểm tra quyền sở hữu ở mọi action nhận `accountId`/`tradeId`/`signalId` | 3–4 | ~10 controller |
| A7 | Phân quyền Hangfire dashboard theo role (hiện mọi user đã đăng nhập đều vào được) | 0,5 | Xem S-03 |
| A8 | Bỏ seed admin mặc định, bắt buộc đổi mật khẩu lần đầu, thêm khoá tài khoản sau N lần sai | 2–3 | Xem S-02 |
| A9 | Sửa C-01 (venue testnet/mainnet) + C-03 (múi giờ ngày giao dịch) | 3–4 | Hai lỗi này sai **số liệu kỷ luật**, không sửa thì multi-user chỉ nhân rộng cái sai |
| | **Cộng giai đoạn A** | **25,5 – 36,5** | |

**Giai đoạn B — Điều kiện cần để **thu tiền** được**

| # | Hạng mục | Ngày-người (ước lượng) |
|---|---|---:|
| B1 | Đăng ký / xác thực email / quên mật khẩu / mời thành viên | 5–7 |
| B2 | Gói dịch vụ, hạn mức (số tài khoản, số lệnh, số lần gọi AI), thực thi hạn mức | 5–8 |
| B3 | Tích hợp thanh toán (Stripe/Paddle/cổng nội địa) + webhook + hoá đơn | 6–10 |
| B4 | Đo & phân bổ chi phí LLM theo user; cache/debounce advisor (xem P-02) | 4–6 |
| B5 | Onboarding: hướng dẫn tạo API key **read-only** đúng cách, kiểm tra quyền khoá, cảnh báo nếu khoá có quyền rút tiền | 4–5 |
| B6 | Trang public + tài liệu + trang giá | 5–8 |
| B7 | Điều khoản dịch vụ, chính sách riêng tư, **tuyên bố miễn trừ tư vấn đầu tư** (bắt buộc — sản phẩm chạm tiền thật) | 3–5 *(cần tư vấn pháp lý ngoài, không tính vào ngày-người dev)* |
| | **Cộng giai đoạn B** | **32 – 49** |

**Giai đoạn C — Điều kiện cần để **vận hành được** với nhiều user**

| # | Hạng mục | Ngày-người (ước lượng) |
|---|---|---:|
| C1 | Tách Hangfire ra worker riêng (hiện in-process, cùng DB với app) | 3–5 |
| C2 | Rate-limit + circuit breaker cho Binance và LLM (Polly), tôn trọng weight | 3–5 |
| C3 | Sửa 5 điểm nạp-toàn-bảng (P-01) → paging + aggregate phía DB | 4–6 |
| C4 | Health check, metric, tracing, alert khi job chết | 3–5 |
| C5 | Sao lưu / khôi phục DB, quy trình migration không downtime | 3–4 |
| C6 | **Trả nợ test**: 9 lớp chặn còn thiếu + bộ test parser AI + test preflight/advisor/sync | 8–12 |
| C7 | CI (build + test + kiểm tra lỗ hổng phụ thuộc) | 1–2 |
| | **Cộng giai đoạn C** | **25 – 39** |

### 6.3 Tổng ước lượng

| Giai đoạn | Ngày-người (ước lượng) |
|---|---:|
| A — mở cho user thứ hai một cách an toàn | 25,5 – 36,5 |
| B — thu được tiền | 32 – 49 |
| C — vận hành được ở quy mô | 25 – 39 |
| **TỔNG** | **82,5 – 124,5 ngày-người** |

**Diễn giải thẳng**: với **một người làm toàn thời gian**, đây là **4 đến 6 tháng**. Nếu tác giả vẫn
đi làm full-time và chỉ làm buổi tối/cuối tuần (giả định ~2 ngày-người hiệu quả mỗi tuần), con số này
là **10 đến 15 tháng**.

Để so sánh: toàn bộ 12.368 dòng hiện có được viết trong khoảng 2 tháng. **Nghĩa là phần còn phải làm
để thương mại hoá lớn hơn toàn bộ phần đã làm.** Đây là con số quan trọng nhất trong tài liệu này.

---

## 7. Tài Sản Thật Sự Có Giá Trị vs Boilerplate

### 7.1 Know-how thật — khó copy, tốn thời gian thật để có được

| Tài sản | Vì sao khó copy | Bằng chứng |
|---|---|---|
| **Chuỗi 18 lớp chặn tuần tự, đúng thứ tự, có lý do cho từng lớp** | Không phải kiến thức lập trình mà là **kiến thức vận hành sàn futures**. Thứ tự đúng (dedup → SL/TP → cap → rule) chỉ có được sau khi từng bị các lỗi đó. Đặc biệt tinh tế: *lỗi entry thì huỷ lệnh, lỗi SL/TP thì KHÔNG huỷ vị thế* (`LiveOrderService.cs:272-283` vs `:312-328`). Đây là bài học tiền thật. | `LiveOrderService.cs:56-344` |
| **Tách bạch rào "rủi ro" nới được vs rào "kỹ thuật" luôn giữ** | `AllowOverrideRisk` chỉ nới cap đòn bẩy, cap notional, giới hạn lệnh/ngày, rule Critical — **không bao giờ** nới min-size sàn, chống trùng vị thế, bắt buộc SL/TP. Rất nhiều bot bỏ hết khi bật cờ override. | `LiveOrderService.cs:158-161`, `:169`, `:220`, `:231`, `:244` |
| **Ép khối lượng lên min sàn rồi CHẤM LẠI rule** | Một chi tiết mà 99% bot bỏ qua: sàn ép size lên → rủi ro thực tế tăng → phải chấm lại. Tác giả nghĩ ra và code đúng (dù chưa test). | `LiveOrderService.cs:199-208` |
| **Xử lý Hedge Mode vs One-way Mode của Binance** | Biết rằng `positionSide` bắt buộc ở Hedge, `reduceOnly` **không hợp lệ** ở Hedge, và phải hỏi `/fapi/v1/positionSide/dual` để biết. Kiến thức này chỉ có sau khi ăn lỗi `-4061`/`-1106`. | `BinanceFuturesOrderProvider.cs:64-100`, `:260-284` |
| **Snap `stepSize`/`tickSize`, và snap XUỐNG cho qty vs snap LÊN cho min-notional** | `SnapQuantity` dùng `Math.Floor` (không vượt rủi ro dự kiến), `SnapQuantityUp` dùng `Math.Ceiling` (để đạt min notional). Hai hàm khác nhau cho hai mục đích khác nhau — đây là chi tiết mà người chưa từng bị lỗi `-1111` sẽ không nghĩ ra. | `BinanceFuturesOrderProvider.cs:327-341` |
| **Bộ 3 behavior detector với ngưỡng leo thang** | Revenge (nặng hơn nếu vào sớm hơn 1/3 cửa sổ), LossStreak (Critical khi gấp đôi ngưỡng), OversizedAfterLoss (**chỉ xét khi lệnh trước là lệnh thua** — chi tiết quan trọng). Đây là **hiểu biết tâm lý trader**, không phải kỹ thuật. | `Behavior/Detectors/*.cs` + 9 test |
| **Parser JSON chịu lỗi 3 tầng cho LLM** | Canonical → Deserialize → Regex partial; bóc code fence; quét object cân bằng ngoặc có nhận biết chuỗi/escape; đọc nhiều tên trường thay thế; retry với prompt sửa lỗi. **~250 dòng kinh nghiệm đau thương với LLM.** Giá trị cao — nhưng đang bị nhân bản 2 lần và không test. | `MarketScanService.cs:695-948`, `TradePreflightAnalysisService.cs:433-801` |
| **Triết lý "deterministic trước, AI sau" được cài đặt nhất quán** | Không phải khẩu hiệu — kiểm chứng được ở 6 điểm trong mã (Mục 3.5). AI **không bao giờ** là điều kiện đủ; đầu ra AI **luôn** qua luật cứng. | xem Mục 3.5 |
| **Audit trail ghi cả khi KHÔNG sinh đề xuất** | `AiSignalScanRecord` được tạo ở dòng đầu (`MarketScanService.cs:238-239`) và cập nhật `Status`/`RejectReason` ở mọi nhánh thoát. Rất nhiều hệ thống chỉ log khi thành công. | `MarketScanService.cs:238-313` |

**Ước lượng thời gian để một dev .NET giỏi nhưng KHÔNG phải trader tái tạo phần này:**
**không phải vấn đề thời gian code, mà là vấn đề kinh nghiệm.** Code thì ~3–4 tuần (ước lượng);
nhưng để *biết cần code cái gì* thì phải trade futures thật vài trăm lệnh và ăn đủ các loại lỗi.
**Đây là tài sản thật.**

### 7.2 Boilerplate — ai cũng viết được trong ~1 tuần

| Phần | LOC ước tính | Đánh giá |
|---|---:|---|
| `BaseRepository` + `UnitOfWork` + `Result` + `PaginatedResult` | ~250 | Mẫu chuẩn, comment trong mã còn ghi *"gọn hoá từ BaseRepository của EOffice"* — tức là copy từ dự án cũ |
| 20 entity + 19 EF configuration | ~900 | Cơ học |
| 27 file migration | 16.710 | **Máy sinh, giá trị 0** |
| Cookie auth + `PasswordHasher` + `AccountController` | ~120 | Mẫu chuẩn |
| Adapter LLM (3 bản, mỗi bản ~90–200 dòng) | ~380 | Gọi HTTP + parse `choices[0].message.content`. Rất mỏng. |
| `SmtpEmailSender` | ~60 | Mẫu chuẩn |
| Notification CRUD + preference | ~330 | Cơ học |
| 23 Razor view + CSS glassmorphism | 3.287 | Có công, nhưng thay thế được bằng bất kỳ template admin nào |
| 10 controller (trừ phần logic lạc chỗ) | ~1.100 | Cơ học |
| `IndicatorService` (SMA/EMA/RSI/MACD/ATR) | ~150 | **Có sẵn ở mọi thư viện TA**; tự viết chỉ để tránh dependency. Giá trị thấp nhưng test tốt. |

**Ước tính tỷ lệ**: khoảng **75–80% của 12.368 dòng là boilerplate hoặc mã cơ học** (ước lượng).
Phần lõi thật sự có giá trị vào khoảng **2.500–3.000 dòng**: `LiveOrderService`, `RuleEngine/*`,
`Behavior/*`, phần validate + parser trong 2 service AI, và phần Hedge Mode/precision trong
`BinanceFuturesOrderProvider`.

**Nói thẳng**: giá trị của MMW **không nằm ở 12.368 dòng mã**. Nó nằm ở khoảng **2.700 dòng** thể hiện
kinh nghiệm giao dịch thật, cộng với **hiến chương v1.0.0 + baseline spec 50 FR** — hai tài liệu này
thực ra là tài sản trí tuệ cô đọng hơn cả mã nguồn, và trớ trêu thay chúng **chưa được commit**.

---

## 8. Kết Luận & Ba Việc Phải Làm Ngay

### 8.1 Phán quyết tổng thể

| Câu hỏi | Trả lời thẳng |
|---|---|
| Đây có phải sản phẩm không? | **Chưa.** Đây là một prototype nội bộ chất lượng khá, chạy được, phục vụ 1 người. |
| Kiến trúc clean thật hay trên giấy? | **Thật.** Đây là điểm mạnh nhất về mặt kỹ thuật. |
| Code có god-class không? | **Có 2**, cả hai đều ở lớp AI, cả hai đều 0 test, và chúng chia sẻ ~250 dòng trùng lặp. |
| Controller có logic nghiệp vụ không? | **Có**, rõ nhất là `TradesController.ImportFromBinance` (103 dòng). |
| 19 test có đủ cho các lớp chặn live order không? | **Không. Đúng 50%** — 9/18 lớp có test chặn. Lớp #14 (chấm lại rule sau khi tăng qty) là lỗ hổng đáng lo nhất. |
| Có sẵn sàng chạy tiền thật không? | **Chưa nên.** C-01 (trộn testnet/mainnet), C-02 (job chồng lấn), C-03 (múi giờ ngày), S-01 (khoá plaintext) đều phải xử lý trước. |
| Khoảng cách tới SaaS multi-user? | **82,5–124,5 ngày-người (ước lượng)** — lớn hơn toàn bộ phần đã làm. |
| Có phải dự án Web3 không? | **Không.** Không token, không smart contract, không blockchain. Đây là SaaS fintech đọc CEX. |
| Có khách trả tiền không? | **Không. Zero.** 1 người dùng, chính là tác giả. |

### 8.2 Ba việc phải làm trong tuần này, theo đúng thứ tự

1. **`git add -A && git commit`.** Ngay hôm nay. 59 ngày công việc và toàn bộ tài liệu thiết kế đang
   nằm trên một ổ đĩa duy nhất. Đồng thời `git rm -r --cached .vs/` để dọn 12 file rác. Đẩy lên remote
   riêng tư. **Đây là việc duy nhất trong danh sách này mà không làm thì mọi việc khác có thể trở
   thành vô nghĩa.**
2. **Mã hoá `TradingAccount.ApiKey`/`ApiSecret`** bằng EF `ValueConverter` + khoá từ User Secrets /
   biến môi trường. Hoặc, nếu chưa kịp: **thu hồi khoá futures hiện tại, cấp lại khoá read-only**, và
   chỉ nạp khoá trading khi thật sự cần. Rủi ro hiện tại là mất tiền thật.
3. **Viết test cho 9 lớp chặn còn thiếu** — ưu tiên #14 (chấm lại rule sau khi tăng qty), #17 (giới
   hạn lệnh live/ngày), #15 (min notional), #7 (trùng vị thế trên sàn). **Ước lượng 2–3 ngày-người.**
   Chi phí thấp nhất trong toàn bộ danh sách nợ, và nó bảo vệ đúng chỗ tiền chảy ra.

### 8.3 Một câu kết

> MMW là một **prototype được thiết kế tốt hơn mức bình thường của dự án cá nhân**, với một lõi kỷ
> luật deterministic đáng tin cậy và một lượng know-how giao dịch thật không nhỏ. Nhưng nó **chưa là
> sản phẩm, chưa an toàn để chạy tiền thật ở mức hiện tại, và cách nó thương mại hoá còn xa hơn cả
> quãng đường đã đi**. Giá trị lớn nhất cần bảo vệ ngay lúc này không phải là 12.368 dòng mã — mà là
> ~2.700 dòng know-how cộng hai tài liệu thiết kế, **và cả ba thứ đó hiện đang không được commit.**

---

*Tài liệu này được lập bằng cách đọc mã nguồn trực tiếp và chạy build/test thật lúc 2026-07-29.
Mọi khẳng định kỹ thuật đều neo vào `đường/dẫn/file.cs:dòng`. Mọi con số công sức đều là ước lượng
chủ quan và được đánh dấu rõ. Không có số liệu thị trường nào được đưa ra vì không có nghiên cứu
thị trường trong phạm vi lượt kiểm kê này.*
