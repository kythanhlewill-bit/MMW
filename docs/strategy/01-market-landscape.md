# 01 — Bản đồ thị trường 2026 cho phân khúc MMW

> **Loại tài liệu**: Thẩm định thị trường để ra quyết định (market due diligence), KHÔNG phải pitch deck.
> **Ngày lập**: 2026-07-29 · **Người lập**: nghiên cứu thị trường, phiên phân tích MMW
> **Phạm vi**: công cụ hỗ trợ trader crypto bán lẻ — trading journal, trading psychology, AI trading assistant
> **Nguồn dữ liệu**: web research 2025–2026, mọi con số ghi `[nguồn, ngày truy cập]`. Không có số nào được bịa.
> **Giới hạn công cụ**: `WebSearch`/`WebFetch` của môi trường bị lỗi backend trong toàn phiên
> (`There's an issue with the selected model (deepseek-v4-pro)`). Toàn bộ nghiên cứu được thực hiện
> qua browser automation (DuckDuckGo + truy cập trực tiếp trang nguồn). Mọi URL đều đọc được nội dung thật.

---

## 0. Kết luận trước, luận cứ sau

Ba câu trả lời thẳng, để bạn không phải đọc 900 dòng mới biết:

1. **MMW là một sản phẩm kỹ thuật tốt trong một thị trường đang co lại, ở đúng đáy chu kỳ, với
   khoảng trống cạnh tranh hẹp hơn nhiều so với giả định ban đầu.** Luận điểm "chưa ai lấy behavior
   detection làm core" **đã sai tại thời điểm 07/2026** — có ít nhất 8 sản phẩm đang lấy đúng điều đó
   làm core, một số sâu hơn MMW về mặt tín hiệu hành vi.

2. **Khoảng trống thật sự còn lại rất hẹp nhưng có thật**: *chặn cứng lệnh futures crypto ở tầng
   API sàn, trước khi lệnh chạm sàn, dựa trên trạng thái hành vi + rule cá nhân*. Không sản phẩm nào
   trong 18 sản phẩm khảo sát làm việc này. TiltGuard chặn ở tầng trình duyệt/TradingView; prop firm
   chặn ở tầng tài khoản; journal chặn... không gì cả, chỉ ghi lại sau khi đã thua. MMW `LiveOrderService`
   với ~13 lớp chặn tuần tự là thứ duy nhất chặn đúng chỗ.

3. **Nhưng khoảng trống hẹp đó không đủ để dựng một business thương mại trong 12 tháng tới**, vì
   ba lý do độc lập nhau, mỗi lý do đủ để giết: (a) chu kỳ crypto đang ở Extreme Fear, retail im lặng,
   volume sàn giảm 32–48%; (b) giá sàn của phân khúc bị neo ở $6–12/tháng bởi Trader Make Money và
   ở $0 bởi mô hình affiliate-rebate của CoinMarketMan; (c) rủi ro pháp lý Việt Nam đang đếm ngược —
   Nghị quyết 05/2025/NQ-CP quy định sau 6 tháng kể từ khi sàn nội địa đầu tiên được cấp phép, người
   Việt giao dịch qua sàn chưa cấp phép (Binance) có thể bị **xử phạt hành chính hoặc truy cứu trách
   nhiệm hình sự**, mà MMW chỉ có adapter Binance.

**Verdict**: *Không khả thi thương mại trong 12 tháng tới ở dạng SaaS bán cho trader Việt Nam.*
Có 2 đường thoát hợp lý (mục 7), không đường nào là "làm tiếp cái đang làm rồi bán".

---

## 1. Quy mô, tăng trưởng và trạng thái chu kỳ

### 1.1 Trạng thái thị trường crypto tại 07/2026 — đây là biến số quan trọng nhất

Đây không phải chi tiết phụ. Nhu cầu với **mọi** công cụ hỗ trợ trader bán lẻ là hàm số của số lượng
trader đang hoạt động, mà số đó là hàm số của giá và biến động. Tại 29/07/2026:

| Chỉ số | Giá trị | Nguồn |
|---|---|---|
| Giá BTC | **$64,279.95** (29/07/2026, 8:27 EDT) | [CoinDesk, truy cập 29/07/2026] |
| Giá BTC (nguồn chéo) | $64,168 (+0.47% 24h) | [WorldCoinIndex, truy cập 29/07/2026] |
| Giá BTC (20/07/2026) | $64,290 — "khoảng giá nén giữa các tín hiệu mâu thuẫn" | [The Cryptonomist, 20/07/2026] |
| ATH tháng 10/2025 | $124,680.48 | [CoinStats AI, 01/07/2026] |
| Mức giảm từ đỉnh | **−45.5%** | [CoinStats AI, 01/07/2026] |
| Fear & Greed Index | **14 — Extreme Fear** | [CoinStats AI, 01/07/2026] |
| Dòng ETF 30 ngày | **−$6.96 tỷ USD** | [CoinStats AI, 01/07/2026] |
| Bối cảnh tháng 6/2026 | "thị trường retail im lặng và tháng ETF tệ nhất lịch sử" | [Yahoo Finance, 29/06/2026] |
| Dự báo Binance Research cho 2026 | thị trường crypto giảm khoảng **7.9%** cả năm | [Binance Square, 16/01/2026] |

Khối lượng giao dịch trên sàn — chỉ số gần nhất với "số trader đang hoạt động":

| Kỳ | Khối lượng | Biến động | Nguồn |
|---|---|---|---|
| Q1/2026 | $17.9 nghìn tỷ USD | **−32% QoQ**, derivatives chiếm 82% | [Phemex News, 20/04/2026] |
| Tháng 3/2026 | $4.3 nghìn tỷ USD | **−48%** so với đỉnh 10/2025, thấp nhất từ 10/2024 | [The Market Periodical, 10/04/2026] |
| Q2/2026 | $16.5 nghìn tỷ USD | −8% QoQ; spot hồi $3.3T→$4.5T; derivatives tụt $14.6T→$12.0T | [TokenInsight, Crypto Exchange Report Q2 2026, 20/07/2026] |
| Tháng 6/2026 | spot $1.2T (+23% MoM) | tháng đầu vượt $1T kể từ tháng 3 | [CryptoRank.io, 06/07/2026] |

**Diễn giải trung thực**: đây là **đáy chu kỳ, không phải đỉnh**. Derivatives — đúng phân khúc MMW
phục vụ — **đang teo mạnh nhất** ($14.6T → $12.0T, −18% QoQ trong Q2/2026), trong khi spot đang hồi.
Nghĩa là đúng nhóm khách hàng mục tiêu của MMW (futures USDT-M retail) là nhóm rút lui nhanh nhất.

Có một mặt tích cực nghịch lý cần ghi nhận cho công bằng: **thị trường gấu là lúc trader thua nhiều
nhất, và cũng là lúc nhu cầu "kỷ luật" chủ quan cao nhất**. Nhưng đây là nhu cầu *cảm xúc*, không phải
nhu cầu *có ngân sách*. Người vừa mất 45% tài khoản không phải người sẵn sàng mở ví trả $30/tháng.
Bằng chứng gián tiếp: chi tiêu thu hút người dùng (UA) cho ứng dụng tài chính tại APAC giảm 27%,
riêng Việt Nam giảm **48%** [AppsFlyer State of Finance 2026, qua Advertising Vietnam, ~04/2026] —
tức là chính các công ty fintech đang cắt ngân sách vì thị trường không chuyển đổi.

### 1.2 Quy mô thị trường phần mềm giao dịch — dữ liệu vĩ mô có nhưng ít dùng được

| Thị trường | Quy mô | Dự báo | Nguồn |
|---|---|---|---|
| Trading Software (tổng) | $14.7 tỷ (2026) | $38.84 tỷ (2035), CAGR 11.40% | [MarkWide Research, truy cập 29/07/2026] |
| Financial Trading Software | $13.84 tỷ (2025) → $15.38 tỷ (2026) | $29.45 tỷ (2032), CAGR 11.38% | [Global Information Inc./Research and Markets, 13/01/2026] |
| AI Crypto Trading Bot | $185 triệu (2025) | $537.22 triệu (2032), CAGR 16.5% | [PW Consulting, 13/06/2026] |

**Cảnh báo phương pháp luận — phải đọc**: các báo cáo "crypto trading bot market size" mâu thuẫn
nhau đến mức **không dùng được**. Bốn nhà nghiên cứu cho bốn con số cách nhau **hơn 200 lần** cho
cùng một năm:

| Nguồn | Quy mô 2025/2026 | Ngày |
|---|---|---|
| Business Research Insights | **$54.08 tỷ** (2026) → $200.14 tỷ (2035), CAGR 14% | [06/07/2026] |
| Verified Market Reports | **$22.23 tỷ** (2025) → $66.61 tỷ (2033), CAGR 14.8% | [23/05/2026] |
| Grand View Research (automated crypto trading) | $22.2 tỷ (2025) → $25.3 tỷ (2026) → $66.6 tỷ (2033) | [truy cập 29/07/2026] |
| IntelMarketResearch | **$4.331 tỷ** (2025) → $34.839 tỷ (2034), CAGR 35.1% | [~25/07/2026] |
| Market Publishers | **$251.0 triệu** (2025), CAGR 7.60% | [truy cập 29/07/2026] |

Chênh $251 triệu vs $54.08 tỷ = 215 lần. Kết luận: **không dùng bất kỳ con số nào trong nhóm này để
làm cơ sở quyết định**. Chúng đo những thứ khác nhau (có cái đo doanh thu phần mềm, có cái đo giá trị
tài sản được bot quản lý) và phần lớn là báo cáo bán tự động.

**Quy mô thị trường "trading journal software" riêng: KHÔNG TÌM ĐƯỢC DỮ LIỆU CÔNG KHAI.** Không có
nhà nghiên cứu nào tách riêng phân khúc này. Đây là câu trả lời hợp lệ và cũng là một tín hiệu:
phân khúc quá nhỏ để ai đó bỏ tiền nghiên cứu.

### 1.3 Ước lượng bottom-up cho phân khúc thật của MMW *(ước lượng — không phải số công bố)*

Vì không có TAM công bố, dựng ước lượng từ dưới lên bằng số liệu quan sát được:

- CoinMarketMan — journal crypto-native lâu đời nhất — công bố **"13,000+ traders"**
  [coinmarketman.com/pricing, truy cập 29/07/2026].
- Trader Make Money — journal crypto-native đang dẫn đầu — công bố **"170k+ traders"**
  [tradermake.money, truy cập 29/07/2026], nhưng phần lớn ở gói **Free**.
- Cryptohopper — bot, không phải journal — công bố **"hơn 500,000 người dùng"**
  [UseThisAI.fyi, truy cập 29/07/2026].

**Ước lượng (giả định)**: toàn bộ thị trường trader crypto trả tiền cho một journal/psychology tool
chuyên dụng trên toàn cầu ở mức **cỡ 10⁵ người, không phải 10⁶–10⁷**. Với ARPU thực tế $10–25/tháng
(mục 2.4), doanh thu toàn phân khúc *crypto journal* ước chừng **$20–60 triệu/năm toàn cầu**
— một thị trường ngách nhỏ, đã có 6–8 người chơi, đang ở đáy chu kỳ. Con số này là **ước lượng của
người phân tích**, không có nguồn nào công bố nó.

Một thị trường liền kề lớn hơn nhiều, đáng ghi nhận: **crypto prop trading đạt quy mô $20 tỷ trong
2025, chỉ 5–10% trader vượt được vòng đánh giá, và chỉ 7% từng nhận payout; hơn 40 prop firm hiện
cung cấp crypto làm asset class chính** [CryptoFundTrader, 24/12/2025]. Ngành prop nói chung tăng
1,264% từ 12/2015 đến 04/2024 [cùng nguồn]. Ghi chú thẳng: nguồn này là một prop firm tự công bố,
có xung đột lợi ích — coi là chỉ dấu định tính, không phải số liệu kiểm chứng.

### 1.4 Trạng thái trưởng thành của category

Một quan sát định tính có giá trị, từ chính một người bán trong ngành:

> *"Thị trường phần mềm trading journal đã trưởng thành đáng kể. Nơi mà vài năm trước lựa chọn chủ
> yếu là giữa một spreadsheet và Tradervue, trader năm 2026 có nhiều lựa chọn được xây dựng riêng
> — mỗi cái nhấn mạnh một thứ khác nhau về độ sâu analytics, tự động hoá import, tính năng AI,
> **theo dõi tâm lý**, và giá."*
> [thespeculatorsjournal.com, 03/06/2026]

Ba hàm ý cho MMW:

1. **Category đã qua giai đoạn "chưa ai làm".** Cửa sổ first-mover đã đóng khoảng 2–3 năm trước.
2. **"Theo dõi tâm lý" đã được liệt kê như một trục cạnh tranh chuẩn**, ngang hàng với analytics và
   giá — không còn là điểm khác biệt.
3. Mật độ nội dung so sánh cực dày (tìm được **>15 bài "Best trading journal 2026"** chỉ trong một
   truy vấn: tradelog.cot-reports.com, tradereplay.app, traderlog.co, F6S, StockBrokers.com,
   journalplus.co, sureinsight.app, thespeculatorsjournal.com, tradingjournal.com, disciplined.me,
   edrisfinance.com, financialtechwiz.com, pineify.app, traderssecondbrain.com, tradertrac.com…).
   Đây là dấu hiệu kinh điển của một thị trường **bão hoà, cạnh tranh bằng SEO và affiliate**, không
   phải bằng sản phẩm. Chi phí thu hút khách qua kênh nội dung ở category này đã rất cao.

---

## 2. Đối thủ trực tiếp — 18 sản phẩm khảo sát

Chia làm 3 nhóm theo mức độ đe dọa với MMW.

### Nhóm A — Trading journal đa tài sản (đe dọa thấp–trung bình, không crypto-native)

| # | Sản phẩm | URL | Giá thực tế (USD/tháng) | Tính năng lõi | Sàn/broker | AI? | Behavior/psychology? | Quy mô |
|---|---|---|---|---|---|---|---|---|
| 1 | **TradeZella** | tradezella.com | **Essential $35 · Pro $59 · Ultra $99**; annual −25% → $26/$44/$74 [TradeZella blog, 14/07/2026]. Giá cũ 2025–đầu 2026: Basic $29 / Premium $49 [pineify.app, 05/11/2025] | Journal + analytics + trade replay + backtesting + Zella AI (tính theo credit, reset mỗi kỳ) | Broker Mỹ là chính, **crypto hạn chế** [tradermake.money, 07/2026] | ✅ Zella AI + 3 agent | ✅ **Rule Adherence Score** (% lệnh tuân thủ đủ rule) + tag trạng thái cảm xúc tương quan PnL [getmettle.app, 22/06/2026] | Trustpilot 4.8/5, ~860 review [top30forexbrokers, truy cập 29/07/2026]. Founder Umar Ashraf, Dubai [Bullish Bears, 22/04/2026] |
| 2 | **Tradervue** | tradervue.com | Free (30 grouped trade/tháng, chỉ stocks/ETF) · **Silver $29.95** · **Gold $49.95** [StockBrokers.com, 29/10/2025] | Journal lâu đời nhất (từ 2011), tag-profitability report, community/sharing | 80+ broker, **KHÔNG hỗ trợ sàn crypto** [tradermake.money, 07/2026] | ❌ Không có AI | ⚠️ Chỉ tag linh hoạt + báo cáo lợi nhuận theo tag | Bị đánh giá "tính năng 2015 với giá 2026" [traderssecondbrain, 02/04/2026] |
| 3 | **Edgewonk** | edgewonk.com | **$197** một gói duy nhất. Nguồn mâu thuẫn về chu kỳ: "$197 mỗi 16 tháng ≈ $12.32/tháng" [tradingtoolshub, truy cập 29/07/2026] vs "$197/năm ≈ $16.50/tháng, +VAT EU/UK → $230–250" [thefxgeek, 14/07/2026] vs "$169/năm" [daytradingz, 05/06/2026] | Journal thiên tâm lý, edge finder, phân tích chuỗi kỷ luật đối chiếu equity curve | Import thủ công nhiều, giao diện cũ | ❌ | ✅✅ **Tiltmeter** — chấm điểm disciplined/undisciplined cho **từng lệnh**. Được đối thủ Mettle đánh giá là "sâu nhất về lượng hoá kỷ luật" [getmettle.app, 22/06/2026] | "Journal premium rẻ nhất thị trường, và là nền tảng duy nhất có hệ thống theo dõi cảm xúc riêng" [tradingjournal.com, 03/03/2026] |
| 4 | **TraderSync** | tradersync.com | **Pro $29.95 · Premium $49.95 · Elite $79.95**; annual giảm ~45%; trial 7 ngày không cần thẻ [traderssecondbrain, 02/04/2026; tradingsfx, ~27/07/2026] | Journal + Cypher AI + replay, auto-import broker rộng | Broker rộng, **auto-sync sàn crypto mạnh** [tradermake.money, 07/2026] | ✅ Cypher AI | ✅ **Xếp hạng lỗi theo chi phí** (mistake-cost ranking) + tag cảm xúc tuỳ biến [getmettle.app, 22/06/2026] | — |
| 5 | **TradesViz** | tradesviz.com | Free · **Basic $12.99** · **Pro $24.99** [tradingtoolshub, 16/06/2026]. Nguồn khác: Pro $19.99 · Platinum $29.99 [traderssecondbrain, 02/04/2026] | Analytics sâu nhất nhóm, 600+ cách cắt dữ liệu | Broker rộng + **crypto spot/futures/perpetuals/options** (gói trả phí) [tradesviz.com, truy cập 29/07/2026] | ✅ | ✅✅ **Cost-of-emotion pivot grid** — tổng PnL theo tag tâm lý (FOMO, revenge, hesitation, oversize), có ghi nhận sleep/stress/mood trước lệnh [getmettle.app, 22/06/2026] | — |
| 6 | **Trademetria** | trademetria.com | Free tier (giới hạn 30 lệnh/tháng) [tradingtoolshub compare, truy cập 29/07/2026]; giá gói trả phí không xác minh được | Analytics gắn chặt với tag | — | ❌ | ✅ Tag cảm xúc/lỗi cạnh analytics | — |

### Nhóm B — Journal crypto-native (đe dọa CAO — cùng thị trường, cùng sàn)

| # | Sản phẩm | URL | Giá thực tế | Tính năng lõi | Sàn | AI? | Behavior? | Quy mô |
|---|---|---|---|---|---|---|---|---|
| 7 | **Trader Make Money (TMM)** | tradermake.money | **Free · Novice+ $6/th · Trader $12/th · PRO Trader $22/th** (billing annually) [tradermake.money/prices, truy cập 29/07/2026] | Journal crypto-native, **funding & fee-aware PnL** (PnL thật trên perp), 60–90+ widget, Voice Notes AI, hồ sơ công khai được sàn xác thực | **10 sàn**: Binance, Bybit, OKX, Bitget, Gate, MEXC, KuCoin, **Hyperliquid**, BingX, Aster | ✅ **AI Coach** — "review lịch sử và gắn cờ các lỗ hổng hành vi như **oversizing after a loss**" [tradermake.money, truy cập 29/07/2026] | ✅ Trực tiếp cạnh tranh detector `OversizedAfterLoss` của MMW | **170k+ traders** [tradermake.money, truy cập 29/07/2026] |
| 8 | **CoinMarketMan (CMM)** | coinmarketman.com | Basic **Free** (1 sàn) · **PRO $699.99/năm** (≈$58/th) · **ENTERPRISE $899.99/năm** (≈$75/th). **Quan trọng: "CMM UNLOCKED" = miễn phí trọn đời qua link giới thiệu sàn; "90% người dùng CMM đang ở UNLOCKED"** [coinmarketman.com/pricing, truy cập 29/07/2026] | Journal tự động + analytics derivatives + Verification Page + HyperTracker | Sàn crypto (portfolio sync là thế mạnh lõi) | ❌ Không có AI coach [tradermake.money, 07/2026] | ⚠️ Có cảnh báo "đang dùng risk quá cao" + average R + account risk [coinmarketman.com/features, truy cập 29/07/2026] — nhưng không phải behavior detection | **13,000+ traders** [coinmarketman.com, truy cập 29/07/2026] |

> **Đây là phát hiện quan trọng nhất về mô hình kinh doanh trong toàn báo cáo.**
> CoinMarketMan — journal crypto-native lâu đời nhất — **không sống bằng subscription**. 90% người
> dùng ở gói UNLOCKED miễn phí, đổi lại họ đăng ký sàn qua affiliate link của CMM và CMM ăn rebate
> phí giao dịch. Nghĩa là **giá sàn của phân khúc journal crypto không phải $6, mà là $0**, được
> tài trợ bởi ví của sàn. Bất kỳ ai định bán subscription cho trader crypto phải cạnh tranh với 0đ.

### Nhóm C — Bot / terminal giao dịch tự động (đe dọa gián tiếp — chiếm ngân sách, không chiếm nhu cầu)

| # | Sản phẩm | URL | Giá thực tế | Sàn | AI? | Behavior? | Quy mô |
|---|---|---|---|---|---|---|---|
| 9 | **3Commas** | 3commas.io | Free = paper only · **Starter $20 · Pro $50 · Expert $140** + Custom [ComparEdge, 08/07/2026]. G2 ghi dải $15–$110 [G2, 09/04/2026] — mâu thuẫn, đã đổi gói 10/2025 [3commas blog, 28/10/2025] | 22+ sàn [ai-trading-ranked, 03/2026] | ⚠️ | ❌ | — |
| 10 | **Cryptohopper** | cryptohopper.com | **Mâu thuẫn nguồn**: Explorer/Adventurer/Hero **$29–$129** [ComparEdge, 17/07/2026] vs Explorer $19 · Adventurer $49 · Hero $99 + Pioneer free paper-only [uwuu.ai, 08/05/2026] | 10+ sàn | ✅ | ❌ | **500,000+ người dùng** [UseThisAI.fyi, truy cập 29/07/2026] |
| 11 | **Coinrule** | coinrule.com | Starter free · **Hobbyist $39.99** · **Trader $79.99** · **Pro $499.99**/tháng; annual $29.99/$59.99/$449.99 [defenderbot, 11/04/2026]. Nguồn khác: Trader $59.99/th billed $719/năm, Pro $249.99 [walletreviewer + jonathonspire, 13/04/2026] | 30+ sàn | ❌ (rule-based no-code) | ❌ | — |
| 12 | **Altrady** | altrady.com | **Basic $32 · Essential $57 · Premium $103**/tháng; annual $23/$40/$72 [ComparEdge, 08/07/2026]. Bản EUR: €28–€90/tháng [gncrypto.news, 22/04/2026; Finestel, 09/06/2026] | 15+ sàn | ⚠️ | ❌ | — |

### Nhóm D — **Psychology-first / tilt detection (ĐE DOẠ CHIẾN LƯỢC CAO NHẤT)**

Đây là nhóm phá vỡ giả định "chưa ai lấy behavior làm core".

| # | Sản phẩm | URL | Giá | Cách tiếp cận | Behavior depth | Quy mô |
|---|---|---|---|---|---|---|
| 13 | **TiltGuard** | tiltguard.app | **Thanh toán một lần, truy cập trọn đời** (không công bố số tiền trên trang đã đọc) [tiltguard.app, truy cập 29/07/2026] | Chrome extension cho **TradingView**. Tự mô tả: *"Rule enforcement infrastructure. It is not a signal service. It does not predict markets. **It enforces rules.**"* | ✅✅✅ **Hard Daily Loss Enforcement** (khoá phiên tức thì khi chạm ngưỡng lỗ ngày) · **Trade Count Protection** (khoá phiên khi đủ số lệnh) · **Cool-Down Lock** (bắt buộc nghỉ sau lệnh thua) · **Non-Override Protection Mode** (rule đặt lúc bình tĩnh KHÔNG thể bị bypass lúc tilt). Nhắm prop firm evaluation. | Caughman Group LLC |
| 14 | **Zero Tilt** | zerotilt.io | Trial 7 ngày; **giá không công khai** — không tìm được dữ liệu công khai | App psychology-first: Panic Button (box breathing + journal prompt + distraction game), Urge Tracker, streak/goal, clan/leaderboard | ✅✅ Log "urge to tilt" theo thời gian thực, phân tích trigger theo thời điểm/ngưỡng lỗ/điều kiện thị trường | Tự công bố: **12,000+ users · 4.9★ · 10M+ urges blocked · 150+ quốc gia** [zerotilt.io, truy cập 29/07/2026] |
| 15 | **A-Trader / arizet "Trading Psychology"** | arizet.com | Trial 14 ngày; **giá không công khai** — không tìm được dữ liệu công khai | **Sản phẩm sâu nhất về behavior trong toàn khảo sát.** Theo dõi 4 "behavioral regime" (Calm / Flow / Warning / Tilt) theo thời gian thực, **SDK-callable** | ✅✅✅ **22 behavioral signals**, bao gồm: decision latency · position-size variance · **stop-moving frequency** · **revenge-trade index** · loss-recovery time · idle-restraint score. Có "mental-state attribution" cho từng lệnh và "decision quality" chấm điểm A− so với baseline. | Tự công bố "live data của hàng nghìn người dùng A-Trader" [arizet.com, truy cập 29/07/2026] |
| 16 | **Mettle** | getmettle.app | **Free to start, không cần thẻ** (gói Journal/Apprentice). Trader & Master "coming soon" [getmettle.app, truy cập 29/07/2026] | Self-report-first: tag emotion/mistake, chấm điểm execution, AI "Cass" phản chiếu pattern. Behavioral tag là **trung tâm sản phẩm**, không phải add-on | ✅✅ Nhưng tự thừa nhận: "phụ thuộc hoàn toàn vào self-report; quantified emotion analytics chưa bằng Edgewonk hay TradesViz" | Mới, founder Daniel Kapadia |
| 17 | **Plancana** | plancana.com | Free download; **giá premium không công bố** [getmettle.app, 22/06/2026 xác nhận "pricing not public"] | Mobile-first: mood diary trước/sau lệnh, AI phân tích pattern cảm xúc, **guardrail rules** kiểu "dừng sau 2 lệnh thua" | ✅✅ Guardrail rule + AI tóm tắt "bạn phá rule bao nhiêu lần sau chuỗi thua" | Tự công bố 4.7★ App Store / 4.8★ Google Play [plancana.com, 12/03/2026] |
| 18 | Nhóm dài đuôi (chưa nghiên cứu sâu) | — | — | **Tilt Proof AI Trading Journal** (App Store) · **TradeMaxxing** (iOS/Android) · **TradeMindset** (trademindset.co) · **Tradefulness** (mindfulness-first) · **TILT / tradelensapp.com** · **StonkJournal** (free) · **Disciplined.me** · **Traders Second Brain** · **tradeplanner.ai** ("AI trading coach catches tilt before you do") | Tất cả đều tự định vị quanh tilt/discipline/emotion | Xuất hiện dày đặc trong kết quả tìm kiếm 2026 → **đây là một category đang bùng nổ, không phải khoảng trống** |

### 2.4 Giá thị trường — bảng neo giá

| Mức giá | Ai đang ở đó |
|---|---|
| **$0 trọn đời** | CoinMarketMan UNLOCKED (affiliate rebate) · TMM Novice · TradesViz Free · Trademetria Free · StonkJournal · Mettle · Plancana · Tradervue Free |
| **$6–$13/tháng** | TMM Novice+ $6 · TradesViz Basic $12.99 · TMM Trader $12 · Edgewonk ≈$12–16 |
| **$20–$35/tháng** | TMM PRO $22 · TradesViz Pro $24.99 · Tradervue Silver $29.95 · TraderSync Pro $29.95 · TradeZella Essential $35 |
| **$50–$100/tháng** | TradeZella Pro $59 / Ultra $99 · CMM PRO $58 / ENTERPRISE $75 · TraderSync Elite $79.95 · Cryptohopper Hero $99–129 |
| **Mua đứt** | TiltGuard (one-time, lifetime) · Edgewonk $197/chu kỳ |

**Hàm ý cho MMW**: nếu định bán, dải giá khả thi là **$9–19/tháng**. Trên $25 phải cạnh tranh trực
diện với TradeZella/TraderSync đã có brand, community, education engine và hàng trăm review. Dưới $9
không đủ bù chi phí LLM + hạ tầng + support cho một người làm một mình.

### 2.5 Bản đồ định vị — hai trục

Trục ngang: **hồi cứu (sau khi lệnh đã xong) → thời gian thực (trước khi lệnh vào)**
Trục dọc: **phân tích số liệu → can thiệp hành vi**

```
                    CAN THIỆP HÀNH VI (chặn / khoá / buộc dừng)
                                    ▲
                                    │
                   Zero Tilt ●      │      ● TiltGuard        ┌──────────────┐
                   (panic button)   │      (khoá phiên,       │  ◆ MMW       │
                                    │       tầng trình duyệt)  │ chặn ở tầng  │
                   Plancana ●       │                          │  API sàn     │
                   (guardrail rule) │      ● A-Trader          └──────────────┘
                                    │      (22 tín hiệu,             ▲
                   Mettle ●         │       SDK, realtime)     KHÔNG AI Ở ĐÂY
                   (self-report)    │
  ──────────────────────────────────┼──────────────────────────────────────────►
   HỒI CỨU                          │                        THỜI GIAN THỰC
                   ● Edgewonk       │      ● 3Commas / Coinrule
                   (Tiltmeter)      │        Cryptohopper / Altrady
                                    │        (đặt lệnh, nhưng KHÔNG có
   ● Tradervue                      │         rule kỷ luật cá nhân hoá)
   ● Trademetria    ● TradesViz     │
   ● TradeZella     ● TraderSync    │
   ● CoinMarketMan  ● TMM           │
                                    ▼
                        PHÂN TÍCH SỐ LIỆU (báo cáo / tag / thống kê)
```

Góc phần tư **trên-phải** — *can thiệp hành vi, thời gian thực, ở tầng thực thi thật* — chỉ có
MMW. TiltGuard ở gần nhưng thấp hơn một tầng (trình duyệt, không phải sàn). A-Trader sâu hơn về
tín hiệu nhưng dừng ở "gợi ý hạn chế", không chặn.

Góc **dưới-trái** đông đúc đến mức bão hoà: 9 sản phẩm cạnh tranh nhau bằng giá và độ sâu analytics.
**Nếu MMW đi tiếp bằng cách thêm biểu đồ, thêm báo cáo, thêm tag — nó rơi vào góc này và thua.**

### 2.6 Đánh giá mức độ đe doạ với MMW

| Đối thủ | Đe doạ | Lý do |
|---|---|---|
| **Trader Make Money** | 🔴 **Cao nhất** | Crypto-native, 10 sàn, 170k user, $6–22/tháng, AI Coach đã bắt đúng `OversizedAfterLoss`, tặng MCP server miễn phí. Chiếm sạch phân khúc "journal crypto có AI" ở mức giá MMW không thể xuống được. |
| **CoinMarketMan** | 🔴 Cao | Không cạnh tranh bằng feature mà bằng **mô hình giá $0** (affiliate rebate). Làm cho toàn bộ phân khúc không thể bán subscription. |
| **A-Trader (arizet)** | 🟠 Trung bình–cao | Sâu hơn MMW về behavior (22 vs 3 tín hiệu), có SDK. Nếu họ thêm execution gating, khoảng trống của MMW biến mất. Chưa rõ quy mô và giá. |
| **TiltGuard** | 🟠 Trung bình | Cùng luận điểm định vị chính xác ("It enforces rules"), cùng nhóm khách (prop). Nhưng tự giới hạn ở tầng trình duyệt + mua đứt → không có động lực đi sâu. |
| **TradeZella** | 🟡 Trung bình–thấp | Mạnh nhất về brand/content nhưng **crypto hạn chế**. Nếu họ nghiêm túc vào crypto thì đổi thành cao. |
| **TraderSync / TradesViz** | 🟡 Thấp–trung bình | Behavior tốt (mistake-cost, cost-of-emotion) nhưng multi-asset, không crypto-first. |
| **Zero Tilt / Plancana / Mettle** | 🟢 Thấp | Cùng thị trường nhu cầu nhưng khác tầng: self-report/mobile/mindfulness, không chạm sàn. Có thể **cùng tồn tại** với MMW. |
| **3Commas / Cryptohopper / Coinrule / Altrady** | 🟢 Thấp | Khác nhu cầu (tự động hoá chiến lược ≠ kỷ luật). Nhưng **cạnh tranh ngân sách**: trader đã trả $20–50 cho bot khó trả thêm cho MMW. |
| **Edgewonk / Tradervue / Trademetria** | 🟢 Thấp | Không crypto. Edgewonk chỉ nguy hiểm ở mặt *narrative* — nó đã sở hữu định vị "journal tâm lý" trong đầu người mua từ nhiều năm. |

---

## 3. Khoảng trống thị trường — cái gì MMW đang làm mà chưa ai làm tốt

### 3.1 Kiểm tra lại giả định gốc — và bác bỏ nó

Giả định trong tài liệu MMW: *"khác biệt cốt lõi của sản phẩm so với một journal thông thường"* là
behavior detection (`specs/001-mmw-system-baseline/spec.md:54`).

**Giả định này không còn đúng tại 07/2026.** Bảng đối chiếu 3 detector của MMW với thị trường:

| Detector MMW | Ai đã có tương đương | Độ sâu so với MMW |
|---|---|---|
| `RevengeTrade` (vào lệnh trong N phút sau cắt lỗ) | A-Trader (**revenge-trade index**) · TiltGuard (**Cool-Down Lock** bắt buộc, không chỉ cảnh báo) · Zero Tilt (Panic Button) · Plancana (guardrail rule) · TradesViz (tag revenge + chi phí USD) | **Nhiều đối thủ sâu hơn.** TiltGuard *chặn*, MMW chỉ *gắn cờ*. A-Trader đo bằng index liên tục, MMW đo bằng ngưỡng nhị phân 30 phút. |
| `LossStreak` (chuỗi thua ≥ ngưỡng) | Edgewonk (**discipline streak** đối chiếu equity curve) · TiltGuard (Hard Daily Loss Enforcement) · A-Trader (loss-recovery time) · Plancana ("phá rule bao nhiêu lần sau chuỗi thua") | **Ngang bằng hoặc thua.** Đây là feature phổ biến nhất. |
| `OversizedAfterLoss` (size tăng >50% sau lệnh thua) | **TMM AI Coach — mô tả nguyên văn: "flags behavioral leaks like oversizing after a loss"** [tradermake.money, truy cập 29/07/2026] · A-Trader (**position-size variance**) · TradesViz (tag "oversize" + chi phí USD) | **Trùng khớp 1-1 với TMM**, sản phẩm crypto-native lớn nhất phân khúc (170k users, $6/tháng). |

**MMW không có detector nào mà thị trường chưa có.** Ngược lại, thị trường có ít nhất 4 tín hiệu
hành vi mà MMW **chưa** có: `decision latency` (thời gian do dự trước khi bấm lệnh),
`stop-moving frequency` (tần suất dời stop — dấu hiệu tilt kinh điển), `idle-restraint score`
(khả năng ngồi yên không vào lệnh), `mental-state attribution per trade` [A-Trader, arizet.com,
truy cập 29/07/2026].

### 3.2 Khoảng trống THẬT SỰ còn lại — hẹp nhưng có thật

Sau khi loại bỏ những gì đã bị chiếm, còn đúng **một** thứ MMW làm mà không ai trong 18 sản phẩm làm:

> **Chặn cứng một lệnh futures crypto ở tầng API sàn — trước khi lệnh chạm sàn — dựa trên đồng thời
> rule rủi ro cá nhân hoá + trạng thái hành vi, với audit trail đầy đủ.**

Bằng chứng rằng khoảng trống này thật:

- **TiltGuard tự loại mình khỏi tầng này**, nói rõ trên trang tính năng:
  *"It does not place trades. It does not access brokerage funds. It does not modify order execution.
  It monitors session activity and enforces predefined limits locally within the browser environment."*
  [tiltguard.app/features, truy cập 29/07/2026]. Tức là TiltGuard khoá **giao diện TradingView**, không
  khoá **sàn**. Trader mở app Binance trên điện thoại là vô hiệu hoá toàn bộ.
- **Toàn bộ nhóm journal (A + B) đều là hồi cứu.** Tradervue được mô tả chính xác:
  *"reviews trades after execution rather than placing them"* [findmymoat.com, 11/07/2026].
- **Nhóm bot (C) đặt lệnh nhưng không có rule kỷ luật cá nhân hoá** — chúng thực thi chiến lược,
  không phải chặn chủ nhân.
- **Prop firm chặn ở tầng tài khoản**, nhưng chỉ áp dụng khi bạn giao dịch vốn của họ, và tỷ lệ
  vượt vòng đánh giá chỉ 5–10% [CryptoFundTrader, 24/12/2025]. Không dùng được cho vốn tự có.

Đây chính xác là `LiveOrderService.cs` (547 dòng) với ~13 lớp chặn tuần tự, cộng FR-026 → FR-033
trong `specs/001-mmw-system-baseline/spec.md:216-223`.

### 3.3 Nhưng phải nói thẳng ba điều làm khoảng trống này khó khai thác

1. **Nó đòi API key có quyền đặt lệnh futures.** Đây là rào cản niềm tin cao nhất trong toàn ngành.
   TiltGuard bán được chính vì nó **không** chạm khoá. Một sản phẩm của một dev cá nhân, chưa audit,
   yêu cầu khoá đặt lệnh futures — tỷ lệ chuyển đổi sẽ rất thấp. Và hiện tại MMW **chưa đủ điều kiện
   để xin**: `src/MMW.Domain/Entities/TradingAccount.cs:29-34` lưu `ApiKey`/`ApiSecret` dạng
   `string?` plaintext trong SQL Server, dù chính comment ở dòng 29 ghi *"Lưu bằng User Secrets hoặc
   encrypted"*. Đây không phải nợ kỹ thuật, đây là **blocker thương mại tuyệt đối**. Không được nhận
   khoá của người thứ hai cho đến khi sửa xong.

2. **Nó chỉ có giá trị khi trader dùng đúng một đường vào lệnh.** MMW chặn được lệnh do MMW gửi.
   Nó không chặn được trader mở app Binance đặt tay. Không có sản phẩm nào chặn được — trừ khi tích
   hợp ở tầng sub-account với quyền hạn chế, thứ Binance không cung cấp cho retail. Đây là giới hạn
   cấu trúc, không phải giới hạn của MMW.

3. **Nó chưa được test.** Phụ lục B mục 5 của spec ghi rõ: *"Chưa có unit test cho luồng preflight,
   advisor AI và các lớp chặn của live order — vùng rủi ro cao nhất lại phủ test thấp nhất"*
   (`specs/001-mmw-system-baseline/spec.md:363`). Bán một sản phẩm mà giá trị bán được là "chặn lệnh
   sai" trong khi chính lớp chặn không có test là rủi ro trách nhiệm pháp lý trực tiếp.

### 3.4 Ba khoảng trống phụ, nhỏ hơn nhưng thật

- **Chưa journal nào tính vốn đầu ngày chính xác cho perp có funding.** TMM quảng cáo
  "funding- and fee-aware PnL" như điểm khác biệt [tradermake.money, 07/2026] — nghĩa là các sản
  phẩm khác vẫn sai. MMW cũng đang sai theo cách khác (Phụ lục B mục 3: vốn đầu ngày đang *ước lượng*
  = số dư hiện tại − PnL trong ngày, `specs/001-mmw-system-baseline/spec.md:361`).
- **Cảnh báo tin vĩ mô gắn vào quyết định vào lệnh** (FR-038, FR-039) — không sản phẩm nào trong
  18 cái khảo sát có. Nhưng đây là feature, không phải sản phẩm.
- **Tiếng Việt bản địa cho futures USDT-M.** Không sản phẩm nào có. Xem mục 4 để hiểu vì sao điều
  này không cứu được business case.

### 3.5 Vấn đề cấu trúc của cả category: retention

Đây là điều không nhà cung cấp nào nói ra, nhưng lộ rõ qua chính tài liệu của họ.

Bài đánh giá của Plancana đặt "Friction" thành một tiêu chí chấm điểm chính thức, với câu hỏi:
*"Will a busy trader actually use this daily, or **abandon it after two weeks**?"*
[plancana.com, 12/03/2026]. Mettle thừa nhận tương tự: *"Log inconsistently and the insight degrades
— the same caveat that applies to Edgewonk's Tiltmeter, Chartlog, and every self-report tool here"*
[getmettle.app, 22/06/2026].

Nói thẳng: **journal có tỷ lệ bỏ dùng rất cao vì nó đòi công sức mỗi ngày để đổi lấy giá trị mơ hồ
sau vài tháng.** Và nó bị bỏ nhanh nhất đúng vào lúc trader thua — lúc mà giá trị lẽ ra cao nhất.
Đây là lý do các sản phẩm chuyển sang **auto-import** (giảm friction) và **AI coach** (giảm công sức
diễn giải).

**Điều này lại là lợi thế cấu trúc của MMW**, và đáng ghi nhận rõ: MMW không đòi trader ghi chép để
tạo giá trị. Rule engine chấm tự động khi lưu lệnh (SC-001), behavior detector chạy tự động, live
order gating chạy dù trader có muốn hay không, và `trade-result-sync` tự đóng lệnh từ fill trên sàn.
Giá trị được tạo ra **kể cả khi người dùng không làm gì** — đó là mô hình retention mạnh hơn hẳn
self-report.

Nhưng lợi thế này chỉ hiện thực hoá được nếu MMW thực sự cắm vào đường vào lệnh của trader. Nếu
trader vẫn đặt lệnh tay trên app Binance và chỉ dùng MMW để xem lại, MMW tụt về đúng góc dưới-trái
của bản đồ định vị (mục 2.5) và mang toàn bộ vấn đề retention của category.

---

## 4. Thị trường Việt Nam

### 4.1 Quy mô — và vì sao con số ai cũng trích là sai

Con số được trích trong mọi deck: *"Việt Nam xếp thứ 4 toàn cầu về crypto adoption (Chainalysis 2025),
17–21 triệu người sở hữu crypto (~17–20% dân số)"* [Disruption Banking, 06/11/2025; sanvietnam.com,
06/06/2026 dẫn Chainalysis 2025 Global Crypto Adoption Index].

**Con số này không dùng được để tính TAM.** Phân tích phương pháp luận chi tiết nhất tìm được
[Alex's Substack, "Vietnam's 21 Million Crypto Users Is the Wrong Number to Build On", 21/05/2026]:

- Nguồn gốc "21 triệu" là **Triple-A**, một payment gateway Singapore. Con số công bố hiện tại của
  họ là **18.6 triệu**, không phải 21 triệu.
- Phương pháp: lấy % suy ra từ Chainalysis Geography Index + khảo sát Statista/Finder, **nhân thẳng
  với dân số ~100 triệu, bao gồm cả trẻ em**.
- Triple-A đo *"anyone who has ever owned cryptocurrency"* — bao gồm người mua AXS năm 2021 rồi
  không quay lại.
- **Sau khi hiệu chỉnh wallet inflation và tài khoản không hoạt động, số nhà đầu tư crypto hoạt động
  duy nhất tại Việt Nam ước tính ≈ 3 triệu người.**

Ba ước tính quy mô thị trường VN 2025, chênh nhau 12 lần, đo ba thứ khác nhau [cùng nguồn]:

| Lớp | Con số | Nguồn gốc | Thực chất đo gì |
|---|---|---|---|
| Transaction volume | **$220–230 tỷ** | Chainalysis (12 tháng tới 06/2025) | **Throughput có đòn bẩy.** 90% volume crypto toàn cầu là derivatives [CoinGlass qua Cointelegraph, Q1/2025]. $10k margin ×10x = $100k "volume". Không phải tài sản. |
| Nominal holdings | **$100 tỷ** | Dragon Capital (Đặng Nguyệt Minh) | Số dư danh nghĩa trên sàn nước ngoài. **Không công bố phương pháp.** |
| Verifiable holdings | **$18 tỷ** | PwC Vietnam, báo cáo 12/2025 | Ước tính bảo thủ nhất, nguồn dữ liệu minh bạch nhất (CoinGecko, Statista, Euromonitor, Fitch, Fiin, Coinbase). PwC dự phóng $48–109 tỷ vào 2030. |

Ghi chú trung thực về nguồn: bài phân tích này là của một cá nhân trên Substack, không phải tổ chức
nghiên cứu. Nhưng nó **truy nguyên và trích dẫn được từng nguồn gốc**, trong khi các con số nó phản
biện thì không. Dùng nó như hiệu chỉnh phương pháp luận, không phải như số liệu chính thức.

**Ước lượng thị trường mục tiêu thật của MMW tại VN (ước lượng — không có nguồn công bố):**

```
~3.0 triệu   nhà đầu tư crypto hoạt động tại VN                [Alex's Substack, 21/05/2026]
× ~10-15%    tỷ lệ giao dịch futures/derivatives có kỷ luật đủ để cần journal   (giả định)
= ~300-450k  trader futures VN
× ~2-5%      tỷ lệ từng trả tiền cho công cụ giao dịch          (giả định, xem 4.2)
= ~6,000-22,000 người
× ~1-3%      thị phần khả dĩ cho một sản phẩm mới của dev đơn lẻ, không marketing  (giả định)
= 60-660 khách hàng tiềm năng tối đa
× $10-15/tháng
= $600-10,000 doanh thu/tháng ở kịch bản lạc quan nhất, sau 2-3 năm xây dựng
```

Mọi hệ số nhân sau dòng đầu tiên là **giả định của người phân tích**, không có nguồn. Nhưng ngay cả
khi mỗi giả định lệch 2 lần theo hướng có lợi, kết quả vẫn nằm dưới ngưỡng một mức lương .NET dev
tại Việt Nam.

### 4.2 Khả năng chi trả thực tế

| Chỉ số | Giá trị | Nguồn |
|---|---|---|
| Thu nhập bình quân lao động Q2/2026 | **9.0 triệu VND/tháng** (≈$340) | [Cục Thống kê, Báo cáo KT-XH Q2/2026, qua Thư Viện Pháp Luật, 04/07/2026] |
| Lao động làm công hưởng lương, 6T/2026 | **10.0 triệu VND/tháng** (≈$380) | [cùng nguồn] |
| Khu vực thành thị (làm công) | **11.1 triệu VND/tháng** (≈$420) | [cùng nguồn] |
| GDP bình quân đầu người 2026 (mục tiêu) | **5,400–5,500 USD/năm** | [Báo Chính phủ, 20/10/2025; Nghị quyết 244/2025/QH15] |
| Chi tiêu UA ứng dụng tài chính VN 2026 | **giảm 48%** (APAC giảm 27%) | [AppsFlyer State of Finance 2026, qua Advertising Vietnam, ~04/2026] |

**Diễn giải**: $15/tháng = ~395,000 VND = **~4% thu nhập tháng của lao động thành thị**. Để so sánh,
$15/tháng ở Mỹ với thu nhập trung vị là ~0.3%. Nghĩa là **cùng một mức giá, gánh nặng tương đối ở
VN nặng hơn ~13 lần**. Trader crypto VN có thu nhập cao hơn trung bình (giả định hợp lý nhưng
**không có số liệu**), nhưng khoảng cách này không biến mất.

Thói quen trả phí phần mềm: **không tìm được dữ liệu công khai đáng tin** về tỷ lệ trader crypto VN
trả phí cho công cụ giao dịch. Các nguồn tìm được (data.stateglobe.com và tương tự) có dấu hiệu là
content farm sinh tự động, **không được dùng làm căn cứ**. Đây là một khoảng trống dữ liệu thật.

### 4.3 Kênh phân phối

Kênh tồn tại và hoạt động sôi nổi:

- **Telegram**: "Cộng Đồng Trader Việt Nam" 2,228 thành viên [t.me/congdongtradevietnam, truy cập
  29/07/2026] · TraderViet [t.me/tradervietnet] · nhiều nhóm signal VIP · VERTEX (vertexgroups.net)
  bán "tín hiệu Smart Money" + signal Telegram VIP
- **Facebook Group**: "BTA — Cộng đồng Trader Crypto Việt Nam" [facebook.com/groups/cryptoblockchainvietnam]
- **Zalo**: tồn tại nhưng không đo được công khai

**Vấn đề của kênh này, nói thẳng**: nội dung chủ đạo là **bán tín hiệu**, không phải bán công cụ kỷ
luật. Ví dụ nguyên văn từ một nhóm: *"Group Signals VIP ⭐️ 3-6 signals per day ⭐️ 3 goals per
transaction ⭐️ **80-90% chance of winning** ⭐️ 2000 pips per month"* [t.me/congdongtradevietnam,
truy cập 29/07/2026].

Đây là **xung đột định vị trực diện**. MMW bán "hệ thống không hứa tín hiệu thắng; nó hứa CHẶN lệnh
sai kỷ luật" (`specs/001-mmw-system-baseline/spec.md:23`). Kênh phân phối duy nhất có sẵn ở VN đang
bán chính xác thứ ngược lại, với lời hứa 80–90% win rate. Bán một sản phẩm chống-hưng-phấn vào một
kênh sống bằng hưng phấn là bài toán go-to-market rất khó, không phải bài toán sản phẩm.

Đường qua KOL cũng vướng: KOL Việt kiếm tiền chủ yếu bằng **affiliate sàn** (rebate phí giao dịch).
Một công cụ khuyến khích giao dịch **ít hơn và nhỏ hơn** làm giảm chính doanh thu của KOL. Nghịch
động lực này là cấu trúc, không thương lượng được. (Đối chiếu: CoinMarketMan giải quyết bằng cách
*trở thành* affiliate — mục 2, nhóm B.)

### 4.4 Kinh tế đơn vị — kiểm tra nhanh, và nó không qua

Giả sử bỏ qua toàn bộ rủi ro pháp lý và giả sử bán được. Bài toán một dev đơn lẻ, giá $12/tháng
(bằng gói Trader của TMM):

| Khoản | Ước tính/tháng/khách | Ghi chú |
|---|---|---|
| Doanh thu gộp | **$12.00** | Giả định giữ được ngang giá TMM |
| Phí thanh toán quốc tế (~5% với thẻ VN/Paddle) | −$0.60 | (giả định) |
| Chi phí LLM | −$0.50 đến −$3.00 | MMW gọi AI ở `market-scan` (5 phút/lần), `trade-advisor` (1 phút/lần), preflight vòng 2. Với 5 job × 24h thì số lần gọi/ngày là hàng trăm. **Đây là kiến trúc tốn LLM bất thường so với journal hồi cứu.** MiniMax/DeepSeek rẻ, nhưng chi phí tỷ lệ thuận với số lệnh và số symbol theo dõi. (ước lượng) |
| Hạ tầng (SQL Server + Hangfire + app) | −$1.00 đến −$3.00 | SQL Server không rẻ như Postgres; Hangfire dùng chung DB làm tăng tải. (ước lượng) |
| Dữ liệu tin vĩ mô | −$0 đến −$2.00 | Hiện provider mặc định là noop; muốn thật phải mua feed. (ước lượng) |
| **Biên gộp còn lại** | **≈ $3.50–$9.90** | |

Với biên $3.50–9.90/khách/tháng, cần **~250–700 khách trả tiền liên tục** để đạt mức thu nhập ngang
một .NET dev tại VN, chưa tính công sức bỏ ra. Đối chiếu với ước lượng ở mục 4.1 (tối đa 60–660
khách tiềm năng trong kịch bản lạc quan nhất, sau 2–3 năm), con số này nằm **ở hoặc trên trần của
thị trường khả dĩ**.

Ghi rõ: mọi con số chi phí ở trên là **ước lượng của người phân tích**, không có nguồn công bố. Nhưng
kết luận không nhạy với sai số: kể cả nếu chi phí bằng 0, vẫn cần 100+ khách trả tiền, và bài toán
vẫn là **thu hút khách**, không phải biên lợi nhuận. Với 0 khách hiện tại, 0 kênh phân phối, và một
kênh duy nhất có sẵn (Telegram/Facebook VN) đang bán thứ ngược lại, đây là bài toán chưa có lời giải.

**Một hệ quả kiến trúc đáng lưu ý**: nếu có ngày thương mại hoá, tần suất job hiện tại (`market-scan`
5 phút, `trade-advisor` 1 phút — `specs/001-mmw-system-baseline/spec.md:319-321`) phải được thiết kế
lại theo hướng event-driven, nếu không chi phí LLM sẽ tăng tuyến tính theo số khách hàng và ăn hết
biên. Đây là quyết định nên cân nhắc **trước** khi tối ưu thêm feature.

---

## 5. Rào cản pháp lý

### 5.1 Việt Nam — đây là rủi ro cấp tồn tại, không phải rủi ro biên

**Dòng thời gian đã xác minh:**

| Ngày | Sự kiện | Nguồn |
|---|---|---|
| 09/09/2025 | **Nghị quyết 05/2025/NQ-CP** ban hành, thí điểm thị trường tài sản mã hoá **5 năm** | [Chính phủ, vanban.chinhphu.vn; Dentons LuatViet, 22/10/2025] |
| 01/01/2026 | **Luật Công nghiệp Công nghệ số** hiệu lực — lần đầu công nhận tài sản số là **tài sản hợp pháp**; crypto vẫn **cấm** làm phương tiện thanh toán | [Thư Viện Pháp Luật, 17/07/2025; sanvietnam.com, 06/06/2026] |
| 20/01/2026 | Bộ Tài chính + UBCKNN **mở cổng nhận hồ sơ** cấp phép sàn | [Doanh nghiệp Việt Nam, 21/01/2026] |
| 05/02/2026 | **7 hồ sơ** đã nộp | [Thanh Niên, 05/02/2026] |
| 27/03/2026 | **Thông tư 32/2026/TT-BTC** — hướng dẫn thuế GTGT/TNDN/TNCN cho tài sản mã hoá, **thuế 0.1% mỗi giao dịch chuyển nhượng**, hiệu lực ngay 27/03/2026 | [Báo Chính phủ, 01/04/2026; Thư Viện Pháp Luật; dff.vn, 30/03/2026] |
| 06/05/2026 | Bộ Tài chính gửi công văn cho **5 doanh nghiệp có hồ sơ đầy đủ hợp lệ**: Sàn GD TSMH **VIX** · **Dịch vụ Tài sản số Việt Nam** · Sàn GD TSMH **Việt Nam Thịnh Vượng** · Sàn GD TSMH **Lộc Phát Việt Nam** · Sàn GD TSMH **Techcom**. **CHƯA CẤP PHÉP** — còn yêu cầu bổ sung tài liệu theo khoản 3, 4, 7, 8 Điều 9 NQ05 | [VnEconomy, 06/05/2026] |
| 01/07/2026 | Luật Thuế TNCN sửa đổi (áp thuế 0.1% chuyển nhượng tài sản số) hiệu lực | [Dân trí, 11/12/2025] |
| 06/06/2026 | **Chưa có sàn nội địa nào hoạt động.** Dự kiến cấp phép ~**Q3/2026** | [sanvietnam.com, 06/06/2026, dẫn CoinDesk 17/03/2026 + Reuters] |

**Điều khoản nguy hiểm nhất, trích từ phân tích của Dentons LuatViet về NQ05:**

> *"sau thời hạn **6 tháng** kể từ khi tổ chức cung cấp dịch vụ tài sản mã hóa đầu tiên được cấp phép,
> nhà đầu tư trong nước giao dịch tài sản mã hóa **không thông qua tổ chức cung cấp dịch vụ tài sản
> mã hóa do Bộ Tài chính cấp phép** tùy theo tính chất, mức độ vi phạm sẽ bị **xử lý vi phạm hành
> chính hoặc truy cứu trách nhiệm hình sự** theo quy định pháp luật"*
> [Dentons LuatViet, 22/10/2025, dẫn NQ05/2025/NQ-CP]

Bổ sung: dự thảo quy định cấm công dân giao dịch trên sàn nước ngoài chưa cấp phép **nêu đích danh
Binance, OKX, Bybit**, mức phạt đề xuất tới **30 triệu VND** cho cá nhân; dự thảo **chưa có hiệu lực**
[sanvietnam.com, 06/06/2026]. Một nguồn khác ghi mức 30–50 triệu VND [dff.vn, 06/01/2026].

Điều kiện cấp phép cực khắt khe, xác nhận không có đường tắt: **vốn điều lệ đã góp tối thiểu
10,000 tỷ VND** (~$380 triệu), doanh nghiệp Việt Nam, tối đa **5 tổ chức** trên toàn quốc
[Dentons LuatViet, 22/10/2025].

**Hệ quả trực tiếp cho MMW — không né được:**

MMW có **adapter Binance ONLY**. Toàn bộ market data, account read-only và futures order ký HMAC đều
là Binance. Nếu quy trình cấp phép hoàn tất vào Q3/2026 như dự kiến, đồng hồ 6 tháng chạy đến khoảng
**Q1/2027**, và sau mốc đó:

- Người dùng MMW tại VN có thể vi phạm hành chính hoặc hình sự **chỉ vì giao dịch qua Binance**,
  hoàn toàn độc lập với MMW.
- MMW — một công cụ được thiết kế để *tự động đặt lệnh* trên sàn đó — nằm ở vị trí xấu nhất có thể:
  không chỉ hỗ trợ, mà **chủ động thực thi** hành vi bị cấm.
- Bán sản phẩm này cho người thứ hai sau mốc đó là rủi ro pháp lý cá nhân cho tác giả, không chỉ
  rủi ro kinh doanh.

**Lưu ý cân bằng**: tại 06/2026 Binance **vẫn hợp pháp** để sử dụng tại VN, và dự thảo hạn chế
**chưa ban hành, chưa có ngày thực thi** [sanvietnam.com, 06/06/2026]. Tại 06/05/2026 Bộ Tài chính
mới yêu cầu bổ sung hồ sơ chứ chưa cấp phép [VnEconomy, 06/05/2026]. Nghĩa là mốc đếm ngược **chưa
bắt đầu**. Nhưng nó là chuyện *khi nào*, không phải *có hay không*.

### 5.2 Hoa Kỳ — SEC / CFTC

| Rủi ro | Nội dung | Nguồn |
|---|---|---|
| **CTA registration (rủi ro cao nhất cho MMW)** | Tư vấn về **futures, options, swaps** trên BTC/ETH/LTC (commodity interests) → phải đăng ký **Commodity Trading Advisor** với CFTC + thành viên **NFA**. Tư vấn thuần spot thường không cần. | [FinanceFeeds dẫn luật sư Adam Tracy, 20/05/2026] |
| SEC investment adviser | Tín hiệu trên token là chứng khoán, **bundled với tư vấn cá nhân hoá có thu phí** → có thể phải đăng ký investment adviser (Form ADV + nghĩa vụ fiduciary) | [FinanceFeeds, 20/05/2026] |
| Làm rõ phân loại | 17/03/2026 SEC + CFTC ra **joint interpretation** về cách luật chứng khoán liên bang áp dụng cho crypto assets; Chủ tịch SEC Paul Atkins tuyên bố nhằm cho thị trường "hiểu rõ" cách xử lý | [SEC.gov press release 2026-30, 17/03/2026; Sullivan & Cromwell, 19/03/2026] |
| Tiền lệ thực thi | 12/2025 SEC kiện 3 nền tảng + 4 investment club, lừa đảo **$14 triệu**, chiêu bài là **"tín hiệu do AI sinh"** trong group chat | [FinanceFeeds, 20/05/2026] |
| Tín hiệu chuẩn hoá | Coinbase đăng ký một **AI agent** với SEC là **RIA** và với CFTC/NFA là **CTA** — filing 04/2026, ra mắt 16/06/2026 | [thirdweb blog, 17/06/2026] |
| Vùng an toàn | Bot **phi lưu ký** giao dịch tài sản **của chính mình** bằng private key **của chính mình** không cần đăng ký SEC/CFTC | [ai-frb.com, 07/06/2026 — **nguồn chất lượng thấp**, blog SEO của một sản phẩm; chỉ dùng làm chỉ dấu, không phải tư vấn pháp lý] |

**Đọc thẳng cho MMW**: MMW là công cụ **futures USDT-M** dùng **AI sinh đề xuất vào lệnh**. Đây là
tổ hợp có rủi ro CTA cao nhất trong toàn bộ phổ sản phẩm. Chừng nào MMW chỉ phục vụ chính tác giả
(self-hosted, tài sản của mình, khoá của mình) thì rủi ro gần bằng 0. **Ngay khi có người dùng thứ
hai trả tiền và nhận đề xuất futures do AI sinh, tính chất pháp lý thay đổi.** Việc Coinbase — công
ty có nguyên một phòng pháp chế — chọn đăng ký cả RIA lẫn CTA cho AI agent của họ là chỉ dấu rõ nhất
về việc cơ quan quản lý coi loại sản phẩm này là gì.

### 5.3 Liên minh châu Âu — MiCA

- Hạn chuyển tiếp **CASP (Crypto-Asset Service Provider)** theo MiCA: **01/07/2026** — đã qua
  [nhiều nguồn tư vấn pháp lý, 2026; soken.dev, 16/05/2026; legasset.com, 19/03/2026].
- Bất kỳ công ty cung cấp dịch vụ crypto cho cư dân EU phải có **CASP license** — thay thế toàn bộ
  các chế độ VASP quốc gia bằng một giấy phép passport được toàn EEA [consulting24.co, 13/06/2026].
- **Đánh giá cho MMW**: nếu MMW chỉ là phần mềm self-hosted, người dùng tự cắm khoá của mình, thì
  **có thể** không rơi vào định nghĩa CASP (không nắm giữ tài sản, không vận hành nền tảng giao dịch).
  Nhưng nếu MMW chạy dạng SaaS, cầm khoá của người dùng và **tự động đặt lệnh thay họ**, ranh giới
  với "execution of orders on behalf of clients" và "reception and transmission of orders" trở nên
  mỏng. **Không tìm được án lệ hay hướng dẫn ESMA cụ thể cho loại "risk-guardrail SaaS cầm API key"**
  — đây là vùng xám thật, cần luật sư nếu định vào EU.

### 5.4 Bảng rủi ro theo mô hình vận hành

| Mô hình | Rủi ro VN | Rủi ro US | Rủi ro EU | Kết luận |
|---|---|---|---|---|
| **Self-host, 1 người dùng là chính tác giả** (hiện tại) | Thấp — cho tới mốc 6 tháng sau cấp phép sàn nội địa | Gần 0 | Gần 0 | ✅ An toàn |
| Open-source, user tự cài, tự cắm khoá | Thấp cho tác giả; người dùng chịu rủi ro riêng | Thấp | Thấp–trung bình | ⚠️ Chấp nhận được |
| SaaS, tác giả cầm khoá, **chỉ cảnh báo, không đặt lệnh** | Trung bình | Trung bình (vẫn có yếu tố advice) | Trung bình | ⚠️ Cần tư vấn |
| **SaaS, tác giả cầm khoá, AI sinh đề xuất futures + tự đặt lệnh** | **Cao** | **Cao — CTA** | **Cao — CASP** | ❌ Không nên |

---

## 6. Xu hướng công nghệ — cái gì đã commodity hoá, cái gì còn là moat

### 6.1 Đã commodity hoá hoàn toàn (không còn là lợi thế)

| Thành phần | Bằng chứng commodity hoá |
|---|---|
| **Lớp LLM sinh đề xuất/phân tích** | Mọi journal đáng kể đều có: Zella AI (TradeZella), Cypher AI (TraderSync), AI Coach (TMM, có ở **cả gói $6/tháng**), Cass (Mettle), AI của TradesViz và Plancana. MMW dùng MiniMax/DeepSeek/Gemini qua port `ILlmService` — đây là hàng hoá, không phải tài sản. |
| **Kết nối AI vào dữ liệu giao dịch** | **TMM tặng MCP server cho Claude và ChatGPT miễn phí trên MỌI gói, kể cả Free** [tradermake.money, truy cập 29/07/2026]. Nghĩa là người dùng có thể tự chat với AI về lịch sử giao dịch của mình mà không trả một xu. Đây là dấu chấm hết cho "AI phân tích journal" như một feature bán được. |
| **Chỉ báo kỹ thuật** | SMA/EMA/RSI/MACD/ATR — có trong mọi thư viện, mọi nền tảng, miễn phí. |
| **Auto-import từ sàn** | TMM: 10 sàn. 3Commas: 22+. Coinrule: 30+. Cryptohopper: 10+. Altrady: 15+. MMW: **1** (Binance). |
| **Emotion/mistake tagging** | Có trong ít nhất 10/18 sản phẩm khảo sát. |

### 6.2 Trạng thái AI agent trading 2026 — nhiều ồn ào, ít số liệu kiểm chứng

Bức tranh chung: LLM + reinforcement learning + kết nối trực tiếp node blockchain, kiến trúc
multi-agent, agent tự quản danh mục / thực thi swap / bỏ phiếu governance
[cleansky.io 12/03/2026; debridge.com 15/04/2026; coinxsight.com 20/06/2026].

Nhưng các con số quy mô đều từ **nguồn chất lượng thấp, tự công bố, không kiểm chứng được**:

- "AI agent hiện quản lý **$15 tỷ** tài sản crypto" [bitpilot.io, 17/06/2026] — **không kiểm chứng được**
- "Vốn hoá token AI agent vượt **$8 tỷ** trong 2026, dẫn đầu FET và VIRTUAL"
  [digitalblockchains.com, 17/06/2026] — **không kiểm chứng được**

**Không dùng hai con số này để ra quyết định.** Chúng được ghi ở đây để đánh dấu rằng chúng tồn tại
và không đáng tin.

Điểm đáng chú ý duy nhất có nguồn tốt: **Coinbase đăng ký AI agent như RIA + CTA** [thirdweb,
17/06/2026]. Đây là dấu hiệu thật rằng AI agent trading đang bước từ "thử nghiệm" sang "sản phẩm
được quản lý", và người có tiền + pháp chế đang chiếm chỗ.

Một quan sát tỉnh táo từ nguồn tốt hơn: một bài phân tích DeFi chỉ ra rằng
*"agent AI ký giao dịch DeFi trông giống trading bot, nhưng không phải. LLM ở giữa đọc, suy luận,
quyết định. Đó là chỗ rủi ro đổi hình dạng"* [neutralis.finance, 08/06/2026]. Đây chính là lý do
nguyên tắc "Deterministic trước, AI sau" của MMW (`specs/001-mmw-system-baseline/spec.md:24`) là
đúng về mặt kỹ thuật — nhưng đúng về kỹ thuật không tự động thành moat thương mại.

### 6.3 Cái gì còn là moat thật (xếp theo độ bền)

1. **Quyền thực thi ở tầng execution + audit trail có thể kiểm toán.** Không phải "phân tích", mà là
   "chặn". Đây là thứ duy nhất trong MMW mà không sản phẩm nào trong 18 cái có (mục 3.2). Bền vì nó
   đòi tích hợp sâu + niềm tin, không copy được bằng một prompt.
2. **Dữ liệu hành vi theo chuỗi thời gian của chính người dùng, đủ dài.** A-Trader đang xây moat này
   (22 tín hiệu × hàng nghìn người dùng). MMW có kiến trúc đúng để làm (bảng Flag tách riêng rule vs
   behavior, FR-016) nhưng có đúng **1 người dùng** → không có dữ liệu → không có moat.
3. **Niềm tin để được giao khoá đặt lệnh.** Đây là moat mạnh nhất và MMW hiện đang **âm** ở đây
   (`TradingAccount.cs:29-34` lưu plaintext).
4. **Kênh phân phối / community.** TradeZella được mô tả là có "content và education engine lớn nhất
   trong ngành" [tradermake.money, 07/2026]. MMW có 0.
5. **KHÔNG phải moat**: lớp AI, bộ chỉ báo, giao diện đẹp, kiến trúc clean, .NET 8, số dòng code.

---

## 7. Kết luận và ba đường đi

### 7.1 Đối chiếu thẳng: MMW ở đâu trên bản đồ

| Chiều | MMW | Thị trường 2026 | Đánh giá |
|---|---|---|---|
| Quy mô mã | ~12.3k LOC thật, 210 file, 5 project | — | Đủ cho MVP một người, không phải lợi thế |
| Người dùng trả tiền | **0** | TMM 170k · Cryptohopper 500k · CMM 13k · Zero Tilt 12k | **Chưa có sản phẩm, mới có phần mềm** |
| Sàn hỗ trợ | **1** (Binance) | TMM 10 · Coinrule 30+ · 3Commas 22+ | Thua rõ rệt, và là rủi ro pháp lý VN |
| Behavior detector | **3** (Revenge, LossStreak, OversizedAfterLoss) | A-Trader **22 tín hiệu** · Edgewonk Tiltmeter · TradesViz cost-of-emotion | Thua về độ sâu |
| Chặn lệnh trước khi chạm sàn | **~13 lớp ở tầng API sàn** | TiltGuard chặn ở tầng trình duyệt; còn lại không chặn | ✅ **Dẫn đầu — điểm mạnh duy nhất** |
| AI | MiniMax/DeepSeek/Gemini qua port | Có ở mọi đối thủ, kể cả gói $6 và gói Free | Ngang bằng = không phải điểm bán |
| Test coverage vùng rủi ro cao | **0 test cho preflight/advisor/live-order gates** | — | ❌ Không được bán khi còn như vậy |
| Bảo mật khoá API | **plaintext trong SQL Server** (`TradingAccount.cs:31,34`) | — | ❌ Blocker tuyệt đối |
| Thời điểm thị trường | — | BTC −45.5% từ đỉnh, F&G = 14, derivatives volume −18% QoQ | ❌ Sai thời điểm |
| Pháp lý VN | Binance-only | Đồng hồ 6 tháng chưa chạy nhưng sẽ chạy | ⚠️ Rủi ro cấp tồn tại |

### 7.2 Trả lời câu hỏi thương mại, không vòng vo

**Có nên biến MMW thành sản phẩm thương mại bán cho trader crypto Việt Nam không?**
**Không, không phải trong 12 tháng tới.** Lý do xếp theo mức độ quyết định:

1. **Pháp lý** — NQ05 đặt một mốc đếm ngược mà bạn không kiểm soát được, đúng vào tim sản phẩm
   (Binance-only). Đây không phải rủi ro có thể "quản lý", nó là rủi ro nhị phân.
2. **Định giá** — thị trường có giá sàn $0 (CMM UNLOCKED qua affiliate) và giá tham chiếu $6–12 (TMM).
   Không có chỗ cho một sản phẩm mới không có brand.
3. **Thời điểm** — mọi chỉ số retail đang ở đáy chu kỳ, và derivatives (đúng phân khúc) teo nhanh nhất.
4. **Khoảng cách sản phẩm** — 0 khách hàng, 0 test ở vùng rủi ro nhất, khoá lưu plaintext, 1 sàn.
5. **Khoảng trống** — hẹp hơn nhiều so với giả định; behavior detection đã bị chiếm bởi ít nhất 8 sản phẩm.

### 7.3 Ba đường đi khả dĩ

**Đường A — Giữ nguyên là công cụ cá nhân, tối ưu cho chính mình. (Khuyến nghị mạnh nhất)**

Đây là đường có ROI cao nhất trên mỗi giờ bỏ ra. MMW đã hoạt động, đã đúng triết lý, đã có kiến trúc
sạch. Việc cần làm không phải thêm feature bán được, mà là làm nó **đáng tin cho chính bạn**:

- Mã hoá `ApiKey`/`ApiSecret` (`src/MMW.Domain/Entities/TradingAccount.cs:31,34`) — kể cả khi chỉ
  một người dùng, khoá này có quyền đặt lệnh futures.
- Viết test cho 13 lớp chặn của `LiveOrderService.cs` và luồng preflight (Phụ lục B mục 5) — đây là
  chỗ mất tiền thật.
- Bổ sung 3–4 tín hiệu hành vi mà thị trường đã chứng minh là có giá trị và MMW đang thiếu:
  `stop-moving frequency`, `decision latency`, `idle-restraint`, `position-size variance liên tục`
  (nguồn ý tưởng: A-Trader 22 signals, arizet.com).
- Sửa vốn đầu ngày đang ước lượng (Phụ lục B mục 3) — nó làm sai quy tắc `DailyLossLimit`, một trong
  hai rule Critical.

Giá trị thu được là **thật và đo được**: nó bảo vệ vốn giao dịch của chính bạn. Không cần một khách
hàng nào để có giá trị đó.

**Đường B — Mở nguồn, xây uy tín kỹ thuật, không xây business.**

MMW là một tham chiếu tốt về "risk guardrail ở tầng execution cho crypto futures" — thứ không có
open-source tương đương tìm được. Mở nguồn (sau khi sửa vấn đề khoá) đổi lấy:
- Uy tín kỹ thuật cá nhân (giá trị nghề nghiệp thật cho một .NET dev)
- Người khác tự cài, tự chịu rủi ro pháp lý của họ, bạn không cầm khoá của ai
- Tránh hoàn toàn CTA/CASP/NQ05 vì bạn không cung cấp dịch vụ, chỉ công bố mã

Không có doanh thu. Nhưng cũng không có rủi ro pháp lý và không có gánh nặng support.

**Đường C — Nếu vẫn muốn thương mại hoá: đổi thị trường, không đổi sản phẩm.**

Nếu quyết định làm business, đừng bán cho retail crypto VN. Hai hướng có logic hơn:

- **Bán cho crypto prop firm** (thị trường $20 tỷ 2025, 40+ firm, chỉ 5–10% trader vượt vòng đánh
  giá, phần lớn trượt vì **vi phạm rule và quản trị rủi ro kém** [CryptoFundTrader, 24/12/2025]).
  Prop firm có ngân sách B2B, có nhu cầu enforcement thật, và **họ đã cầm khoá/tài khoản của trader
  rồi** — giải được đúng rào cản niềm tin lớn nhất của MMW. TiltGuard đã nhìn thấy điều này và đang
  nhắm prop, nhưng chỉ ở tầng trình duyệt. MMW chặn ở tầng đúng hơn.
- **Bán "risk guardrail as infrastructure"** cho các sản phẩm khác, không bán cho end-user. A-Trader
  đang làm đúng vậy: sản phẩm psychology của họ là **SDK-callable** [arizet.com, truy cập 29/07/2026].

Cả hai hướng đều đòi phải giải quyết trước: mã hoá khoá, test cho lớp chặn, và mở rộng khỏi Binance-only.

### 7.4 Điều gì phải đúng để kết luận trên là SAI

Một thẩm định trung thực phải nói rõ điều kiện tự bác bỏ. Kết luận "không khả thi thương mại" ở mục
7.2 sẽ sai nếu **đồng thời** xảy ra các điều sau:

| # | Điều kiện | Xác suất *(đánh giá chủ quan)* | Cách kiểm chứng |
|---|---|---|---|
| 1 | Có ≥1 sàn Việt Nam được cấp phép **và** mở API futures đủ dùng, để MMW thoát khỏi rủi ro Binance | Trung bình — 5 hồ sơ đã hợp lệ, nhưng chưa rõ có sản phẩm futures hay không | Theo dõi sản phẩm của VIX / Techcom / Lộc Phát / Việt Nam Thịnh Vượng / Dịch vụ Tài sản số VN |
| 2 | Nhu cầu "chặn lệnh" được chứng minh có người trả tiền — không phải chỉ có người gật đầu đồng ý | Chưa biết — **không có dữ liệu** | Bán trước cho 10 người quen trong cộng đồng trader, thu tiền thật, trước khi viết thêm dòng code nào |
| 3 | Chu kỳ crypto đảo chiều, retail quay lại | Không kiểm soát được | F&G > 60 duy trì 3 tháng + volume derivatives hồi về mức Q4/2025 |
| 4 | Tìm được kênh phân phối không phụ thuộc KOL-affiliate | Thấp ở VN; **trung bình nếu chuyển sang B2B prop firm** | Thử tiếp cận 5 crypto prop firm với đề xuất risk-guardrail |
| 5 | MMW đạt được mức tin cậy để người lạ giao khoá đặt lệnh (mã hoá + test + audit) | Hoàn toàn trong tầm kiểm soát của tác giả | Sửa `TradingAccount.cs:31,34` + viết test cho `LiveOrderService` |

Điều kiện #2 là điều quan trọng nhất và cũng là điều **rẻ nhất để kiểm chứng**. Nó không đòi viết
thêm code. Nếu 10 trader quen không ai trả $12/tháng cho lời hứa "phần mềm sẽ chặn lệnh sai kỷ luật
của bạn", thì mọi thứ còn lại là vô nghĩa. Nếu 7/10 người trả, thì bản thẩm định này cần được viết lại.

**Khuyến nghị thứ tự hành động**: kiểm chứng #2 trước → nếu qua thì làm #5 → rồi mới xét #1 và #4.
Không làm ngược lại. Sai lầm phổ biến nhất là dành 6 tháng làm #5 (mã hoá, test, đa sàn) rồi mới
phát hiện #2 không qua.

### 7.5 Điều cần theo dõi (trigger để xem xét lại kết luận này)

| Sự kiện | Tác động | Cách theo dõi |
|---|---|---|
| Bộ Tài chính **cấp phép** sàn nội địa đầu tiên | Khởi động đồng hồ 6 tháng → sau đó dùng Binance có thể bị xử lý | VnEconomy, cổng thông tin Bộ Tài chính |
| Dự thảo cấm sàn nước ngoài **được ban hành** | Xác nhận mức phạt và ngày thực thi | Thư Viện Pháp Luật, CoindDesk/Reuters |
| BTC vượt lại vùng $100k + F&G > 60 | Retail quay lại → thị trường công cụ hồi phục | CoinDesk, Fear & Greed Index |
| Một sàn nội địa VN mở **API futures công khai** | Mở đường thay thế Binance adapter | Thông báo của 5 doanh nghiệp đã nộp hồ sơ |
| TMM hoặc CMM ra tính năng **chặn lệnh trước execution** | Khoảng trống duy nhất của MMW bị lấp | Changelog của họ |

---

## 8. Danh sách quyết định cần chốt

Không phải khuyến nghị, mà là các câu hỏi mà bản đồ thị trường này buộc phải trả lời. Mỗi câu có
một mặc định được đề xuất, dựa trên phân tích ở trên.

| # | Quyết định | Mặc định đề xuất | Vì sao (dẫn về mục) |
|---|---|---|---|
| **D1** | MMW là **sản phẩm cá nhân** hay **sản phẩm thương mại**? | **Cá nhân** — cho tới khi điều kiện #2 ở mục 7.4 được kiểm chứng bằng tiền thật | 7.2, 7.4 |
| **D2** | Có nhận khoá API của người thứ hai không? | **Không**, cho tới khi `TradingAccount.cs:31,34` được mã hoá và `LiveOrderService` có test | 3.3, 5.4 |
| **D3** | Có mở rộng khỏi Binance-only không? | **Có, ưu tiên trung bình** — không phải vì tính năng, mà vì **rủi ro pháp lý NQ05** | 5.1 |
| **D4** | Có thêm biểu đồ / báo cáo / analytics sâu hơn không? | **Không** — đó là góc bão hoà của bản đồ định vị, thua chắc | 2.5 |
| **D5** | Có đầu tư thêm vào lớp AI (prompt, model, agent) không? | **Không ưu tiên** — đã commodity hoá, TMM tặng MCP miễn phí | 6.1 |
| **D6** | Có bổ sung tín hiệu hành vi mới không? | **Có** — nhưng chọn 3–4 cái thị trường đã chứng minh mà MMW thiếu: `stop-moving frequency`, `decision latency`, `position-size variance` liên tục, `idle-restraint` | 3.1, 6.3 |
| **D7** | Có mở nguồn không? | **Xem xét nghiêm túc** sau khi xử lý D2 — đổi lấy uy tín kỹ thuật, tránh toàn bộ rủi ro CTA/CASP/NQ05 | 7.3-B |
| **D8** | Có thử hướng B2B prop firm không? | **Thăm dò, chi phí thấp** — 5 email, không viết code | 7.3-C, 7.4 #4 |
| **D9** | Có giữ `SignalGenerator` thuần quy tắc (Phụ lục B mục 8) không? | **Giữ và đưa lại lên luồng chính làm fallback** — nó là hiện thân của nguyên tắc "Deterministic trước, AI sau", và giảm phụ thuộc chi phí LLM | 4.4, 6.1 |
| **D10** | Có tối ưu tần suất Hangfire job không? | **Có, trước khi có khách thứ hai** — chi phí LLM tăng tuyến tính theo khách | 4.4 |

### 8.1 Ba việc rẻ nhất, giá trị cao nhất, làm được ngay

1. **Mã hoá khoá API** (`src/MMW.Domain/Entities/TradingAccount.cs:31,34`). Chi phí: vài giờ.
   Giá trị: mở khoá mọi con đường tương lai, và bảo vệ tiền thật của chính bạn ngay hôm nay.
2. **Test cho 13 lớp chặn của `LiveOrderService.cs`.** Chi phí: 1–2 ngày. Giá trị: đây là điểm mạnh
   *duy nhất* mà thị trường không có (mục 3.2) — nó phải đúng, nếu không nó không phải điểm mạnh.
3. **Hỏi 10 trader quen: "bạn có trả $12/tháng cho phần mềm chặn lệnh sai kỷ luật của bạn không?"**
   rồi **thu tiền trước của người nào nói có**. Chi phí: một buổi. Giá trị: đây là dữ liệu duy nhất
   mà không nghiên cứu thị trường nào thay thế được.

---

## Phụ lục — Danh mục nguồn đã truy cập (toàn bộ 29/07/2026 trừ khi ghi khác)

**Chu kỳ thị trường**: CoinDesk (giá BTC) · WorldCoinIndex · CoinStats AI (01/07/2026) ·
The Cryptonomist (20/07/2026) · Yahoo Finance (29/06/2026) · Coin Gabbar (06/07/2026) ·
TokenInsight Q2 2026 Report (20/07/2026) · Phemex News (20/04/2026) · The Market Periodical (10/04/2026) ·
CryptoRank.io (06/07/2026) · CryptoScorer (26/03/2026) · Binance Square (16/01/2026)

**Quy mô thị trường**: MarkWide Research · Global Information Inc. (13/01/2026) ·
Business Research Insights (06/07/2026) · Verified Market Reports (23/05/2026) ·
Grand View Research · IntelMarketResearch · Market Publishers · PW Consulting (13/06/2026)

**Đối thủ**: tradezella.com/blog (14/07/2026) · pineify.app (05/11/2025) · top30forexbrokers.com ·
StockBrokers.com (29/10/2025) · traderssecondbrain.com (02/04/2026) · tradingtoolshub.com (16/06/2026) ·
thefxgeek.com (14/07/2026) · daytradingz.com (05/06/2026) · tradingjournal.com (03/03/2026) ·
tradingsfx.com · coinmarketman.com/pricing · tradermake.money/prices + /compare ·
ComparEdge (08/07 & 17/07/2026) · G2 (09/04/2026) · uwuu.ai (08/05/2026) · UseThisAI.fyi ·
defenderbot.tech (11/04/2026) · walletreviewer.com · jonathonspire.com (13/04/2026) ·
gncrypto.news (22/04/2026) · Finestel (09/06/2026) · ai-trading-ranked.com

**Psychology/behavior**: tiltguard.app/features · zerotilt.io · arizet.com/company/trading-psychology ·
getmettle.app/blog (22/06/2026) · plancana.com/blog (12/03/2026)

**Việt Nam**: Alex's Substack — web3thoughtdrops.substack.com (21/05/2026) · Disruption Banking (06/11/2025) ·
Cục Thống kê qua thuvienphapluat.vn (04/07/2026) · Báo Chính phủ (20/10/2025) ·
Dentons LuatViet (22/10/2025) · VnEconomy (06/05/2026) · Thanh Niên (05/02/2026) ·
Doanh nghiệp Việt Nam (21/01/2026) · sanvietnam.com (06/06/2026) · dff.vn (30/03 & 06/01/2026) ·
Dân trí (11/12/2025) · Báo Chính phủ — Thông tư 32/2026 (01/04/2026) ·
AppsFlyer State of Finance 2026 qua Advertising Vietnam · t.me/congdongtradevietnam · vertexgroups.net

**Pháp lý quốc tế**: FinanceFeeds (20/05/2026) · SEC.gov press release 2026-30 (17/03/2026) ·
Sullivan & Cromwell (19/03/2026) · thirdweb blog (17/06/2026) · ai-frb.com (07/06/2026, chất lượng thấp) ·
consulting24.co (13/06/2026) · soken.dev (16/05/2026) · legasset.com (19/03/2026)

**Công nghệ**: cleansky.io (12/03/2026) · debridge.com (15/04/2026) · coinxsight.com (20/06/2026) ·
neutralis.finance (08/06/2026) · bitpilot.io (17/06/2026, không kiểm chứng) ·
digitalblockchains.com (17/06/2026, không kiểm chứng)

**Prop firm**: CryptoFundTrader (24/12/2025, tự công bố — xung đột lợi ích)

**Mã nguồn MMW**: `specs/001-mmw-system-baseline/spec.md` ·
`src/MMW.Domain/Entities/TradingAccount.cs:29-34`

---

*Tài liệu này là phân tích thị trường và chiến lược sản phẩm. Nó KHÔNG chứa lời khuyên đầu tư cá nhân,
không khuyến nghị mua bán bất kỳ tài sản nào, và không thay thế tư vấn pháp lý chuyên nghiệp. Mọi kết
luận pháp lý ở mục 5 là tóm tắt nguồn công khai, cần luật sư xác nhận trước khi hành động.*
