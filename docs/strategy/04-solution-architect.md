# 04 — Thẩm định kiến trúc & lộ trình kỹ thuật MMW

> **Loại tài liệu**: thẩm định kiến trúc giải pháp (solution architecture due diligence), vòng 2.
> **Ngày lập**: 2026-07-30 · **Vai**: Senior Web3/Crypto Solution Architect
> **Đầu vào**: `docs/strategy/01-market-landscape.md` · `02-product-reality.md` · `03-financial-analyst.md` ·
> `.specify/memory/constitution.md` v1.0.0 · `specs/001-mmw-system-baseline/spec.md` · **đọc trực tiếp mã nguồn** tại `D:/KYLT/MMW`.
> **Công cụ**: `WebSearch`/`WebFetch` **vẫn lỗi backend** tại 2026-07-30 (`deepseek-v4-pro`), thử đúng một lần, không retry.
> Đã dùng browser để lấy **số liệu rate limit thật của Binance từ chính API của sàn** (mục 2.1) — đây là số liệu mới,
> không có trong file 01–03.
> **Miễn trừ**: đây là phân tích kỹ thuật và kiến trúc. KHÔNG chứa lời khuyên đầu tư cá nhân, không khuyến nghị
> mua/bán tài sản nào, không thay thế tư vấn pháp lý.

---

## 0. Kết luận trước, luận cứ sau

Bảy câu, để không phải đọc 1.200 dòng mới biết:

1. **Kiến trúc phân tầng là thật, nhưng Port/Adapter thì chỉ đúng một nửa — và nửa sai đang gây bug tiền thật.**
   `IExchangeOrderProviderFactory.Create` có tham số `useTestnet` (`IExchangeOrderProviderFactory.cs:9`),
   `IExchangeAccountProviderFactory.Create` **thì không** (`BinanceAccountProviderFactory.cs:19`). Đây mới là
   **nguyên nhân gốc** của bug C-01 mà file 02 mô tả; sửa hai dòng `useTestnet: false` **không** sửa được gốc.

2. **Phát hiện nghiêm trọng nhất của lượt này: hệ thống phân tích trên nến SPOT rồi đặt lệnh trên PERP.**
   `BinanceMarketDataProvider.cs:45` gọi `/api/v3/klines` tới `data-api.binance.vision`
   (`BinanceOptions.cs:8`) — đó là **thị trường giao ngay**. Lệnh thì đi tới `fapi`/testnet futures.
   `grep -i "funding|markPrice|premiumIndex"` trên toàn bộ `src` trả về **rỗng**. Nghĩa là ATR, EMA, RSI, và
   **giá SL/TP tuyệt đối** đều tính trên một chuỗi giá **khác** với công cụ đang giao dịch.

3. **Phát hiện nghiêm trọng thứ hai: một quả mìn vi phạm hiến chương, có nguồn từ chính tài liệu Binance.**
   Binance ghi rõ HTTP 503 kèm *"Unknown error, please check your request or try again later"* nghĩa là
   **trạng thái thực thi KHÔNG XÁC ĐỊNH, lệnh có thể đã thành công** [Binance Developer Docs — Futures (USDⓈ-M)
   General Info, truy cập 30/07/2026]. `LiveOrderService.cs:272-283` bắt **mọi** exception ở bước entry và
   **huỷ lệnh hệ thống**. Kết quả: vị thế thật tồn tại trên sàn, sổ ghi `Cancelled` — đúng thứ Nguyên tắc III
   cấm bằng chữ *"không để lại vị thế 'ma'"*.

4. **Đòn bẩy chi phí AI lớn hơn file 03 ước lượng, và lý do là một lập luận về tính đúng đắn chứ không phải tối ưu.**
   Quét 288 lần/ngày trên khung 1h là **11/12 lần gửi lại đúng cùng một bộ chỉ báo** cho LLM. Gate theo
   **nến đóng** thay vì đồng hồ tường cắt −91,7% ngay lập tức, trước mọi biện pháp khác. Cộng với việc
   **xoá hẳn LLM khỏi job advisor định kỳ** (nhánh deterministic đã đầy đủ và *chính xác hơn*), tổng giảm
   **≈ −98,6%**, đưa chi phí cá nhân từ ~$60/tháng về **~$2/tháng (ước lượng)**. Công sức **7–11,5 ngày-người**.

5. **Không giữ khoá API của người khác là lựa chọn kiến trúc đúng, và nó trùng khớp với kết luận tài chính.**
   Giá trị sản phẩm nằm ở việc **đứng chắn đường vào lệnh**; đứng chắn đường thì phải có quyền đặt lệnh;
   có quyền đặt lệnh của người lạ mà không có pháp nhân, không bảo hiểm, không SOC2, không on-call là
   không chịu trách nhiệm được. Hai lối thoát duy nhất: **user tự chạy (self-host)** hoặc **đối tác đã cầm khoá rồi
   (prop firm)**. Đúng bằng #9 và #5 của file 03. Kiến trúc và tài chính hội tụ — đó là tín hiệu mạnh.

6. **Blockchain: 4/4 KHÔNG.** Và với on-chain track record tôi **đồng ý và củng cố** kết luận của file 03 §4.6 bằng
   một lập luận mạnh hơn: neo hash lên chain chứng minh **MMW không chối được bản ghi MMW tự tạo**, nó
   **không** chứng minh bản ghi đó đúng. Neo tin cậy không đổi. Chữ ký Ed25519 + transparency log append-only
   đạt cùng kết quả với 2–4 ngày-người và không có ví, không gas, không phụ thuộc chain.

7. **Viết lại frontend là cái bẫy lớn nhất và nó không giải quyết vấn đề thật.**
   Trang Trades chậm không phải vì Razor — mà vì `TradesController.cs:92-97` gọi Binance **đồng bộ trong request**
   với timeout 10 giây (`Infrastructure/DependencyInjection.cs:48`). SPA rewrite ≈ **25–45 ngày-người (ước lượng)**,
   bằng **1,5–3 lần toàn bộ backlog ROI cao** của file 03 §7.1, và sửa được **0/4** vấn đề thật.

**Phán quyết kiến trúc**: *Kiến trúc hiện tại đủ tốt để một người dùng nó an toàn sau ~20–28 ngày-người sửa chữa.
Nó KHÔNG đủ tốt để chở người thứ hai, và khoảng cách đó lớn hơn 15–20% so với ước lượng của file 02.
Lộ trình đúng là làm cho công cụ của chính mình đúng và rẻ, giữ nguyên các cổng mở, và không viết một dòng
code multi-tenant nào cho tới khi có một khách hàng nói "có".*

---

## 1. Đánh giá kiến trúc hiện tại

### 1.1 Chiều phụ thuộc — CLEAN THẬT, xác minh lại độc lập

Tôi kiểm tra bằng `ProjectReference` chứ không bằng tài liệu:

| Project | Tham chiếu | Đánh giá |
|---|---|---|
| `MMW.Domain` | (không có project ref) | ✅ Sạch |
| `MMW.Application` | `Domain`, `Shared` (`MMW.Application.csproj:17-18`) | ✅ **Không** tham chiếu `Infrastructure` |
| `MMW.Infrastructure` | `Application`, `Domain`, `Shared` (`MMW.Infrastructure.csproj:24-26`) | ✅ Adapter phụ thuộc port, đúng chiều |
| `MMW.Web` | cả 4 (`MMW.Web.csproj:21-24`) | ✅ Composition root |

Đây là **điểm mạnh thật và cần giữ nguyên**. Nhưng có một chi tiết đáng chú ý về quản trị:

> **Hiến chương ghi sai chính chiều phụ thuộc mà nó bảo vệ.**
> `constitution.md:118` viết *"Chiều phụ thuộc là một chiều: `Web → Application → Infrastructure → Domain`"*.
> Mã nguồn thì `Infrastructure → Application` (`MMW.Infrastructure.csproj:24`). Mã đúng, hiến chương sai.
> Trong một dự án mà hiến chương là "luật tối cao", một câu sai ở nguyên tắc V là nợ cần trả (PATCH, 10 phút).

### 1.2 Port/Adapter — đúng về hình thức, sai về ngữ nghĩa ở ba chỗ

Port được khai báo trong `Application` và cài trong `Infrastructure` — đúng. Nhưng **hợp đồng của port không mô tả
đủ bài toán**, và đó là chỗ rò rỉ thật.

#### Rò rỉ 1 — Port không mang khái niệm "venue". Đây là nguyên nhân gốc của C-01.

| Port | Chữ ký | Có tham số venue? |
|---|---|---|
| `IExchangeOrderProviderFactory` | `Create(apiKey, apiSecret, bool useTestnet)` (`IExchangeOrderProviderFactory.cs:9`) | ✅ Có |
| `IExchangeAccountProviderFactory` | `Create(apiKey, apiSecret)` (`BinanceAccountProviderFactory.cs:19`) | ❌ **KHÔNG** |

Hệ quả dây chuyền, tệ hơn nhiều so với mô tả của file 02:

- `BinanceAccountProvider.cs:41-43` **hardcode** `https://fapi.binance.com` cho `/fapi/v2/balance`
  và `:56-59` cho `/fapi/v1/userTrades`. Không có đường nào để trỏ sang testnet.
- `LiveBalanceService.GetEffectiveBalanceAsync` (`LiveBalanceService.cs:38-40`) gọi factory này để lấy
  **số dư dùng tính khối lượng theo % rủi ro**. Nghĩa là: khi `UseTestnet=true` (mặc định,
  `LiveTradingOptions.cs:15`), hệ thống **tính size theo số dư MAINNET rồi đặt lệnh lên TESTNET**.
- Tệ hơn: khoá testnet của Binance là **bộ khoá hoàn toàn riêng** với mainnet. `TradingAccount` chỉ có
  **một** cặp khoá (`TradingAccount.cs:29-34`). Nên ở chế độ testnet, lời gọi mainnet sẽ 401 → rơi vào
  `catch { return fallback; }` (`LiveBalanceService.cs:44-47`) → **im lặng** dùng `CurrentBalance` trong DB.
  Không log, không cảnh báo. Người dùng không có cách nào biết.

> **Sửa hai dòng `useTestnet: false` (`TradeResultSyncService.cs:134`, `TradesController.cs:458`) KHÔNG sửa được bug này.**
> Phải sửa **hợp đồng port**: thêm venue vào `IExchangeAccountProviderFactory.Create`, và cho `TradingAccount`
> mang khái niệm venue của riêng nó (một tài khoản là *testnet* hoặc *mainnet*, không phải một cờ toàn cục).
> **Ước lượng 2–3 ngày-người**, thay vì "sửa 2 dòng".

#### Rò rỉ 2 — Phân tích trên SPOT, giao dịch trên PERP. (Phát hiện mới, không có trong file 01–03.)

```
BinanceOptions.cs:8       MarketDataBaseUrl = "https://data-api.binance.vision"   ← SPOT public data
BinanceMarketDataProvider.cs:45   GET /api/v3/klines?symbol=...                   ← SPOT klines
BinanceOptions.cs:14      FuturesApiBaseUrl = "https://fapi.binance.com"          ← USDT-M PERP
BinanceFuturesOrderProviderFactory.cs:30  → đặt lệnh trên PERP (hoặc testnet PERP)
```

`grep -rni "funding|markPrice|premiumIndex" src --include=*.cs` → **0 kết quả**.

Hệ quả cụ thể, không phải lý thuyết:

| Thứ được tính trên SPOT | Được dùng để làm gì trên PERP |
|---|---|
| `analysis.Atr` (`MarketScanService.cs:391`) | LLM được dặn *"tối thiểu 1 ATR khỏi entry"* (`MarketScanService.cs:69-70`) |
| `analysis.Price`, EMA20/50, RSI, MACD | Toàn bộ quyết định LONG/SHORT/WAIT |
| `signal.Entry / StopLoss / TakeProfit` | **Giá tuyệt đối** gửi thẳng lên sàn perp (`LiveOrderService.cs:261-270, 290-306`) |

Với BTCUSDT basis thường nhỏ; với altcoin và trong giai đoạn funding căng, spot và perp **lệch nhau**.
Thêm nữa: `BinanceFuturesOrderProvider.cs:79-88` **không truyền `workingType`** cho `STOP_MARKET`/
`TAKE_PROFIT_MARKET` → dùng mặc định của sàn. Nghĩa là trong cùng một quyết định có tới **ba hệ quy chiếu giá**:
spot close (tính chỉ báo) · perp last (đặt lệnh) · perp mark hoặc last (kích hoạt SL/TP, tuỳ mặc định sàn —
**cần kiểm chứng bằng test tích hợp**, tôi không xác minh được mặc định trong lượt này).

**Đây là lỗi tính đúng đắn ở lớp lõi và nó chưa từng được ghi nhận.** Sửa: đổi sang `/fapi/v1/klines` +
`/fapi/v1/premiumIndex` cho mark price. **1,5–2,5 ngày-người (ước lượng)** — nhưng phải làm lại toàn bộ
đường số liệu chỉ báo, nên phải có test trước.

#### Rò rỉ 3 — Port market data nói tiếng Binance

`IMarketDataProvider.GetCandlesAsync(string symbol, string interval, int limit, ...)`:
`interval` là **chuỗi thô của Binance** (`"1h"`, `"5m"`), `symbol` là **định danh của Binance**.

Kết luận của file 02 §3.1 — *"thêm sàn thứ hai là công việc bình thường, không phải viết lại"* — **quá lạc quan**.
Nó đúng cho **đặt lệnh** (port `IExchangeOrderProvider` đủ trừu tượng), nhưng **sai cho dữ liệu và định danh**:
Bybit/OKX/Hyperliquid dùng ký hiệu symbol và từ vựng interval khác nhau; `WatchItem.Symbol`,
`Trade.Symbol`, `MarketSnapshot.Symbol` đều là chuỗi phẳng không mang venue. Thêm sàn thứ hai cần một
lớp chuẩn hoá symbol + interval **chưa tồn tại**: **+3–5 ngày-người (ước lượng)** ngoài mọi con số đang có.

### 1.3 Rule plug-in — đúng 70%, và 30% còn lại là chỗ quan trọng

`TradeRuleEngine.Evaluate` đúng là chỉ `foreach (var rule in _rules)` (`RuleEngine/IRuleEngine.cs`, phần impl).
Thêm rule **không** phải sửa engine. Nhưng "thêm rule mà không sửa gì khác" thì **sai**, vì bốn ràng buộc:

| # | Ràng buộc | Bằng chứng | Hệ quả |
|---|---|---|---|
| 1 | `ITradeRule.Type` trả về `FlagType` — một **enum trong Domain** | `ITradeRule.cs`, `TradingEnums.cs:116-132` | Thêm rule ⇒ **sửa `MMW.Domain`**. Không thể thêm rule từ một assembly plug-in riêng. |
| 2 | `RuleEvaluationContext` là hợp đồng **đóng, 4 trường**: `Trade`, `Settings`, `AccountEquity`, `Day` | `RuleEvaluationContext.cs:11-22` | Rule cần dữ liệu khác (vd: "không vào lệnh trong vùng tin CPI", "tổng exposure tương quan", "cooldown sau lệnh thua") ⇒ **phải sửa cả context lẫn `RuleEvaluationService`** (`RuleEvaluationService.cs:74-80`). |
| 3 | `RiskSetting` là entity có **cột cố định** | `RiskSetting.cs:16-42` | Rule mới có ngưỡng mới ⇒ **thêm cột + migration EF**. Trái tinh thần "ngưỡng đọc từ cấu hình" của Nguyên tắc I ở quy mô. |
| 4 | `Evaluate` là **đồng bộ**, không `async` | `ITradeRule.cs` | Rule cần I/O (hỏi vị thế trên sàn, hỏi lịch vĩ mô, hỏi lịch sử) **không cài đặt được** mà không đổi interface. |

Cộng thêm một rủi ro vận hành: **engine không cô lập ngoại lệ**. Một rule ném exception → cả `Evaluate` ném →
`TradeWorkflowService` ném → `TradeService.CreateAsync` để lại lệnh **chưa được chấm** (đúng C-06 của file 02),
vi phạm SC-001. Sửa: `try/catch` từng rule + sinh một `Flag` mức Critical "rule lỗi" thay vì im lặng.
**0,5 ngày-người.**

**Điểm sáng đối chứng**: `BehaviorContext` làm đúng hơn hẳn — nó mang **cả `History` các lệnh trước**
(`BehaviorContext.cs:14`), nên một detector mới có thể tự tính bất cứ thứ gì. Nếu muốn rule engine thật sự
plug-in, hãy làm cho `RuleEvaluationContext` giống `BehaviorContext`: mang dữ liệu thô, không mang kết luận.

**Phán quyết**: *plug-in cho "thêm một rule dùng dữ liệu và ngưỡng đã có" — đúng, chi phí ~1 giờ.
Plug-in cho "thêm một rule cần dữ liệu mới" — sai, chi phí 0,5–2 ngày-người và chạm 3 tầng.*
Vì hiến chương nói *"Nếu phải sửa engine để thêm quy tắc, thiết kế đó sai"* (`constitution.md:127-128`),
đây là vi phạm **một phần** nguyên tắc V — không phải ở engine, mà ở **hợp đồng dữ liệu quanh engine**.

### 1.4 God-class — phán quyết có, nhưng lý do khác file 02

**`MarketScanService.cs` (1.028 dòng): CÓ, god-class.** Tôi đồng ý với bảng 6 trách nhiệm của file 02 §3.3,
và bổ sung một lý do nghiêm trọng hơn số dòng:

> **Nó là service duy nhất trong hệ thống vừa điều phối job, vừa gọi AI, vừa kiểm chứng AI, vừa TỰ TẠO LỆNH,
> vừa TỰ GỬI LỆNH THẬT.** `MarketScanService.cs:504` gọi thẳng `_liveOrders.PlaceForTradeAsync(tradeId, ...)`.
> Nghĩa là **đường từ "một watch item trong DB" tới "một lệnh futures tiền thật" nằm trọn trong một class**,
> không có ranh giới nào để đặt một cổng kiểm soát ở giữa.

Đó là vấn đề kiến trúc thật, không phải vấn đề thẩm mỹ: nó khiến việc **kiểm thử** đường tiền thật đòi phải
dựng cả 17 dependency của constructor (`MarketScanService.cs:117-135`) — và đó là lý do thật sự
khiến file này có **0 test**, chứ không phải vì tác giả lười.

**`TradePreflightAnalysisService.cs` (849 dòng): CÓ**, nhưng nhẹ hơn — nó không tự gây tác dụng phụ, chỉ trả kết quả.

**Trùng lặp parser — xác minh lại, đúng:** 7 hàm cùng tên tồn tại ở cả hai file:

```
MarketScanService.cs        :754 UnwrapAiRoot  :768 ExtractJsonCandidates  :781 ExtractJson
                            :799 ExtractBalancedObjects  :895 ReadDecimal  :942 NormalizeConfidence
TradePreflightAnalysisService.cs :607 ExtractJson :625 ExtractJsonCandidates :638 ExtractBalancedObjects
                            :688 UnwrapAiRoot  :733 ReadDecimal  :795 NormalizeConfidence
```

Đây là **~250 dòng khó nhất, 0 test, nhân đôi**. Tách thành `LlmJsonReader` trong `MMW.Application/Ai/`:
**1–2 ngày-người**, và nó **mở khoá** cho bộ test bảng mà Nguyên tắc VI đòi (`constitution.md:146-147`) —
vì test một static class thuần thì không cần dựng 17 dependency.

### 1.5 Bốn phát hiện mới của lượt này (không có trong file 01–03)

| # | Phát hiện | Vị trí | Mức |
|---|---|---|---|
| **A-01** | **Xử lý sai HTTP 503 "Unknown error" → vị thế ma.** Binance ghi rõ: *"Do not treat as immediate failure; first verify via WebSocket updates or orderId queries to avoid duplicates"* [Binance Developer Docs, General Info, truy cập 30/07/2026]. `LiveOrderService.cs:272-283` bắt **mọi** exception ở entry và set `Status = Cancelled`. Vị thế có thể đã tồn tại thật. | `LiveOrderService.cs:272-283` | 🔴 **Vi phạm Nguyên tắc III** (`constitution.md:90-91`) |
| **A-02** | **Phân tích SPOT, giao dịch PERP; không có funding/mark price.** | `BinanceOptions.cs:8`, `BinanceMarketDataProvider.cs:45` | 🔴 Sai tính đúng đắn ở lõi |
| **A-03** | **Log ứng dụng nằm trong `wwwroot` và được phục vụ TRƯỚC lớp xác thực (môi trường Development).** `Program.cs:18-19` ghi log vào `wwwroot/log`; `app.UseStaticFiles()` ở `Program.cs:94` chạy **trước** `app.UseAuthentication()` ở `:106`. `FallbackPolicy` chỉ áp cho endpoint, **không** cho static file. → `GET /log/file06092026-.txt` tải được **không cần đăng nhập**. | `Program.cs:18-19, 94, 106` | 🟠 Rò rỉ thông tin |
| **A-04** | **4 script/CSS nạp từ CDN, KHÔNG có `integrity` (SRI), KHÔNG có CSP.** `grep "Content-Security-Policy\|integrity="` trên `src/MMW.Web` → rỗng. Một script bị thay đổi chạy trong phiên đã đăng nhập, đọc được antiforgery token trong DOM, và POST được tới `/Trades/...`. **Đây là đường tới tiền thật KHÔNG cần khoá API** — mã hoá khoá không chặn được nó. | `_Layout.cshtml:22-24, 148-151` | 🟠 Chuỗi cung ứng |

Ngoài ra, một điểm **tích cực** cần ghi nhận và giữ: `clientOrderId = $"mmw-{tradeId}"`
(`LiveOrderService.cs:256`) và `"{clientId}-sl"` / `"{clientId}-tp"` (`:294, :305`) là **định danh tất định**.
Nghĩa là `RetryAsync` 3 lần (`LiveOrderService.cs:531-546`) — dù là retry mù — **không tạo lệnh trùng**,
vì Binance từ chối `clientOrderId` trùng. Đây là đúng ràng buộc `constitution.md:190-191`. Điểm cộng thật.

### 1.6 Bảng điểm kiến trúc

| Chiều | Điểm /10 | Nhận xét một câu |
|---|---:|---|
| Chiều phụ thuộc / phân tầng | **9** | Sạch thật, giữ nguyên |
| Port/Adapter — hình thức | 8 | Interface đúng chỗ |
| Port/Adapter — **ngữ nghĩa** | **4** | Thiếu venue, thiếu chuẩn hoá symbol/interval, sai thị trường |
| Rule/Behavior plug-in | 7 | Đúng cho rule đơn giản; sai khi cần dữ liệu mới |
| Tính đúng đắn đường tiền thật | **4** | 18 lớp chặn tốt, nhưng A-01 + A-02 + 9/18 lớp không test |
| Khả năng kiểm thử | **3** | 2 god-class × 17 dependency ⇒ 1.877 dòng lõi AI có 0 test |
| Bảo mật | **2** | Khoá plaintext + seed `Admin@123` + không CSP/SRI + log lộ |
| Sẵn sàng scale | **2** | Không rate-limit, không `DisableConcurrentExecution`, không tenant |
| Chi phí vận hành theo thiết kế | **1** | Chi phí AI tuyến tính theo user, không chia sẻ được |
| **Tổng thể** | **4,4/10** | *Lõi tốt hơn mong đợi, vỏ chưa chịu được tiền thật của người khác* |

---

## 2. Scalability — từ 1 user tới 1.000 và 10.000

### 2.1 Rate limit THẬT của Binance — số liệu mới, lấy trực tiếp từ sàn hôm nay

File 01 và 03 **không có** số này. Tôi lấy trực tiếp từ chính API:

| Nguồn | Giới hạn | Giá trị |
|---|---|---:|
| USDⓈ-M Futures (`fapi`) | `REQUEST_WEIGHT` / 1 phút | **2.400** |
| | `ORDERS` / 1 phút | **1.200** |
| | `ORDERS` / 10 giây | **300** |
| Spot & `data-api.binance.vision` | `REQUEST_WEIGHT` / 1 phút | **6.000** |
| | `RAW_REQUESTS` / 5 phút | **300.000** |

[`GET https://fapi.binance.com/fapi/v1/exchangeInfo` và `GET https://data-api.binance.vision/api/v3/exchangeInfo`, truy cập 30/07/2026]

Chính sách phạt, trích nguyên văn [Binance Developer Docs — Futures (USDⓈ-M) General Info, truy cập 30/07/2026]:

- *"HTTP 429 return code is used when breaking a request rate limit."*
- *"HTTP 418 return code is used when an IP has been auto-banned."*
- *"IP bans are tracked and scale in duration for repeat offenders, from 2 minutes to 3 days."*
- *"The limits on the API are based on the IPs, not the API keys."*

> **Câu cuối là câu quan trọng nhất cho multi-tenant, và nó chưa ai nói:**
> giới hạn tính **theo IP, không theo khoá**. Trong một SaaS, **mọi user dùng chung IP của server**.
> Nghĩa là một user với watchlist 300 symbol có thể làm **toàn bộ nền tảng bị ban 2 phút → 3 ngày**.
> Đây là **shared-fate failure** — một chế độ hỏng mà kiến trúc hiện tại không có bất kỳ phòng vệ nào.

### 2.2 Điểm vỡ thật KHÔNG phải rate limit — mà là thời gian tường của vòng lặp tuần tự

Hãy tính đúng số request, không phóng đại.

**Điều mà file 03 §5.5 bỏ sót**: `BinanceMarketDataProvider` **đã có cache** —
`IMemoryCache` với TTL 15 giây, khoá `klines:{symbol}:{interval}:{limit}` (`BinanceMarketDataProvider.cs:40-48`),
và `AddMemoryCache()` đăng ký **singleton** (`Infrastructure/DependencyInjection.cs:42`). Nghĩa là trong **một
process**, số lời gọi Binance tỉ lệ với **số symbol phân biệt**, không phải số user.

Vậy con số "100 user × 10 symbol × 288 lượt = 288.000 request/ngày" của file 03 §5.5 là **quá cao**;
con số đúng là **288 × số symbol phân biệt** (giả sử mỗi lượt quét cách nhau > 15 giây).

| Kịch bản | Symbol phân biệt (ước lượng) | Request klines/ngày | Trung bình req/s | Weight/phút lúc burst (giả định weight=2/lượt) |
|---|---:|---:|---:|---:|
| 1 user, 5 symbol | 5 | 1.440 | 0,02 | ~10 |
| 1.000 user × 10 symbol | ~200–400 *(phân bố Zipf: BTC/ETH/SOL có mặt ở gần hết watchlist)* | 57.600–115.200 | 0,7–1,3 | 400–800 |
| 10.000 user × 10 symbol | ~400–600 *(trần thực tế: số perp USDT-M có thanh khoản)* | 115.200–172.800 | 1,3–2,0 | 800–1.200 |

*(Weight/lượt của `/api/v3/klines` phụ thuộc tham số `limit`; MMW dùng `limit=200` (`MarketScanService.cs:22`)
và `limit=100` (`TradeAdvisorService.cs:17`). **Tôi không xác minh được bảng weight theo `limit` trong lượt này**
— trang tài liệu endpoint trả 404. Con số 2 ở trên là **giả định**; kết luận không nhạy vì kể cả weight=10
thì 1.200 × 5 = 6.000 vẫn chỉ **vừa chạm** trần 6.000/phút của spot.)*

**Kết luận phản trực giác nhưng đúng: rate limit của Binance KHÔNG phải điểm vỡ.** Điểm vỡ là chỗ khác:

#### Điểm vỡ thật #1 — vòng lặp tuần tự có LLM ở giữa

`MarketScanService.cs:165` là `foreach` **tuần tự, await từng symbol**. Mỗi vòng gồm:
1 lời gọi HTTP klines (~0,2 s) + **1 lời gọi LLM** (~3–8 s, ước lượng; timeout cấu hình 30 s,
`LlmOptions.cs:10`) + có thể 1 lời gọi repair (`:268`) + có thể 1 lời gọi preflight (`:189`) + I/O DB.

| Số symbol | Thời gian một lượt quét (ước lượng, 4–10 s/symbol) | Cron `*/5` = 300 s |
|---:|---:|---|
| 5 | 20–50 s | ✅ vừa |
| 30 | 2–5 phút | ⚠️ **chạm trần** |
| 75 | 5–12,5 phút | ❌ **vượt** |
| 300 | 20–50 phút | ❌❌ vượt 4–10 lần |

**Ngưỡng vỡ ≈ 30–75 symbol trên một instance (ước lượng).** Không phải 1.000 user — mà là **khoảng 3–7 user**
nếu mỗi người có watchlist 10 symbol phân biệt.

#### Điểm vỡ thật #2 — không có `DisableConcurrentExecution`, và nó cộng hưởng với #1

`grep DisableConcurrentExecution src` → **rỗng**. `AddHangfireServer()` (`Program.cs:53`) không truyền
`WorkerCount` → dùng mặc định của Hangfire (bội số CPU; **tôi không xác minh được con số chính xác** trong lượt này).

Khi lượt quét N chưa xong mà cron đã đến lượt N+1, Hangfire **enqueue tiếp và chạy song song trên worker khác**.
Cộng thêm `BackgroundJob.Enqueue` lúc khởi động (`Program.cs:130`) và nút `ScanNow` cho user bấm tuỳ ý.
Ba hệ quả, xếp theo mức nghiêm trọng:

1. **Nhân chi phí LLM theo số lượt chồng lấn.** Nếu 4 lượt chạy song song, hoá đơn ×4 mà không ai biết.
2. **TOCTOU vi phạm SC-005.** Chống trùng dựa trên đọc DB (`MarketScanService.cs:477-486` và
   `LiveOrderService.cs:101-108`) — hai lượt song song cùng đọc trước khi bên nào ghi.
   `clientOrderId = mmw-{tradeId}` **khác nhau** giữa hai trade khác nhau nên Binance **không** chặn.
3. **Burst request tới Binance nhân lên** → tiến gần trần 6.000 weight/phút nhanh hơn nhiều so với bảng trên.

**Sửa: 0,5 ngày-người.** Thêm `[DisableConcurrentExecution(timeoutSeconds)]` lên 5 job + khoá `ScanNow`.
Đây là **tỷ lệ giá trị / công sức cao nhất trong toàn bộ tài liệu này.**

#### Điểm vỡ thật #3 — scale-out phá cache và phá SignalR

Ngay khi chạy 2 instance:
- `IMemoryCache` là **per-process** → số lời gọi Binance ×N instance, và tính theo IP thì N instance sau
  một NAT/load balancer vẫn **chung một IP** ở nhiều cấu hình → nhân rủi ro ban.
- SignalR mặc định **in-memory backplane** (`Program.cs:81`) → thông báo chỉ tới user đang giữ kết nối trên
  đúng instance đó. Cần Redis backplane hoặc Azure SignalR.
- `SeedData.InitializeAsync` gọi `db.Database.MigrateAsync()` (`SeedData.cs:23`) **ở mọi instance khi khởi động**
  → race condition khi deploy nhiều instance cùng lúc.

### 2.3 Cache tầng chung cho market data — thiết kế cụ thể

Nền tảng **đã có** (`IMemoryCache`, TTL 15 s). Cần nâng cấp thành 3 tầng:

```
Tầng 0 — In-process (đang có, giữ)
  Khoá: klines:{symbol}:{interval}:{limit}      TTL 15 s
  Vấn đề: (a) chết khi restart, (b) không dùng chung giữa instance,
          (c) khoá gồm `limit` ⇒ MarketScan (200) và Advisor (100) gọi Binance HAI lần cho cùng symbol.
  → Sửa nhỏ, giá trị ngay: luôn lấy limit=200 rồi TakeLast(n) trong bộ nhớ. 0,25 ngày-người, −50% call.

Tầng 1 — Distributed cache (khi có instance thứ hai)
  Redis. Cùng khoá. TTL theo interval, KHÔNG theo đồng hồ:
  TTL = thời gian còn lại tới lần đóng nến kế tiếp (1h → tối đa 3.600 s).
  → Giảm số lời gọi klines từ 288/ngày/symbol xuống 24/ngày/symbol cho khung 1h. −91,7%.

Tầng 2 — Bảng `SymbolVerdict` bền vững trong DB  ← phần quan trọng nhất, xem 2.4
  Khoá: (Symbol, Interval, LastClosedCandleOpenTime, PromptVersion)
  Giá trị: verdict của AI ở cấp SYMBOL (không có dữ liệu user)
  → Chi phí AI chuyển từ O(user × symbol × 288) sang O(symbol × số nến đóng).
```

Thêm một thành phần bắt buộc cho multi-tenant, chưa tồn tại:

**Global token bucket cho Binance**, đặt trong Infrastructure, chia sẻ qua Redis:
- Ngân sách: 6.000 weight/phút (spot) và 2.400 weight/phút (futures) — số thật, mục 2.1.
- Giữ lại **≥30% headroom** cho đường đặt lệnh (đường tiền thật **không bao giờ** được xếp hàng sau job quét).
- Chia phần công bằng theo tenant; tenant vượt hạn mức thì **job của tenant đó** chậm lại, không phải cả nền tảng.
- Đọc header `X-MBX-USED-WEIGHT-1M` mà Binance trả về mỗi request [Binance Developer Docs, truy cập 30/07/2026]
  để hiệu chỉnh thay vì đếm mù.
- Polly: circuit breaker mở khi gặp 418, backoff luỹ thừa khi 429, **không retry 4xx** (hiện `RetryAsync`
  retry mù mọi lỗi, `LiveOrderService.cs:531-546`).

**Ước lượng: 3–5 ngày-người.** Không cần cho 1 user; **bắt buộc** trước user thứ hai.

### 2.4 Kiến trúc AI hai tầng — ràng buộc thiết kế số một

#### 2.4.1 Chẩn đoán gốc — chính xác hơn file 03

File 03 §5.6 chỉ đúng một nửa: nói rằng payload nhúng dữ liệu user (`riskPolicy`, `account` ở
`MarketScanService.cs:363-381`) nên không chia sẻ được. Đúng. Nhưng **nguyên nhân lớn hơn nằm ở tần suất, và
nó là một lỗi về tính đúng đắn chứ không phải về chi phí**:

> **Quét 288 lần/ngày trên khung 1h nghĩa là 11/12 lần gửi cho LLM một bộ chỉ báo KHÔNG ĐỔI.**
> `MarketAnalyzer` tính EMA/RSI/MACD/ATR trên 200 nến (`MarketScanService.cs:22, 177`). Giữa hai lần
> đóng nến, mọi giá trị dựa trên nến đã đóng là **y hệt**. Chỉ có nến đang chạy thay đổi.
> Trả tiền 12 lần cho cùng một câu hỏi không phải "chưa tối ưu" — đó là **lỗi thiết kế**.

Và nó còn tệ hơn thế: hệ thống lưu **một `AiSignalScanRecord` cho MỖI lượt** kèm prompt + payload + phản hồi thô
(`MarketScanService.cs:238-239, 259, 262`). Nên 11/12 bản ghi audit cũng là rác — đây là nguyên nhân thật của
trần 10 GB SQL Express mà file 03 §5.5 cảnh báo.

#### 2.4.2 Thiết kế: tách tầng SYMBOL (chia sẻ) khỏi tầng USER (deterministic)

**Tầng 1 — Symbol Verdict. Chạy 1 lần cho toàn hệ thống, mỗi lần đóng nến.**

- Đầu vào: **đúng bằng `BuildCompactAiSignalPayload`** (`MarketScanService.cs:434-463`) + 24 nến gần nhất.
  Không `account`, không `riskPolicy` — payload này **đã tồn tại trong mã và đang nằm không dùng**
  (chỉ dùng ở nhánh repair, `:267`).
- Đầu ra: hướng (long/short/wait), score, confidence, **vùng entry/SL/TP tuyệt đối**, invalidation, warnings.
  Tất cả đều là **thuộc tính của thị trường**, không phải của người dùng. Đây là điểm mấu chốt: giá SL không
  phụ thuộc vào việc tài khoản có $2.000 hay $50.000 — chỉ **khối lượng** mới phụ thuộc.
- Lưu vào bảng mới `SymbolVerdict` khoá `(Symbol, Interval, LastClosedCandleOpenTime, PromptVersion)`.
- Trigger: **theo nến đóng**, không theo cron. Cron `*/5` chỉ để *kiểm tra đã có nến mới chưa*.

**Tầng 2 — User Fit. Thuần deterministic, KHÔNG gọi AI. Chi phí $0.**

Toàn bộ phần user-specific **đã tồn tại và đã deterministic**:

| Bước | Mã hiện có | Cần AI không? |
|---|---|---|
| Kiểm chứng đúng phía giá theo hướng | `MarketScanService.cs:644-654` | ❌ Không |
| RR ≥ ngưỡng tài khoản | `MarketScanService.cs:665-669` | ❌ Không |
| Khoảng cách rủi ro > 0 | `MarketScanService.cs:656-662` | ❌ Không |
| Tính khối lượng theo % rủi ro | `MarketScanService.cs:563-569` | ❌ Không |
| Chấm 5 rule kỷ luật | `TradeRuleEngine` | ❌ Không |
| 3 behavior detector | `Behavior/Detectors/*` | ❌ Không |
| 18 lớp chặn live order | `LiveOrderService.cs:56-344` | ❌ Không |

> **Đây là hệ quả trực tiếp của Nguyên tắc II ("Deterministic trước, AI sau") mà tác giả đã cài đặt đúng —
> chỉ là chưa nhận ra hệ quả KINH TẾ của nó: nếu mọi thứ liên quan tới user đều deterministic, thì
> chi phí AI theo đầu user PHẢI bằng 0. Kiến trúc hiện tại đang trả tiền cho một thứ nó không cần.**

**Ngoại lệ có chủ đích cần giữ**: preflight vòng 2 trước khi đặt lệnh thật (`LiveOrderService.cs:90-94` yêu cầu
AI thật). Đây là chỗ **duy nhất** nên tiêu tiền cho model tốt — vì đây là chỗ duy nhất mà chất lượng câu trả lời
có hệ quả tiền thật. Tần suất: chỉ khi có tín hiệu thật vượt cửa, ~30–60 lượt/user/tháng (ước lượng).

#### 2.4.3 Xoá LLM khỏi job advisor — khuyến nghị mạnh nhất, và nó cải thiện tuân thủ hiến chương

`TradeAdvisorService` gọi `EnhanceWithLlmAsync` **cho từng lệnh, mỗi phút** (`:87-92`, `Program.cs:139-142`).
Chi phí: **216.000 lượt/tháng ở T=5** [file 03 §5.1] ≈ $45,32/user/tháng.

Điều LLM làm ở đó: **viết lại** một lời khuyên deterministic đã đầy đủ.
Và system prompt dặn nó *"Không lặp lại dữ liệu đã cung cấp"* (`TradeAdvisorService.cs:32`).

> **Đối chiếu với hiến chương**: Nguyên tắc I yêu cầu *"Mọi cảnh báo hướng tới người dùng PHẢI nêu số liệu
> thực tế so với ngưỡng đã cấu hình, không được nói chung chung"* (`constitution.md:47-49`).
> Nhánh deterministic `GenerateAdvice` (`TradeAdvisorService.cs:163-210`) **làm đúng điều đó** — nó nói
> *"Khoảng cách SL: 2,4%"*. Nhánh LLM được lệnh **không** nhắc lại số liệu.
> **Nghĩa là: MMW đang trả $45/tháng để làm cho cảnh báo của mình KÉM tuân thủ hiến chương hơn.**

**Khuyến nghị: xoá `EnhanceWithLlmAsync` khỏi job định kỳ hoàn toàn.** Chuyển thành nút bấm
"Hỏi AI về lệnh này" trên trang chi tiết. Chi phí: **0,5–1 ngày-người**. Giảm: **−100%** của 216.000 lượt.

*(Cảnh báo trung thực: nếu tác giả thấy lời khuyên LLM thực sự có giá trị chủ quan, hãy giữ nó nhưng đổi
trigger sang sự kiện — `RiskLevel` đổi bậc (`TradeAdvisorService.cs:151-161` đã tính sẵn), hoặc giá vượt
ngưỡng band quanh SL/TP — kèm debounce 15 phút. Vẫn giảm ~95–98%.)*

#### 2.4.4 Định lượng từng biện pháp

Neo vào cấu hình cá nhân của file 03 §6.4-A (W=5 symbol khung 1h, T=2 lệnh mở, Tier B DeepSeek):
**135.216 lượt/tháng, $60,17/tháng.**

| # | Biện pháp | Ngày-người | Cơ chế | Giảm lượt gọi |
|---|---|---:|---|---|
| **R0** | **Ghi `usage` token vào `AiSignalScanRecord`** | **0,5** | `MiniMaxLlmService.cs:77-85` (và 2 adapter còn lại) **không đọc trường `usage`** của phản hồi → hiện **không thể** đo chi phí thật. Thêm `PromptTokens`/`CompletionTokens` vào `ILlmService` + cột DB. | 0% *(nhưng đây là điều kiện để kiểm chứng mọi con số còn lại — làm TRƯỚC)* |
| **R1** | **Gate theo nến đóng** | 1–2 | Chỉ quét khi có nến mới đóng. Khung 1h: 24 thay vì 288. | **−91,7%** |
| **R2** | **Tách 2 tầng + bảng `SymbolVerdict`** | 3–5 | Tầng 1 chia sẻ toàn hệ thống; tầng 2 deterministic. | Với 1 user: 0% thêm. Với N user: **chi phí scan/user → ~0** |
| **R3** | **Xoá LLM khỏi advisor định kỳ** | 0,5–1 | Xem 2.4.3 | **−100%** của 86.400 lượt/tháng (cá nhân) |
| **R4** | **`SignalGenerator` làm bộ lọc trước AI** | 1–2 | Mã chết có 5 test [file 02 §2 mục 20] → đưa lại luồng chính; chỉ gọi AI khi tín hiệu deterministic vượt ngưỡng. | **−40% đến −70%** phần scan còn lại |
| **R5** | **Cache phản hồi + dedup `limit`** | 0,5–1 | Gộp `limit=100`/`200` thành một; TTL theo nến. | −50% call Binance; −5–10% call AI |
| **R6** | **Dọn `AiSignalScanRecord`** | 1 | Giữ 90 ngày chi tiết, sau đó chỉ tóm tắt. | 0% *(giải quyết trần 10 GB SQL Express)* |
| | **TỔNG** | **7,5 – 12,5** | | |

**Kết quả cho cấu hình cá nhân (ước lượng):**

| Nguồn | Trước | Sau R1+R3+R4+R5 | Giảm |
|---|---:|---:|---|
| market-scan | 43.200 lượt/tháng | 3.600 → lọc R4 → **~1.400–2.200** | −95% đến −97% |
| repair (10%) | 4.320 | ~180 | −96% |
| preflight | 1.296 | ~30–60 | −95% |
| trade-advisor | 86.400 | **~30–60** (bấm tay) | **−99,9%** |
| **TỔNG** | **135.216** | **~1.650 – 2.500** | **−98,2% đến −98,8%** |
| **Chi phí Tier B** | **$60,17/tháng** | **≈ $1,5 – 2,5/tháng (ước lượng)** | **~24–40×** |

**Đối chiếu với file 03**: file 03 §6.4 ước lượng "sau refactor $3–6/tháng". Con số của tôi thấp hơn
(**$1,5–2,5**) vì tôi **xoá hẳn** LLM khỏi advisor thay vì chỉ chuyển sang event-driven. Cả hai đều đúng
về bậc độ lớn; khác biệt nằm ở một quyết định sản phẩm, không phải ở phép tính.

**Với 1.000 user (nếu có ngày đó)** — đây mới là chỗ R2 phát huy:

| | Kiến trúc hiện tại | Sau R1+R2+R3+R4 |
|---|---:|---:|
| Lượt gọi AI/ngày toàn nền tảng | 288 × 10 × 1.000 = **2.880.000** | 24 × ~300 symbol phân biệt = **7.200** |
| **Tỷ lệ giảm** | | **−99,75% (400×)** |
| Chi phí biên/user thêm vào | ~$130/tháng | **≈ $0** *(tầng 2 không gọi AI)* |
| Chi phí cố định nền tảng | — | **~$40–70/tháng (ước lượng)** cho **toàn bộ** user |

> **Điểm quan trọng nhất về mặt mô hình kinh doanh, và file 03 chưa nêu ở dạng này:**
> R2 không chỉ *giảm* chi phí — nó **đổi chiều của hàm chi phí** từ tuyến tính theo user sang
> **gần như phẳng**. Bảng "phá sản" ở file 03 §5.4 (biên gộp −993%) trở thành biên gộp **>90%**,
> vì chi phí biên thật sự của user thứ 1.001 là **chi phí DB + băng thông**, không phải LLM.
> Điều này **không** cứu bài toán B2C (SOM 21–148 khách vẫn nhỏ hơn 109–186 khách hoà vốn) — nhưng nó
> **cứu bài toán B2B**, nơi 1–4 khách là đủ.

### 2.5 Một cơ hội kiến trúc mà phân tích tài chính bỏ sót

R2 tạo ra một **hệ quả phụ có giá trị chiến lược**, và nó gần như miễn phí:

> Bảng `SymbolVerdict` là một **dòng dữ liệu công khai được, không chứa dữ liệu user, không chứa khoá API,
> không chứa vị thế** — chỉ gồm: *"tại nến 1h đóng lúc T, mô hình nói gì về BTCUSDT, và sau đó giá đi đâu"*.
> Job tầng 1 **phải chạy dù sao đi nữa** cho chính tác giả dùng. Nên chi phí biên để công bố nó là **≈ 0**.

Đây chính là thứ mà file 01 §7.1 và file 03 §3.3.6 nói MMW **thiếu và cần**: một **track record công khai,
liên tục, có dấu thời gian**. Và nó có ba tính chất hiếm:

1. **Không cần khách hàng** — nó chạy trên dữ liệu công khai.
2. **Không cần custody, không cần khoá API của ai** — nên nằm ở ô xanh của bảng rủi ro pháp lý file 01 §5.4.
3. **Nuôi cả ba đường mà file 03 khuyến nghị**: #5 (prop firm đọc trước khi tin), #9 (open-source), #6
   (viết public về kiến trúc).

**Cảnh báo bắt buộc, và nó nghiêm túc**: phải trình bày như một **nhật ký đánh giá mô hình**
(*"mô hình nói X, thực tế Y, tỷ lệ đúng Z"*), **tuyệt đối không** như tín hiệu giao dịch.
Tiền lệ 12/2025 SEC kiện 3 nền tảng + 4 investment club với chiêu bài *"tín hiệu do AI sinh"*
[FinanceFeeds dẫn Adam Tracy, 20/05/2026] là ranh giới cần tránh. Không kèm entry/SL/TP có thể hành động,
không kèm lời kêu gọi, có tuyên bố miễn trừ.

**Chi phí thêm: 1–2 ngày-người** (một trang tĩnh + job xuất). **Giá trị: đây là tài sản duy nhất trong toàn
bộ dự án có thể xây được mà không cần một khách hàng nào.**

---

## 3. BẢO MẬT — phần quan trọng nhất

### 3.1 Khoá API plaintext: nghiêm trọng đến mức nào?

Xác minh lại: `TradingAccount.cs:29-34` — `public string? ApiKey` / `ApiSecret`, `[MaxLength(200)]`,
không converter, không mã hoá. Comment ghi *"Lưu bằng User Secrets hoặc encrypted"* — mã không làm.

**Nhưng mức nghiêm trọng thật cao hơn cách mọi người thường mô tả**, vì một lập luận ít người nêu:

> **"Khoá không có quyền rút tiền" ≠ "không mất tiền được".**
> Một khoá có quyền TRADE futures cho phép kẻ tấn công **mở một vị thế lớn trên một perp thanh khoản mỏng
> ở giá xấu, và tự đứng phía đối ứng**. Tiền chuyển từ tài khoản nạn nhân sang tài khoản kẻ tấn công
> **qua sổ lệnh của sàn**, không qua bất kỳ lệnh rút nào. Quyền rút tiền là hoàn toàn không cần thiết.
> Đây là lý do khuyến nghị *"cấp quyền tối thiểu, không cấp quyền rút"* (`constitution.md:163-164`)
> là cần nhưng **rất xa mức đủ**.

Cộng thêm hai yếu tố khuếch đại **của riêng MMW**:

| Yếu tố | Vị trí | Hệ quả |
|---|---|---|
| Mật khẩu admin mặc định là `public const`, tự seed | `SeedData.cs:11-12, 28-37` | Ai đọc được mã nguồn (đã dự định open-source!) biết mật khẩu mặc định của mọi instance chưa đổi |
| Hangfire dashboard mở cho **mọi** user đã đăng nhập | `HangfireAuthorizationFilter.cs:11` | Từ dashboard trigger tay `market-scan` → đường tới auto-đặt lệnh thật |
| `AllowedHosts: "*"` | `appsettings.json` | Không chặn Host header |
| Không CSP, không SRI (A-04) | `_Layout.cshtml:22-24, 148-151` | **Đường tới tiền thật không cần khoá API** |
| Log trong `wwwroot`, phục vụ trước auth (A-03) | `Program.cs:18-19, 94, 106` | Rò rỉ không cần đăng nhập (Development) |

### 3.2 Threat model — 6 kịch bản, blast radius cụ thể

Giả định: SaaS multi-tenant, N user, mỗi user một cặp khoá futures TRADE.

| # | Kịch bản | Đường vào | Blast radius **hiện tại** | Blast radius **sau envelope + KMS** |
|---|---|---|---|---|
| **T1** | **DB bị dump** | Backup `.bak` bị lấy, snapshot storage cấu hình sai, firewall Azure SQL mở, dev copy DB prod về máy | **Toàn bộ N khoá, cleartext, dùng được ngay, vĩnh viễn** | Chỉ ciphertext + wrapped DEK. Vô dụng nếu không có quyền KMS. **Đây là kịch bản mà envelope encryption thắng tuyệt đối.** |
| **T2** | **RCE trên app server** | Deserialization, dependency bị chiếm (vd `AutoMapper 14.0.0` **GHSA-rvv3-g6hj-g44x** đang chưa vá, `MMW.Application.csproj:10`), file upload | Toàn bộ N khoá tức thì | Kẻ tấn công có quyền gọi KMS ⇒ vẫn lấy được, **nhưng phải giải mã từng khoá, để lại dấu vết trong KMS audit log, và bị chặn bởi rate limit KMS.** Từ "mất tất cả trong 1 giây, không ai biết" thành "mất dần, có báo động". |
| **T3** | **SQL injection / IDOR** | `HomeController.Index(long? accountId)` (`:34`), `TradesController.CancelOpenOrder(long accountId, ...)` (`:105`) nhận `accountId` từ request và **dùng khoá của tài khoản đó để huỷ lệnh trên sàn** [file 02 §6.1] | User A huỷ lệnh / xem tài khoản của user B | **Không đổi** — mã hoá không sửa IDOR. Phải bịt riêng (A6). |
| **T4** | **Chuỗi cung ứng frontend** (A-04) | 4 asset từ `cdn.jsdelivr.net`, không SRI | Script chạy trong phiên đã đăng nhập ⇒ đọc antiforgery token trong DOM ⇒ POST tạo/đặt lệnh. **Không cần khoá API.** | **Không đổi.** Sửa bằng self-host asset + CSP: **0,5 ngày-người**. |
| **T5** | **Insider / chính tác giả** | Một người, vừa là admin DB, vừa là admin app, vừa là admin KMS | Toàn quyền | **Không đổi được về mặt kỹ thuật.** Solo dev **không thể** có separation of duties. → Đây là lập luận quyết định cho mục 3.6. |
| **T6** | **Log / audit rò khoá** | — | ✅ **Đã làm đúng**: `BinanceFuturesOrderProvider.cs:518-534` redact `signature` → `***redacted***`, API key → `MaskKey` (4 đầu + 4 cuối) | Giữ nguyên |

**Đọc bảng này ra kết luận**: envelope encryption giải quyết **T1 hoàn toàn** và **T2 một phần**.
Nó **không** giải quyết T3, T4, T5. Nghĩa là **mã hoá khoá là điều kiện cần, không phải điều kiện đủ**,
và bất kỳ ai nói "mã hoá xong là an toàn nhận khoá người lạ" là sai.

### 3.3 Thiết kế envelope encryption — cụ thể, kèm một chỗ file 02 khuyến nghị sai

**Kiến trúc:**

```
KEK (Key Encryption Key)   ← Azure Key Vault / AWS KMS / (self-host: DPAPI hoặc khoá trong biến môi trường)
   │  wrap/unwrap
   ▼
DEK per tenant (AES-256-GCM, 32 byte, app sinh)
   │  encrypt/decrypt
   ▼
ApiKeyCipher / ApiSecretCipher  (varbinary)  +  Nonce (12B)  +  Tag (16B)
                                             +  DekId, KeyVersion
                                             +  ApiKeyLast4 (plaintext, để hiển thị UI)
```

**Chỗ file 02 §6.2 (A5) và file 03 §7.1 khuyến nghị SAI, cần đính chính:**

> Cả hai đề xuất **"EF `ValueConverter` + DPAPI/Key Vault"**.
> **`ValueConverter` không dùng được với KMS.** Converter chạy **đồng bộ** bên trong `SaveChanges` và
> quá trình materialization; nó không thể `await` một lời gọi mạng tới Key Vault, không thể xử lý lỗi
> mạng, không thể ghi audit, và làm **mọi** truy vấn nạp `TradingAccount` đều phải giải mã kể cả khi
> chỉ cần đọc `Name`.
>
> **Thiết kế đúng**: một service `ISecretProtector` (Application port, Infrastructure adapter) được gọi
> **tường minh** ở **đúng 6 chỗ** cần khoá — và may mắn là cả 6 chỗ đều grep ra được:
> `LiveOrderService.cs:97, 357, 392, 490` · `LiveBalanceService.cs:38` ·
> `TradeResultSyncService.cs:134` · `TradesController.cs:122, 149, 458`.
> `ValueConverter` **vẫn dùng được** cho biến thể self-host đơn giản (khoá đối xứng từ biến môi trường,
> không có KMS) — và với 1 user thì đó là lựa chọn đúng.

**Kèm theo, bắt buộc:**

- **Audit mỗi lần giải mã**: bảng `ExchangeApiAuditRecord` **đã tồn tại** — thêm loại bản ghi `SecretAccess`
  với lý do (`tradeId`, `job`, `userId`). Đúng Nguyên tắc IV. Chi phí gần 0 vì hạ tầng đã có.
- **Rotation**: cột `KeyVersion` + job re-wrap nền. Rotation KEK không cần chạm ciphertext (chỉ re-wrap DEK).
  Rotation *khoá sàn* thì phải do user làm trên Binance — MMW chỉ có thể **nhắc** và **kiểm tra tuổi khoá**.
- **Kiểm tra quyền khoá lúc onboarding**: gọi thử một endpoint cần quyền rút; nếu thành công ⇒ **từ chối khoá**
  và bắt user cấp lại. Đây là biện pháp rẻ nhất và hiệu quả nhất trong toàn mục này.

**Ước lượng công sức (ước lượng của tôi, khác file 02):**

| Phạm vi | Ngày-người | Ghi chú |
|---|---:|---|
| Self-host 1 user: AES-GCM + khoá từ User Secrets/biến môi trường + migration + 6 call site | **3–4** | Bắt được ~80% giá trị (T1). File 02 ghi 3–5 — **đồng ý** |
| Multi-tenant: DEK/tenant + KMS + audit truy cập + rotation + kiểm tra quyền khoá | **6–9** | File 02 ghi 3–5 — **tôi cho là thiếu 2–4 ngày-người** |
| Bịt IDOR (T3) | 3–4 | Đồng ý với A6 |
| Self-host CDN asset + CSP + `X-Frame-Options` + chuyển log ra khỏi `wwwroot` (T4, A-03) | **0,5** | **Chưa có trong bất kỳ ước lượng nào của file 02/03** |

### 3.4 Trách nhiệm pháp lý và uy tín — nói ngắn, nói thẳng

Tôi không phải luật sư và không đưa tư vấn pháp lý. Ba quan sát kỹ thuật có hệ quả pháp lý:

1. **MMW nằm ở ô đỏ nhất của bảng file 01 §5.4** — *"SaaS, tác giả cầm khoá, AI sinh đề xuất futures +
   tự đặt lệnh"*: Cao ở VN, Cao ở US (CTA), Cao ở EU (CASP). Không có ô nào tệ hơn trong bảng đó.
2. **9/18 lớp chặn không có test chứng minh chúng chặn** [file 02 §4.2]. Trong một hệ thống chạm tiền,
   "chúng tôi có lớp bảo vệ nhưng không kiểm chứng được nó hoạt động" là mô tả sách giáo khoa của
   *sơ suất*. Phần lớn tài phán **không** cho phép điều khoản miễn trừ loại trừ trách nhiệm với sơ suất nghiêm trọng.
   *(Nhận định chung, cần luật sư xác nhận.)*
3. **Tác giả là cá nhân, không phải pháp nhân.** Không có màn chắn trách nhiệm hữu hạn, không có bảo hiểm
   E&O. Một bug ở lớp chặn không test làm cháy tài khoản của một khách hàng trả $19/tháng tạo ra khoản nợ
   lớn hơn nhiều lần doanh thu [file 03 §1.7].

**Hệ quả kiến trúc, không phải hệ quả pháp lý**: **không nhận khoá của người thứ hai cho tới khi 18/18 lớp
chặn có test + khoá được mã hoá + A-01 và A-02 được sửa.** Trùng khớp F8 của file 03 và D2 của file 01.

### 3.5 Kiến trúc thay thế — CÓ THỂ không giữ khoá không?

Đây là câu hỏi quan trọng nhất của cả tài liệu. Câu trả lời: **có, và đó là lựa chọn đúng.**

Nhưng trước hết phải nói rõ ràng buộc không thể vượt qua:

> **Giá trị lõi của MMW là CHẶN lệnh trước khi nó chạm sàn.**
> Muốn chặn thì phải **nằm trên đường đi**. Nằm trên đường đi thì phải **có quyền đặt lệnh**.
> ⇒ **Không có phương án nào vừa "chặn được" vừa "không ai cầm khoá".**
> Câu hỏi thật là: **AI cầm khoá?** — tác giả, hay user, hay một pháp nhân đã cầm sẵn.

| # | Phương án | Ai giữ khoá | Chặn được? | Rủi ro pháp lý | Tác động mô hình KD | Công sức thêm |
|---|---|---|---|---|---|---:|
| **A** | **Read-only, chỉ cảnh báo** | Tác giả, nhưng khoá **không có quyền TRADE** | ❌ **Không** — chỉ cảnh báo sau khi lệnh đã vào | Thấp (bằng TiltGuard) | **Mất USP duy nhất** [file 01 §6.3 xếp "quyền thực thi ở tầng execution" là moat #1] → tụt xuống cạnh tranh với TMM $6 và CMM $0 | 2–4 nd (bỏ đường đặt lệnh) |
| **B** | **Self-host / desktop agent** ⭐ | **User giữ, tác giả KHÔNG BAO GIỜ chạm** | ✅ **Có, đầy đủ** | **Gần 0** — file 01 §5.4 xếp ⚠️"chấp nhận được" | Không có SaaS. Doanh thu: license self-host / sponsor / open-source. **Khớp #9 của file 03** | **8–14 nd** *(xem dưới)* |
| **C** | **Sub-account Binance, vốn giới hạn** | Tác giả, nhưng khoá chỉ chạm được sub-account có $X | ✅ Có | Trung bình — vẫn là custody | Blast radius bị **giới hạn cứng bằng $X**. Đây là biện pháp giảm thiểu tốt **nếu** buộc phải làm SaaS | 2–3 nd + onboarding phức tạp hơn nhiều |
| **D** | **OAuth-style delegation** | Sàn giữ; MMW giữ token có scope | ✅ Có | Thấp hơn C | **Tôi KHÔNG xác minh được** Binance có cung cấp delegation kiểu này cho user thường (khác với chương trình broker/link dành cho tổ chức). **Không được giả định là có.** Kể cả nếu có, token vẫn phải lưu và mã hoá — lợi ích thật là **thu hồi được + giới hạn scope**, không phải "không giữ gì" | không xác định |
| **E** | **B2B: prop firm giữ khoá, MMW là engine** ⭐ | **Prop firm** (đã cầm sẵn rồi) | ✅ Có, qua API của firm | **Thấp nhất trong nhóm có doanh thu** [file 03 §3.3.5 chấm PL 7/10] | **Khớp #5 của file 03 — phương án duy nhất toán học đóng được** | 35–55 nd [file 03] |

**Phân tích phương án B (self-host) — con số thật, không lạc quan:**

MMW đã là một ứng dụng ASP.NET Core tự chứa. Nhưng để phát hành được cho người khác tự chạy:

| Việc | Ngày-người | Vì sao tốn |
|---|---:|---|
| Thoát SQL Server → SQLite/PostgreSQL | **3–5** | **27 migration EF là provider-specific.** Với 1 user và không có dữ liệu production cần bảo tồn ở đâu khác, giải pháp đúng là **squash 27 migration thành 1 baseline** — việc này còn **xoá luôn 16.710 dòng mã sinh tự động** [file 02 §1.1]. |
| Docker Compose + first-run setup wizard | 2–3 | Thay `SeedData` hardcode `Admin@123` (`SeedData.cs:11-12`) bằng bắt buộc đặt mật khẩu lần đầu |
| Mã hoá khoá (bản self-host) | 3–4 | Mục 3.3 |
| Tài liệu cài đặt + hướng dẫn tạo khoá đúng quyền | 1–2 | |
| **Tổng** | **9–14** | |

**So sánh với 82,5–124,5 ngày-người của đường SaaS** [file 02 §6.3]: đường self-host rẻ hơn **6–13 lần**,
đưa rủi ro pháp lý về gần 0, xoá sạch rủi ro trách nhiệm, và **giữ nguyên 100% giá trị sản phẩm** —
vì user tự chạy vẫn được chặn lệnh đầy đủ.

> **Phán quyết mục 3**: *Kiến trúc dominant là **B cho phổ thông + E cho doanh thu**.
> Tác giả không nên cầm khoá đặt lệnh của người lạ trong 18 tháng tới, và điều đó **không phải là hạn chế**
> — nó là lựa chọn giữ được cả sản phẩm lẫn sự nghiệp.*

---

## 4. Multi-tenant — lộ trình và kiểm chứng ước lượng

### 4.1 Mô hình cách ly: chọn row-level, và lý do là ràng buộc vận hành

| Mô hình | Đánh giá cho MMW | Kết luận |
|---|---|---|
| **Row-level** (`TenantId` + global query filter) | Một DB, một bộ migration, một Hangfire storage | ✅ **Chọn** |
| **Schema-per-tenant** | **27 migration × N schema** = địa ngục migration cho một dev đơn lẻ | ❌ |
| **DB-per-tenant** | Chi phí SQL Server × N; 5 job phải mở N connection; backup × N | ❌ |

Nhưng row-level chỉ an toàn nếu có **ba lớp**, không phải một:

1. **Global query filter** trong `MmwDbContext` — `grep "HasQueryFilter"` hiện **rỗng**. Đây là lớp mặc định.
2. **Predicate `TenantId` tường minh trong mọi truy vấn của job** — vì job **không có HTTP context**
   (xem 4.2 dưới). Filter một mình sẽ khiến job **im lặng không làm gì**.
3. **Test tích hợp khẳng định đọc chéo tenant trả về rỗng.** Không có lớp 3 thì lớp 1 là **lời hứa,
   không phải kiểm soát**. Đây là điều kiện tôi coi là bắt buộc, không phải tuỳ chọn.

### 4.2 Kiểm chứng ước lượng 82,5–124,5 ngày-người của file 02 — tôi KHÔNG hoàn toàn đồng ý

| Hạng mục file 02 | File 02 | **Của tôi** | Lý do khác biệt |
|---|---:|---:|---|
| A1 — `UserId` cho 14 entity + migration + backfill | 4–6 | **3–5** | **Thấp hơn.** Với đúng 1 user, backfill là `UPDATE ... SET TenantId = 1`. Cơ học thuần. |
| A2 — `ICurrentUser` + global query filter | 3–4 | **6–9** | ⚠️ **Cao hơn nhiều.** Đây là hạng mục file 02 đánh giá thấp nhất. **Vấn đề: 5 Hangfire job chạy KHÔNG có HTTP context** (`TradeAdvisorService.cs:54`, `TradeResultSyncService.cs:40`, `MarketScanService.cs:159`). Nếu `ICurrentUser` trả null thì global filter loại **toàn bộ** hàng ⇒ job **im lặng không làm gì**, không lỗi, không log. Cần **hai chế độ context** (`IgnoreQueryFilters` + vòng lặp scoping theo tenant) và phải viết lại vòng lặp của cả 5 job. **Chế độ hỏng là rò rỉ dữ liệu im lặng hoặc no-op im lặng** — hạng mục rủi ro nhất trong toàn bộ bảng. |
| A3 — `AppSetting` per-user | 2–3 | 2–3 | Đồng ý. Lưu ý: `AllowOverrideRisk` toàn cục (`AppSetting.cs:22`) là lỗi **an toàn**, không chỉ lỗi multi-tenant — một user bật thì **nới lớp chặn cho tất cả**. |
| A4 — job phân mảnh theo user + `DisableConcurrentExecution` | 5–7 | **3–5** | ✅ **Thấp hơn — nhưng CHỈ NẾU làm R2 trước.** Sau khi tách 2 tầng (mục 2.4.2), tầng 1 **không có tenant nào cả**, tầng 2 là vòng lặp deterministic rẻ. **Thứ tự làm thay đổi chi phí.** Nếu làm A4 trước R2 thì 5–7 là đúng. |
| A5 — mã hoá khoá | 3–5 | **6–9** | Cao hơn: DEK/tenant + KMS + audit + rotation + kiểm tra quyền khoá (mục 3.3) |
| A6 — bịt IDOR | 3–4 | 3–4 | Đồng ý |
| A7 — phân quyền Hangfire dashboard | 0,5 | 0,5 | Đồng ý |
| A8 — bỏ seed admin, đổi mật khẩu lần đầu, khoá tài khoản | 2–3 | 2–3 | Đồng ý |
| A9 — sửa C-01 + C-03 | 3–4 | **5–7** | Cao hơn: C-01 phải sửa **hợp đồng port** (mục 1.2), không phải 2 dòng. Cộng A-02 (spot/perp) 1,5–2,5. |
| **— Thiếu —** `A10` **Đo & chặn cứng chi phí LLM theo tenant** | — | **2–3** | **Không có trong file 02 phase A.** Không có nó, một user có thể làm phá sản người vận hành. Đây là hạng mục **an toàn**, phải ở phase A không phải phase B. |
| **— Thiếu —** `A11` **Ngân sách rate-limit Binance chung + Polly** | — | **3–5** | **Shared-fate**: giới hạn tính theo IP không theo khoá [Binance Docs, 30/07/2026]. Một tenant làm ban cả nền tảng 2 phút → 3 ngày. |
| **— Thiếu —** `A12` **Xoá/xuất dữ liệu theo tenant** | — | **2–3** | Nghĩa vụ cơ bản khi giữ dữ liệu tài chính người khác |
| **— Thiếu —** `A13` **CSP + SRI + log ra khỏi wwwroot** (T4, A-03) | — | **0,5** | |
| **CỘNG PHA A** | **25,5 – 36,5** | **38,5 – 56** | **+51% đến +53%** |

**Phase B (thu tiền) — 32–49**: **đồng ý**, không sửa. *(Nếu chỉ làm B2B thì bỏ được ~60% của phase B
như file 03 §3.3.5 đã tính.)*

**Phase C (vận hành) — 25–39 → của tôi 28–44**: nhích lên vì C2 (rate-limit) đã bị tôi kéo lên phase A nhưng
thêm vào đó là backplane SignalR (1–2) và tách migration khỏi startup (0,5).

### 4.3 Tổng kết đối chiếu

| | File 02 | **Của tôi** | Chênh |
|---|---:|---:|---:|
| A — mở an toàn cho user thứ hai | 25,5 – 36,5 | **38,5 – 56** | +51% |
| B — thu được tiền | 32 – 49 | 32 – 49 | 0 |
| C — vận hành được ở quy mô | 25 – 39 | 28 – 44 | +12% |
| **TỔNG** | **82,5 – 124,5** | **98,5 – 149** | **+19%** |

Ở nhịp **2,19 ngày-người/tuần** [file 03 §6.1]: **45–68 tuần** = **10,5–16 tháng build thuần**,
zero doanh thu trong suốt quãng đó.

> **Kết luận về mục 4**: tôi **đồng ý về hướng** với file 02 và **không đồng ý về độ lớn** — ước lượng của
> file 02 **lạc quan khoảng 15–20%**, và sai lệch tập trung ở đúng chỗ nguy hiểm nhất (A2 — query filter
> gặp background job) cùng ba hạng mục an toàn bị bỏ sót (A10, A11, A13).
> Điều này **củng cố** kết luận tài chính của file 03 §7 (F1: không B2C), chứ không lật nó.

**Thứ tự làm bắt buộc, nếu có ngày phải làm:**

```
R1–R6 (chi phí AI)  →  A5 (mã hoá)  →  A9 (venue+timezone+spot/perp)  →  test 9 lớp chặn
     →  A11 (rate budget)  →  A10 (quota LLM)  →  A1  →  A2  →  A3  →  A4  →  A6–A8, A12–A13
```

Lý do thứ tự: **R2 trước A4 làm A4 rẻ đi 2 ngày-người** (mục 4.2). **A9 trước A1** vì multi-tenant hoá một
cơ chế kỷ luật đang tính sai ngày và sai thị trường chỉ là nhân rộng cái sai — đúng như file 02 §6.2 ghi chú A9.

---

## 5. Blockchain / Smart contract — phản biện thẳng thắn

**Mặc định là KHÔNG.** Kết quả: **4/4 KHÔNG.** Dưới đây là lý do kỹ thuật cho từng cái.

### 5.1 On-chain proof of track record → **KHÔNG**

File 03 §4.6 kết luận: *"Nó cần một chữ ký và một hash"*. **Tôi đồng ý, và tôi muốn củng cố lập luận đó
bằng một điểm mạnh hơn mà file 03 chưa nêu:**

> **Neo hash lên chain chứng minh MMW KHÔNG CHỐI ĐƯỢC một bản ghi mà chính MMW tạo ra.
> Nó KHÔNG chứng minh bản ghi đó ĐÚNG.**
>
> Dữ liệu cần chứng minh (`Flag`, `Trade`, `AiSignalScanRecord`, `ExchangeApiAuditRecord`) đều do MMW
> **tự quan sát và tự ghi**. Bên xác minh vẫn phải tin rằng MMW quan sát Binance đúng.
> **Neo tin cậy không hề dịch chuyển.** Chain chỉ thêm tính chống-kiểm-duyệt cho dấu thời gian —
> một tính chất mà **không ai trong kịch bản này cần**.

Phương án off-chain đạt đúng cùng kết quả:

| Thành phần | Cơ chế | Chi phí |
|---|---|---:|
| Toàn vẹn bản ghi | Merkle tree trên tập `Flag` + `Trade` theo kỳ | có sẵn |
| Không chối bỏ | Chữ ký Ed25519 của MMW trên Merkle root | ~0,5 nd |
| Dấu thời gian tin cậy | RFC 3161 TSA (miễn phí, nhiều nhà cung cấp công) | ~0,5 nd |
| Chống ghi lại lịch sử | Transparency log append-only (kiểu CT log; đơn giản nhất: repo Git công khai có commit ký) | ~1–2 nd |
| **Tổng** | | **2–4 ngày-người** |

Cái duy nhất blockchain thêm vào: một ví cần bảo vệ, gas cần trả, một chain cần tin, và một bề mặt pháp lý mới.

**Sắc thái duy nhất tôi bổ sung so với file 03**: nếu mục tiêu là **hồ sơ kỷ luật CÓ THỂ MANG THEO giữa
nhiều prop firm** thì một sổ append-only **dùng chung** thật sự có giá trị. Nhưng *sổ dùng chung* ≠ *blockchain*.
Một transparency log được host, hay thậm chí một bucket công khai chỉ-ghi-thêm, làm được. **Vẫn KHÔNG.**

### 5.2 Non-custodial copy-trading qua smart contract → **KHÔNG, dứt khoát**

Ba lý do **kỹ thuật** (bỏ qua phần pháp lý mà file 03 §3.3.3 đã loại bỏ hoàn toàn):

1. **Smart contract không thực thi được lệnh trên CEX.** MMW giao dịch Binance USDT-M perp. Không có
   cầu nối tin cậy nào cho phép một contract đặt lệnh trên một sàn tập trung. Contract chỉ có thể làm phần
   **kế toán phí và escrow** — tức là **phần dễ**. Phần khó (thực thi, khớp lệnh, đo hiệu suất) vẫn off-chain
   và vẫn cần tin MMW. Contract **không thêm bất kỳ đảm bảo nào**.
2. **Nếu chuyển sang DEX perp để non-custodial thật thì đó là một sản phẩm khác** — thanh khoản khác, phí khác,
   funding khác, và toàn bộ know-how Binance (Hedge Mode, `positionSide`, snap `stepSize`) trở nên vô dụng.
3. **Dữ liệu để chứng minh track record hiện đang SAI**: `TradeResultSyncService.cs:134` +
   `TradesController.cs:458` hardcode `useTestnet: false` trong khi mặc định là testnet, cộng với rò rỉ port
   ở mục 1.2. Copy-trading cần track record; MMW chưa có track record đáng tin.

### 5.3 DEX perp integration (Hyperliquid / GMX / dYdX) → **KHÔNG trong 18 tháng**, nhưng đây là mục đáng bàn nhất

Đây là mục duy nhất có một luận điểm ủng hộ nghiêm túc. Tôi trình bày cả hai phía.

**Ủng hộ:**
- File 01 §5.1: sau mốc 6 tháng kể từ khi sàn nội địa đầu tiên được cấp phép, nhà đầu tư trong nước giao dịch
  ngoài tổ chức được cấp phép *"tùy theo tính chất, mức độ vi phạm sẽ bị xử lý vi phạm hành chính hoặc truy cứu
  trách nhiệm hình sự"* [Dentons LuatViet, 22/10/2025, dẫn NQ05/2025/NQ-CP]; dự thảo nêu đích danh
  **Binance, OKX, Bybit** [sanvietnam.com, 06/06/2026].
- DEX perp giải quyết **trọn vẹn** bài toán custody ở mục 3: user ký bằng khoá của chính mình, MMW không bao
  giờ giữ secret. Ở phiên bản mạnh nhất, MMW chỉ đơn giản **từ chối ký** — mà đó *chính là* sản phẩm.
- Port `IExchangeOrderProvider` đã tồn tại và đủ sạch để cắm adapter thứ hai.

**Phản đối — và mạnh hơn:**
1. **Nó KHÔNG giảm rủi ro pháp lý VN.** Ngôn ngữ của NQ05 là về giao dịch tài sản mã hoá **không qua tổ chức
   được Bộ Tài chính cấp phép**. Một DEX cũng không phải tổ chức được cấp phép. Nếu có gì, ranh giới còn
   **mờ hơn**, không rõ hơn. *(Nhận định về văn bản, cần luật sư xác nhận.)*
2. **Không tạo khác biệt cạnh tranh.** TMM đã hỗ trợ **Hyperliquid** trong danh sách 10 sàn
   [tradermake.money, truy cập 29/07/2026].
3. **Nó vứt bỏ đúng phần tài sản thật.** File 02 §7.1 định danh know-how có giá trị: thứ tự 18 lớp chặn,
   Hedge Mode vs One-way, `positionSide` bắt buộc, `reduceOnly` không hợp lệ ở Hedge, snap `stepSize` xuống
   cho qty và lên cho min-notional, mã lỗi −4061/−1106/−1111. **Toàn bộ là kiến thức Binance.**
   Một adapter DEX thay thế nó bằng một tập bài học đau thương **mới** mà tác giả chưa có.
4. **Chi phí**: quản lý khoá ví, ký EIP-712, quản lý nonce, websocket trạng thái lệnh, mô hình margin khác.
   **10–18 ngày-người (ước lượng)** — trên một codebase đang có 9/18 lớp chặn không test.

**Kết luận**: **KHÔNG làm.** Nhưng **cửa đã mở sẵn với chi phí bằng 0** nhờ port `IExchangeOrderProvider`.
Đó là vị thế kiến trúc đúng: giữ quyền chọn, không thực thi quyền chọn. Xem lại **chỉ khi** đồng hồ NQ05
thật sự bắt đầu chạy **và** tác giả vẫn muốn tự dùng công cụ.

### 5.4 NFT / token gating cho gói trả phí → **KHÔNG**

Bốn lý do độc lập, mỗi lý do đủ để dừng:

1. **Kém hơn nghiêm ngặt so với một dòng trong bảng.** Vẫn cần hệ thống tài khoản cho chính ứng dụng
   (cookie auth, `Program.cs:56-66`). NFT chỉ **thêm** một tầng, không **thay** tầng nào.
2. **Tạo bài toán hỗ trợ mới**: user bán NFT giữa kỳ → mất quyền truy cập → khiếu nại. Chuyển nhượng
   quyền truy cập là **tính năng của NFT** và là **bug của SaaS**.
3. **Giết phễu.** Bắt user có ví là một bước rơi rụng nữa trong một phễu mà SOM toàn cầu chỉ có
   **21–148 khách** [file 03 §1.6].
4. **Không hợp pháp tại VN cho mục đích thanh toán.** Crypto vẫn **cấm** làm phương tiện thanh toán
   [Thư Viện Pháp Luật, 17/07/2025; sanvietnam.com, 06/06/2026]. Bán quyền truy cập lấy token
   là chính xác điều đó.

Cộng thêm: MMW có **0 khách trả tiền**. Gating là giải pháp cho một vấn đề chưa tồn tại.

### 5.5 Bảng tổng kết

| Use case | Kết luận | Phương án off-chain tương đương | Chi phí off-chain |
|---|---|---|---:|
| On-chain proof of track record | ⛔ **KHÔNG** | Ed25519 + RFC 3161 + append-only log | 2–4 nd |
| Non-custodial copy-trading | ⛔ **KHÔNG** | Không có (và không nên có — file 03 §3.3.3) | — |
| DEX perp thay CEX | ⛔ **KHÔNG trong 18 tháng** | Giữ port `IExchangeOrderProvider` mở | 0 nd |
| NFT/token gating | ⛔ **KHÔNG** | Một cột `PlanId` | ~0 nd |

> **Và một điểm quan trọng ít ai nói**: MMW hiện có **zero bề mặt on-chain**, và đó chính là **lý do**
> file 01 §5.4 xếp mô hình hiện tại vào ô ✅ *"An toàn"*. Thêm bất kỳ thành phần chain nào là
> **đánh đổi một tài sản pháp lý đang có** lấy một tính năng không ai yêu cầu.

---

## 6. UI/UX

### 6.1 Hiện trạng — đo, không đoán

| Chỉ số | Giá trị |
|---|---|
| 23 view Razor, 3.287 dòng `.cshtml` | [file 02 §1.1] |
| Tài nguyên frontend | **4 asset từ `cdn.jsdelivr.net`**: Tabler CSS 1.0.0, Tabler icons 3.17.0, select2 4.1.0-rc.0, jQuery 3.7.1, Tabler JS, SignalR client 8.0.7 (`_Layout.cshtml:22-24, 148-151`) |
| `wwwroot` | chỉ có `css/site.css` và `log/` — **không có node_modules, không có bundler, không có build step** |
| Realtime | SignalR đã nối (`NotificationHub.cs`, `Program.cs:118`), **chỉ dùng cho notification** |
| CSP / SRI | **không có** (grep rỗng) |

### 6.2 Đánh giá cho sản phẩm real-time nhiều bảng biểu

**Điểm mạnh cần bảo vệ (và nó lớn hơn người ta tưởng):**

> **Không có build toolchain frontend.** Với một dev 17,5 giờ/tuần, mỗi giờ **không** phải dành cho
> nâng cấp vite/webpack, sửa lỗi transitive dependency, hay đuổi theo phiên bản React là một giờ dành cho
> 9 lớp chặn chưa có test. Đây là một lựa chọn kiến trúc **đúng**, không phải một khoản nợ.

**Điểm yếu thật — và ba trong bốn cái là vấn đề BACKEND:**

| # | Vấn đề | Vị trí | Là vấn đề frontend? |
|---|---|---|---|
| 1 | **Gọi Binance đồng bộ trong request pipeline, timeout 10 giây** | `TradesController.cs:92-97, 137-160, 616-626`, `HomeController.cs:61-72` | ❌ **Backend.** SPA không sửa được. |
| 2 | **`GetAllAsync()` rồi `.Skip().Take()` trong bộ nhớ** — phân trang ảo | `TradesController.cs:73-78`, `TradeService.cs:51-56` | ❌ **Backend.** Trang chậm dần vĩnh viễn theo số lệnh. |
| 3 | **Truy vấn đồng bộ trong action async** (`.Count()`, `.ToList()`) chặn thread pool | `MarketController.cs:33-48` | ❌ **Backend.** |
| 4 | Cập nhật PnL/advisor phải **F5 trang** dù SignalR đã sẵn | `_Layout.cshtml:151` (client đã nạp) | ✅ Frontend — nhưng là **thiếu 1 handler**, không phải thiếu 1 framework |

> **Kết luận chẩn đoán: "app chậm" ở MMW gần như hoàn toàn là bệnh backend.**
> Viết lại frontend chữa đúng **1/4** triệu chứng và **0/4** nguyên nhân.

### 6.3 Có nên đổi SPA? — **KHÔNG. Và đây là cảnh báo cụ thể nhất trong tài liệu này.**

| | SPA rewrite | Sửa đúng chỗ |
|---|---:|---:|
| Ngày-người (ước lượng) | **25–45** | **3–5** |
| So với toàn bộ backlog ROI cao của file 03 §7.1 (17,35–27,35 nd) | **1,5–3 lần LỚN HƠN** | ~1/5 |
| Sửa được vấn đề #1 (Binance đồng bộ)? | ❌ | ✅ |
| Sửa được vấn đề #2 (phân trang ảo)? | ❌ | ✅ |
| Sửa được vấn đề #3 (sync trong async)? | ❌ | ✅ |
| Sửa được vấn đề #4 (realtime)? | ✅ | ✅ |
| Thêm rủi ro mới | ✅ build chain, bundle size, CORS, auth token trong browser, dependency churn | — |
| Sửa được lớp chặn nào chưa test? | **0/9** | — |

> **Nói thẳng**: viết lại frontend là cách tiêu 25–45 ngày-người để có được **cùng một sản phẩm chưa an toàn
> để chạy tiền thật**, chỉ là mượt hơn khi bấm. Với ràng buộc 2,19 ngày-người/tuần, đó là **11–20 tuần** —
> đúng bằng toàn bộ cửa sổ Giai đoạn 1 của lộ trình ở mục 7. **KHÔNG làm.**

**Làm gì thay thế — 3–5 ngày-người, ROI cao hơn nhiều:**

| # | Việc | Ngày-người | Kết quả |
|---|---|---:|---|
| U1 | Đưa lời gọi sàn ra khỏi request: đọc từ cache/DB, làm mới bằng job nền, đẩy qua SignalR | **1,5–2** | **Sửa đúng nguyên nhân "trang chậm 10 giây"** |
| U2 | Đẩy PnL/advisor/flag qua `NotificationHub` đã có thay vì F5 | 1–1,5 | Realtime thật, tận dụng hạ tầng đã trả tiền |
| U3 | Phân trang phía DB — `PaginatedResult` **đã có sẵn** trong `MMW.Shared` | 0,5–1 | Trang Trades không chậm dần theo thời gian |
| U4 | **Self-host 4 asset CDN + thêm SRI + thêm CSP + chuyển log ra khỏi `wwwroot`** | **0,5** | Đóng T4 và A-03. **Bắt buộc cho app chạm tiền.** |
| U5 | htmx hoặc Alpine (một file, không build) cho cập nhật từng phần **nếu** thật sự cần | 0,5 | **Không phải React** |

### 6.4 Mobile — PWA, và thậm chí PWA cũng là tuỳ chọn

**Native: KHÔNG.** Hai codebase nữa, hai quy trình review store, một bề mặt xác thực thứ hai cho một ứng dụng
chạm tiền, cho **một người dùng**. Không vừa ràng buộc, không cần bàn thêm.

**PWA: có thể, 2–4 ngày-người (ước lượng)** — manifest + service worker cache shell + Web Push nối vào
pipeline notification đã có (`NotificationService`, `INotificationEmailQueue`, `IRealtimeNotificationSender`).

**Hai cảnh báo trung thực:**

1. **Web Push trên iOS có lịch sử hạn chế** (yêu cầu cài về màn hình chính, và khả năng nhận nền không
   ngang Android). **Tôi không xác minh được trạng thái 2026** trong lượt này. Nếu kênh cảnh báo quan trọng
   là điện thoại iOS, hãy kiểm chứng trước khi đầu tư, hoặc dùng Telegram bot (~0,5 ngày-người, hoạt động ở
   mọi nền tảng, và tác giả đã ở trong hệ sinh thái Telegram theo file 01 §4.3).

2. **Một lập luận theo hiến chương, và tôi cho là nó quyết định:**
   > Nguyên tắc I: *"Tính năng chỉ nhằm tăng tần suất vào lệnh... KHÔNG ĐƯỢC đưa vào sản phẩm"*
   > (`constitution.md:46-47`).
   > Một app mobile cho phép **đặt lệnh** làm giao dịch dễ hơn ⇒ **trái nguyên tắc I**.
   > Một bề mặt mobile chỉ cho **đóng lệnh / xác nhận cảnh báo / xem cờ vi phạm** thì **phục vụ** kỷ luật.
   >
   > **Khuyến nghị: nếu làm PWA, chỉ làm read + đóng lệnh + xác nhận. Không có nút mở lệnh.**
   > Đây là một quyết định sản phẩm được suy ra từ hiến chương, và nó cũng làm giảm 60% khối lượng công việc.

---

## 7. Lộ trình kỹ thuật 3 giai đoạn

**Ràng buộc cứng**: 1 dev, 15–20 giờ/tuần = **2,19 ngày-người/tuần** [file 03 §6.1].
Mọi hạng mục dưới đây được đối chiếu với ngân sách đó, và **những gì không vừa được nói thẳng là không vừa**.

### Giai đoạn 0 — Tuần này. 0,35 ngày-người.

| Việc | nd | Lý do |
|---|---:|---|
| `git add -A && git commit` + push remote riêng tư + `git rm -r --cached .vs/` | **0,25** | **143 mục chưa commit, commit cuối 2026-06-01 — xác minh lại hôm nay.** `LiveOrderService.cs`, `TradePreflightAnalysisService.cs`, `constitution.md`, `spec.md` **chưa từng được git theo dõi**. Không làm việc này thì mọi việc khác có thể vô nghĩa. |
| 5 email tới 5 crypto prop firm | 0,1 | Quyền chọn đuôi phải +$48.317, chi phí ~0 [file 03 F4] |

### Giai đoạn 1 — 0–3 tháng. Ngân sách: 13 tuần × 2,19 = **28,5 ngày-người.**

**Mục tiêu**: *làm cho công cụ của chính tác giả ĐÚNG, RẺ và AN TOÀN. Không xây gì cho người khác.*

| # | Hạng mục | nd | Neo | Rủi ro nếu bỏ |
|---|---|---:|---|---|
| 1.1 | **R0 — ghi `usage` token vào audit** | **0,5** | `MiniMaxLlmService.cs:77-85` (+2 adapter) | Không có nó thì **không kiểm chứng được** mọi con số chi phí. File 03 §8 xếp đây là phép thử rẻ nhất có thể lật đổ chính nó. **Làm tuần 1, đọc hoá đơn tháng 9.** |
| 1.2 | **`DisableConcurrentExecution` cho 5 job + khoá `ScanNow`** | **0,5** | `Program.cs:126-155`, `MarketController.cs:53-60` | TOCTOU vi phạm SC-005 + nhân hoá đơn LLM. **Tỷ lệ giá trị/công sức cao nhất toàn tài liệu.** |
| 1.3 | **R1+R3+R4+R5 — cắt tần suất LLM** | **3–6** | mục 2.4.4 | −98% chi phí AI cho chính mình |
| 1.4 | **A-01 — xử lý HTTP 503 "Unknown": query `origClientOrderId` trước khi huỷ** | **0,5–1** | `LiveOrderService.cs:272-283`, `:256` | **Vị thế ma — vi phạm Nguyên tắc III.** `clientOrderId` đã tất định nên sửa rất rẻ. |
| 1.5 | **Mã hoá `ApiKey`/`ApiSecret` (bản self-host)** | **3–4** | `TradingAccount.cs:29-34` | S-01 🔴 |
| 1.6 | **Sửa hợp đồng port venue (C-01 gốc) + `TradingAccount` mang venue** | **2–3** | mục 1.2 | Sổ giao dịch hiện **không đáng tin** |
| 1.7 | **A-02 — chuyển sang `/fapi/v1/klines` + mark price** | **1,5–2,5** | `BinanceMarketDataProvider.cs:45` | Chỉ báo và SL/TP đang tính trên **sai thị trường** |
| 1.8 | **C-03 ngày giao dịch theo UTC+7 + C-04 vốn đầu ngày** | **2–4** | `TradingDayService.cs:36, 74`, `LiveOrderService.cs:228` | Không sửa thì "5 lệnh/ngày" và "lỗ 3%/ngày" reset lúc 07:00 giờ VN ⇒ **cơ chế kỷ luật không bảo vệ được gì** |
| 1.9 | **Test cho 9 lớp chặn còn thiếu**, ưu tiên #14, #17, #15, #7 | **2–3** | `LiveOrderService.cs:200-208, 229-235, 212-216, 111-119` | Nguyên tắc VI; bảo vệ đúng chỗ tiền chảy ra |
| 1.10 | **U4 — self-host CDN + SRI + CSP + log ra khỏi `wwwroot`** | **0,5** | A-03, A-04 | Đường tới tiền thật **không cần khoá API** |
| 1.11 | Nâng/bỏ AutoMapper 14.0.0 (NU1903) | **0,5–1** | `MMW.Application.csproj:10` | Cổng chất lượng #2 của hiến chương đang **fail** |
| 1.12 | R6 — dọn `AiSignalScanRecord` cũ | **1** | mục 2.4.4 | Trần 10 GB SQL Express |
| | **CỘNG** | **17,5 – 27,5** | | |

**Đối chiếu ngân sách: 17,5–27,5 nd so với 28,5 nd khả dụng.**

> ⚠️ **Vừa, nhưng ở cận trên là KHÔNG CÒN SLACK.** Nếu có bất kỳ trượt tiến độ nào, thứ bị cắt sẽ là mục
> 1.9 (test) vì nó "không thấy được" — mà đó chính xác là thứ **không được phép cắt**.
> **Khuyến nghị lịch: đưa 1.9 lên trước 1.10–1.12.** Nếu tháng thứ 3 mà 1.10–1.12 chưa xong thì không sao;
> nếu 1.9 chưa xong thì đừng bật `LiveTrading.Enabled=true`.

### Giai đoạn 2 — 3–9 tháng. Ngân sách: 26 tuần × 2,19 = **57 ngày-người.**

**Cổng quyết định đầu giai đoạn** (tuần 13): *có prop firm nào trả lời "có" không?* [file 03 §7.1 tuần 12]

---

#### Nhánh 2A — KHÔNG có ai trả lời (xác suất cao nhất theo file 03 §6.3: 50% bi quan + 35% cơ sở)

**Mục tiêu**: *trả nợ kỹ thuật, xây tài sản không cần khách hàng, rồi TRẢ LẠI THỜI GIAN.*

| # | Hạng mục | nd | Lý do |
|---|---|---:|---|
| 2A.1 | **Tách `LlmJsonReader` dùng chung + bộ test bảng cho parser** | **3–5** | Xoá ~250 dòng trùng lặp (`MarketScanService.cs:754-948` vs `TradePreflightAnalysisService.cs:607-795`); phủ đúng 5 loại đầu vào lỗi mà `constitution.md:146-147` gọi tên |
| 2A.2 | **Tách `MarketScanService`**: bỏ nhánh auto-đặt-lệnh ra khỏi service quét | **3–5** | `MarketScanService.cs:504` gọi thẳng `PlaceForTradeAsync` — cắt liên kết này là điều kiện để test được đường tiền thật |
| 2A.3 | **Trang "review cờ vi phạm theo thời gian"** | **3–5** | **Đây là giá trị cốt lõi "học từ lỗi" mà spec tuyên bố và HIỆN CHƯA TỒN TẠI** [file 02 §2 mục 19]. Nghịch lý lớn nhất của sản phẩm: có `Flag`, có behavior detector, có 9 test — nhưng **không có màn hình nào để nhìn lại**. |
| 2A.4 | **R2 — bảng `SymbolVerdict` + tách 2 tầng** | **3–5** | Không cần cho 1 user, nhưng **mở khoá mục 2.5** (track record công khai) |
| 2A.5 | **Trang track record công khai từ `SymbolVerdict`** | **1–2** | Tài sản duy nhất xây được mà không cần khách hàng (mục 2.5). **Phải trình bày là nhật ký đánh giá mô hình, không phải tín hiệu.** |
| 2A.6 | CI (build + test + `dotnet list package --vulnerable`) | **1–2** | 7 cổng chất lượng hiện được thực thi **bằng ý chí**, không bằng máy [file 02 D-04] |
| 2A.7 | Dọn + mở nguồn (squash 27 migration thành 1 baseline) | **3–5** | [file 03 F5]; xoá 16.710 dòng mã sinh; giải quyết vĩnh viễn D-01 |
| 2A.8 | U1+U2+U3 (backend perf + realtime) | **3–4,5** | mục 6.3 |
| | **CỘNG** | **20 – 33,5** | |

**Ngân sách 57 nd, dùng 20–33,5 ⇒ dư 23,5–37 ngày-người ≈ 190–300 giờ.**

> **Và đây là khuyến nghị mà tôi cho là quan trọng nhất của cả lộ trình:
> ĐỪNG lấp chỗ trống đó bằng việc lập trình.**
> File 03 §6.6 đã chứng minh kịch bản đối chứng thắng ở mọi mức xác suất. Số giờ dư ra là **lợi ích**,
> không phải khoảng trống cần điền. Vạch thêm hạng mục để lấp 300 giờ là cách chắc chắn nhất để biến
> một kết luận đúng thành một dự án 18 tháng.

---

#### Nhánh 2B — CÓ một prop firm nói "có"

| # | Hạng mục | nd |
|---|---|---:|
| 2B.1 | Toàn bộ nhánh 2A.1, 2A.2, 2A.4, 2A.6 (điều kiện cần) | 10–17 |
| 2B.2 | A5 (mã hoá bản multi-tenant + KMS + audit + rotation) | 6–9 |
| 2B.3 | A6 (bịt IDOR) + A7 + A8 | 5,5–7,5 |
| 2B.4 | A11 (ngân sách rate-limit Binance) + A10 (quota LLM/tenant) | 5–8 |
| 2B.5 | A1 + A2 + A3 + A4 (tenant hoá) | 14,5–22 |
| | **CỘNG** | **41 – 63,5** |

> ⚠️ **Ngân sách 57 nd. Một chữ "có" duy nhất tiêu HẾT hoặc VƯỢT toàn bộ cửa sổ 3–9 tháng,
> và trong suốt quãng đó doanh thu vẫn bằng $0** (chu kỳ bán B2B 3–9 tháng, giả định [file 03 §3.3.5]).
> Đây là con số cần nói trước khi trả lời "có" cho prop firm, không phải sau.
> **Khuyến nghị: nếu có "có", hãy đàm phán một pilot CÓ PHÍ TRẢ TRƯỚC trước khi viết dòng code thứ nhất**
> — đúng tinh thần "gật đầu không tính; chuyển khoản mới tính" của file 03 §8.

### Giai đoạn 3 — 9–18 tháng. Ngân sách: 39 tuần × 2,19 = **85 ngày-người.**

**Kịch bản cơ sở (không có khách hàng): KHÔNG CÓ GIAI ĐOẠN 3.**

Tôi không vạch một danh sách hạng mục cho giai đoạn này, và việc đó là có chủ đích.
Nếu tới tháng thứ 9 vẫn chưa có ai trả tiền, thì bằng chứng đã đủ để nói rằng 85 ngày-người tiếp theo
có giá trị kỳ vọng cao hơn **ở nơi khác**. Vạch một roadmap để lấp chỗ trống là cách một phân tích trung thực
tự phản bội nó.

**Chỉ nếu Giai đoạn 2 kết thúc với một pilot có phí đã ký:**

| Hạng mục | nd | Ghi chú |
|---|---:|---|
| Phase B rút gọn cho B2B (hoá đơn thủ công, không cần Stripe/landing/self-serve onboarding) | 12–20 | Bỏ được ~60% phase B [file 03 §3.3.5] |
| Tách Hangfire ra worker riêng | 3–5 | [file 02 C1] |
| Health check, metric, tracing, alert khi job chết | 3–5 | [file 02 C4] |
| Backup/restore + migration không downtime + tách migration khỏi startup | 3,5–4,5 | `SeedData.cs:23` chạy migrate ở **mọi** instance |
| Adapter sàn thứ hai **+ lớp chuẩn hoá symbol/interval** | **11–19** | 8–14 cho adapter + **3–5 cho lớp chuẩn hoá mà không ai tính** (mục 1.2, rò rỉ 3) |
| SignalR backplane + phân trang/aggregate phía DB | 5–8 | |
| | **CỘNG** | **37,5 – 61,5** |

### 7.1 Những hạng mục KHÔNG VỪA ràng buộc — nói thẳng

| Hạng mục | Ngày-người | Tuần ở nhịp 2,19 nd/tuần | Phán quyết |
|---|---:|---:|---|
| **SaaS B2C đầy đủ** (A+B+C của tôi) | **98,5 – 149** | **45–68 tuần** | ❌ **KHÔNG VỪA.** 10,5–16 tháng build thuần, $0 doanh thu. Và SOM 21–148 khách < 109–186 khách hoà vốn [file 03 §1.6, §3.3.1] |
| **SPA rewrite** | 25–45 | 11–20 tuần | ⚠️ **Vừa lịch, nhưng GIÁ TRỊ ÂM.** Chiếm trọn Giai đoạn 1, sửa 0/4 vấn đề thật (mục 6.3) |
| **Native mobile (iOS+Android)** | không ước lượng | — | ❌ **KHÔNG VỪA.** 2 codebase + 2 store + bề mặt auth thứ hai cho 1 người dùng |
| **DEX perp adapter** | 10–18 | 5–8 tuần | ❌ **Vừa lịch, sai ưu tiên.** Cạnh tranh trực tiếp với test bảo vệ tiền thật (mục 5.3) |
| **Prop firm tự vận hành** | — | — | ❌ Không phải dự án phần mềm [file 03 §3.3.4] |
| **Token / smart contract bất kỳ** | — | — | ⛔ Xem mục 5 và file 03 §4 |

### 7.2 Lộ trình một trang

```
TUẦN 0        git commit + push  ·  5 email prop firm                        0,35 nd
              ─────────────────────────────────────────────────────────────
GĐ 1  0–3TH   R0 token accounting  →  DisableConcurrentExecution
              →  R1/R3/R4/R5 (cắt 98% chi phí AI)
              →  A-01 (vị thế ma)  →  mã hoá khoá
              →  port venue  →  spot→perp  →  múi giờ + vốn đầu ngày
              →  TEST 9 LỚP CHẶN  →  CSP/SRI/log  →  AutoMapper        17,5–27,5 nd
              MỤC TIÊU: công cụ của CHÍNH MÌNH đúng, rẻ, an toàn.
              ─────────────────────────────────────────────────────────────
    TUẦN 13   ⟡ CỔNG QUYẾT ĐỊNH: có prop firm nào nói "có"?
              ─────────────────────────────────────────────────────────────
GĐ 2  3–9TH   [KHÔNG]  LlmJsonReader+test parser · tách god-class
                       · TRANG REVIEW CỜ VI PHẠM (giá trị lõi còn thiếu)
                       · SymbolVerdict + track record công khai
                       · CI · mở nguồn · U1–U3                          20–33,5 nd
                       ⇒ DƯ 23,5–37 nd. KHÔNG lấp. Trả lại thời gian.
              [CÓ]     + tenant hoá phase A                              41–63,5 nd
                       ⇒ TIÊU HẾT/VƯỢT ngân sách, doanh thu vẫn $0.
                       ⇒ Đòi pilot CÓ PHÍ TRẢ TRƯỚC trước dòng code đầu.
              ─────────────────────────────────────────────────────────────
GĐ 3 9–18TH   [KHÔNG]  KHÔNG CÓ GIAI ĐOẠN 3. Có chủ đích.
              [CÓ+ký]  Phase B rút gọn · worker riêng · observability
                       · sàn thứ hai + chuẩn hoá symbol                 37,5–61,5 nd
```

---

## 8. Điều gì có thể làm tài liệu này SAI

| # | Điều kiện | Xác suất *(chủ quan)* | Cách kiểm chứng — rẻ nhất trước |
|---|---|---|---|
| **1** | **Ước lượng "4–10 giây mỗi symbol" của tôi sai nhiều lần.** Nếu LLM trả trong 1 giây, ngưỡng vỡ của vòng lặp tuần tự (mục 2.2) là 300 symbol chứ không phải 30–75 | Trung bình | **Log `Stopwatch` quanh `_llm.ChatAsync` trong 1 tuần.** Chi phí: 1 giờ. Làm cùng lúc với R0. |
| **2** | **Basis spot–perp nhỏ tới mức A-02 không có hệ quả thực tế** cho watchlist của tác giả | Trung bình *(đúng với BTC/ETH, sai với alt)* | So sánh 200 nến 1h của `/api/v3/klines` và `/fapi/v1/klines` cho từng symbol trong watchlist, tính lệch ATR. Chi phí: 2 giờ. **Nếu lệch <0,5% thì A-02 hạ xuống 🟡 và có thể hoãn.** |
| **3** | **Weight của `/api/v3/klines` cao hơn nhiều so với giả định 2** | Thấp | Đọc header `X-MBX-USED-WEIGHT-1M` mà Binance đã trả về mỗi request. Chi phí: 30 phút. Hiện `BinanceMarketDataProvider.cs:99-102` **vứt bỏ toàn bộ header**. |
| **4** | **Mặc định `workingType` của Binance khiến SL/TP kích hoạt theo mark price** ⇒ SL tính từ nến spot lệch thêm một tầng nữa | Trung bình | Đặt một lệnh testnet có SL và đọc lại `workingType` trong phản hồi. Chi phí: 1 giờ. |
| **5** | **Ước lượng multi-tenant +19% của tôi sai theo hướng ngược lại** — nếu A2 dễ hơn tôi nghĩ | Thấp–trung bình | Spike 1 ngày: bật `HasQueryFilter` cho **một** entity và chạy job `market-scan`. Nếu job vẫn chạy đúng thì tôi sai. **Đây là phép thử rẻ nhất cho phần đắt nhất.** |

Điều kiện **#1 và #5 là quan trọng nhất và rẻ nhất** — mỗi cái có thể lật một mục lớn của tài liệu này
với dưới 1 ngày công. **Làm chúng trước khi tin tôi.**

---

## Phụ lục A — Nguồn

**Số liệu lấy trực tiếp trong lượt này (2026-07-30), không có trong file 01–03:**

- `GET https://fapi.binance.com/fapi/v1/exchangeInfo` — `rateLimits`: REQUEST_WEIGHT 2400/1M ·
  ORDERS 1200/1M · ORDERS 300/10S [truy cập 30/07/2026]
- `GET https://data-api.binance.vision/api/v3/exchangeInfo` — REQUEST_WEIGHT 6000/1M ·
  RAW_REQUESTS 300000/5M [truy cập 30/07/2026]
- Binance Developer Docs — *Futures (USDⓈ-M) → General Info*: HTTP 429/418; *"IP bans... scale in duration
  for repeat offenders, from 2 minutes to 3 days"*; *"The limits on the API are based on the IPs, not the
  API keys"*; xử lý HTTP 503 "Unknown error" (trạng thái thực thi **không xác định**, phải verify bằng
  orderId trước khi gửi lại); mã −1008 và miễn trừ reduce-only/close-position [truy cập 30/07/2026]

**Tái sử dụng từ file 01–03** (nguyên nguồn + ngày như đã ghi tại chỗ): CryptoFundTrader (24/12/2025) ·
tradermake.money · coinmarketman.com · tiltguard.app · zerotilt.io · arizet.com · UseThisAI.fyi ·
plancana.com (12/03/2026) · Dentons LuatViet (22/10/2025) · sanvietnam.com (06/06/2026) ·
Thư Viện Pháp Luật (17/07/2025) · VnEconomy (06/05/2026) · Báo Chính phủ (01/04/2026) ·
FinanceFeeds dẫn Adam Tracy (20/05/2026) · SEC.gov press release 2026-30 (17/03/2026) ·
Sullivan & Cromwell (19/03/2026) · thirdweb (17/06/2026) · CoinStats AI (01/07/2026) ·
TokenInsight Q2 2026 (20/07/2026) · Cục Thống kê Q2/2026 qua thuvienphapluat.vn (04/07/2026)

**Mã nguồn đọc trực tiếp tại 2026-07-30** (không suy diễn từ file 02):
`src/MMW.Application/Services/MarketScanService.cs:22, 31-96, 157-227, 238-314, 344-463, 465-514, 555-693, 754-948` ·
`src/MMW.Application/Services/TradeAdvisorService.cs:17, 27-32, 52-111, 151-210, 230-251` ·
`src/MMW.Application/Services/LiveOrderService.cs:56-344, 531-546` ·
`src/MMW.Application/Services/LiveBalanceService.cs:25-48` ·
`src/MMW.Application/Services/RuleEvaluationService.cs:53-112` ·
`src/MMW.Application/Services/TradingDayService.cs:27-78` ·
`src/MMW.Application/RuleEngine/{IRuleEngine,ITradeRule,RuleViolation,RuleEvaluationContext}.cs` ·
`src/MMW.Application/Behavior/BehaviorContext.cs` ·
`src/MMW.Application/DependencyInjection.cs:23-86` ·
`src/MMW.Domain/Entities/{TradingAccount,RiskSetting,AppSetting}.cs` ·
`src/MMW.Domain/Enums/TradingEnums.cs:116-132` ·
`src/MMW.Domain/DbContext/MmwDbContext.cs:16-46` ·
`src/MMW.Infrastructure/DependencyInjection.cs:22-131` ·
`src/MMW.Infrastructure/Exchanges/Binance/{BinanceOptions,BinanceMarketDataProvider,BinanceAccountProvider,BinanceAccountProviderFactory,BinanceFuturesOrderProvider}.cs` ·
`src/MMW.Infrastructure/Ai/{LlmOptions,MiniMaxLlmService}.cs` ·
`src/MMW.Web/Program.cs:14-167` · `src/MMW.Web/Data/SeedData.cs:11-45` ·
`src/MMW.Web/Infrastructure/HangfireAuthorizationFilter.cs` ·
`src/MMW.Web/Views/Shared/_Layout.cshtml:22-24, 148-151` · `src/MMW.Web/appsettings.json` ·
`.specify/memory/constitution.md:46-49, 90-95, 118, 127-128, 146-147, 163-164`

**Không xác minh được trong lượt này** (đã đánh dấu tại chỗ):
bảng weight theo tham số `limit` của `/api/v3/klines` · mặc định `workingType` của Binance Futures ·
số worker mặc định của Hangfire 1.8 · Binance có cung cấp OAuth-style delegation cho user thường không ·
biểu giá LLM 07/2026 · trạng thái Web Push trên iOS 2026 · giá Azure Key Vault / AWS KMS

---

*Tài liệu này là thẩm định kiến trúc kỹ thuật. Mọi nhận định về mã nguồn đều neo vào `đường/dẫn/file.cs:dòng`
và được đọc trực tiếp tại 2026-07-30. Mọi con số công sức đều là **ước lượng** chủ quan và được đánh dấu.
Mọi nhận định pháp lý là tóm tắt nguồn công khai và cần luật sư xác nhận. Tài liệu **KHÔNG** chứa lời khuyên
đầu tư cá nhân và không khuyến nghị mua/bán bất kỳ tài sản nào.*
