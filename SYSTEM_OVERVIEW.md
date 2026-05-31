# MMW — Trading Assistant System Overview

**Cập nhật:** 2026-05-31  
**Model:** .NET 8 + Tabler + PostgreSQL/SQLite InMemory  
**Test:** 48/48 pass · 7 migrations applied · 0 build errors

---

## 📋 Tổng quan

**MMW (My Market Wisdom)** là ứng dụng ghi nhận & phân tích lệnh giao dịch crypto — giáo dục người dùng về **kỷ luật & quy tắc** thay vì dự đoán thị trường. Chạy quy trình **định kỳ (Hangfire)** tự động phân tích lệnh mở, phát hiện hành vi tâm lý tiêu cực, ghi nhận **AI feedback** (LLM).

### Lịch sử phát triển
- **V1 (2026-05) — MVP Core:** Rule Engine, behavior detector, Dashboard
- **V2 (2026-05) — Market Intelligence:** Live scan job (5 phút), Signal Generator, Advisor (LLM)
- **V3 (hiện tại):** Trade Journal form (thêm/sửa/đóng lệnh), OrderType, prefill từ đề xuất

---

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    MMW.Web (ASP.NET Core MVC)               │
│  Controllers (Auth, Trades, Signals, Market, Accounts...)   │
│  Views (Cshtml + light glassmorphism CSS)                   │
└─────────────────────────────────────────────────────────────┘
                             ↓
┌─────────────────────────────────────────────────────────────┐
│              MMW.Application (Business Logic)               │
│  Services: Trade, TradeAdvisor, MarketScan, RuleEngine...   │
│  RuleEngine (5 rules), Behavior (3 detectors)               │
│  Indicators (SMA/EMA/RSI/MACD/ATR), SignalGenerator         │
└─────────────────────────────────────────────────────────────┘
                             ↓
┌─────────────────────────────────────────────────────────────┐
│              MMW.Infrastructure (Data & External)           │
│  Repositories, DbContext, UnitOfWork                        │
│  Binance adapters (market data, account read-only)          │
│  Hangfire background jobs                                   │
└─────────────────────────────────────────────────────────────┘
                             ↓
┌──────────────┬──────────────┬──────────────┐
│ MMW.Domain   │ MMW.Shared   │ DB (SQLite)  │
│ Entities     │ Interfaces   │ 15 tables    │
│ Enums        │ Repository   │ + Hangfire 7 │
└──────────────┴──────────────┴──────────────┘
```

**Kiến trúc:** Clean Layered, Port/Adapter cho Binance, DI/Repository pattern.

---

## 📊 Domain Model (15 Entities)

| Entity | Mục đích | Quan hệ |
|--------|---------|--------|
| **Trade** | Lệnh giao dịch (journal) | M:1 TradingAccount, M:1 Strategy |
| **TradeAnalysis** | Phân tích lệnh Open (giá, indicator, khoảng cách SL/TP) | 1:1 Trade |
| **TradeSignal** | Đề xuất lệnh từ scan job | M:1 TradingAccount |
| **TradeTag** | Nhãn lỗi/điều kiện | M:1 Trade |
| **TradingAccount** | Tài khoản (Binance, backtest) | 1:N Trade, TradingDay |
| **RiskSetting** | Cài rủi ro per account | 1:1 TradingAccount |
| **Strategy** | Chiến lược giao dịch | M:1 TradingAccount |
| **TradingDay** | Tổng hợp ngày (PnL, winrate, streak) | M:1 TradingAccount |
| **Flag** | Cảnh báo rule/behavior | M:1 Trade |
| **WatchItem** | Symbol watchlist | M:1 TradingAccount |
| **IndicatorRecord** | Lịch sử indicator | M:1 TradingAccount |
| **MarketSnapshot** | Ảnh chụp giá | M:1 TradingAccount |
| **User** | Người dùng | 1:1 AppSetting |
| **AppSetting** | Cài đặt global | 1:1 User |

---

## 🎯 Core Features

### 1️⃣ Rule Engine (5 Rules)
- RequireStopLoss → Critical
- MaxRiskPerTradeRule → Warning/Critical
- MinRiskRewardRule → Warning
- MaxTradesPerDayRule → Warning
- DailyLossLimitRule → Critical

### 2️⃣ Behavior Detection (3 Detectors)
- RevengeTradeDetector
- LossStreakDetector
- OversizedAfterLossDetector

### 3️⃣ Market Intelligence (Scan 5 phút)
- Live ticker (Binance)
- Indicator engine (SMA/EMA/RSI/MACD/ATR)
- Market analyzer (bias từ EMA/MACD)
- Signal generator (RR=2)
- Trade advisor (LLM analysis)

### 4️⃣ Trade Journal
- Ghi nhận lệnh (form tay / từ đề xuất)
- Chọn loại lệnh (Market/Limit/StopLimit)
- Lấy giá live
- Đóng lệnh (PnL + Outcome + số dư)
- Xem phân tích & cảnh báo

---

## 📈 Session Này Làm Gì (2026-05-31)

1. **UI Light Glassmorphism** ✅
   - Viết lại site.css (nền pastel + blob trôi + kính blur)
   - Verified: login + dashboard + market page

2. **Form Ghi Nhận Lệnh** ✅
   - Create GET/POST (form thêm tay)
   - Edit GET/POST (sửa lệnh)
   - Delete (xoá lệnh)
   - Close GET/POST (đóng lệnh, tính PnL)
   - TradeService.CloseAsync (PnL + số dư + Outcome)
   - Test: 2 case (Long win, Short loss) pass

3. **OrderType + Loại Lệnh** ✅
   - Enum OrderType (Market/Limit/StopLimit)
   - Field trên Trade + TradeDto
   - Migration AddOrderType applied

4. **Prefill từ Đề Xuất** ✅
   - `/Trades/Create?signalId=113` → form điền sẵn
   - Symbol, entry/SL/TP, auto-size, OrderType=Limit
   - Banner hint "Điền sẵn từ đề xuất"
   - Nút "Ghi nhận" ở Signals trỏ tới form

5. **Symbol Datalist + Form Polish** ✅
   - Datalist symbol (18 items từ watchlist)
   - Nút "Lấy giá live"
   - Close preview PnL realtime (JS)

---

## ✅ Công Việc Đã Có

- ✅ Auth + TradingAccount management
- ✅ Ghi nhận (thêm/sửa/đóng lệnh)
- ✅ Rule Engine (5 rules)
- ✅ Behavior detectors (3 patterns)
- ✅ Market Intelligence (scan 5 phút)
- ✅ Trade Advisor (LLM V3)
- ✅ Light glassmorphism UI
- ✅ Form create/close + prefill từ signal
- ✅ 48 test pass

---

## ⚠️ Chưa Có

| # | Feature | Độ ưu tiên |
|---|---------|-----------|
| 1 | User Secrets (ApiKey) | 🔴 |
| 2 | Sửa lệnh (Edit) | 🔴 |
| 3 | Xoá lệnh (Delete) | 🔴 |
| 4 | Import fill (FIFO) | 🟡 |
| 5 | Trang xem Flag | 🟡 |
| 6 | Phân trang bảng | 🟡 |
| 7 | Unit test Advisor | 🟡 |
| 8 | Auto-size realtime JS | 🟡 |

---

## 🔧 Tech Stack

| Layer | Stack |
|-------|-------|
| Web | ASP.NET Core 8 MVC + Razor |
| Style | Tabler + custom CSS (light glass) |
| ORM | EF Core Code-first |
| Auth | Cookie + bcrypt |
| Background | Hangfire (in-process) |
| Testing | xUnit + InMemory DB |
| Exchange | Binance API (public) |
| LLM | ILlmService (Claude/OpenAI) |
| DB | SQLite (dev) / PostgreSQL (prod) |

---

## 🏃 Chạy Local

```bash
# Build
dotnet build MMW.sln

# Migrate
dotnet ef database update

# Test
dotnet test tests/MMW.RuleEngine.Tests/

# Run
cd src/MMW.Web && dotnet run
# → http://localhost:5142/Account/Login
# Username: admin | Password: Admin@123
```

---

## 📌 Patterns

- **Repository:** Abstraction cho data access
- **Service-oriented:** Business logic tách theo use case
- **Workflow pipeline:** ProcessTradeAsync → rule → behavior → day summary
- **DI container:** Built-in (Scoped repositories)
- **Rule as plug-in:** Dễ thêm rule mới (loop auto-detect)

---

## 🎯 Hướng Tiếp

**Short-term:** User Secrets, Edit/Delete, auto-size JS, Flag page  
**Mid-term:** Import fill, phân trang, unit test, confirm-before-order  
**Long-term:** Mobile API, notifications, analytics, multi-user, Bybit/OKX

---

**Last updated:** 2026-05-31 · Build: ✅ 0 errors · Test: 48/48 pass
