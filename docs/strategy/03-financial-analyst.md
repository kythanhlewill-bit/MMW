# 03 — Thẩm định tài chính MMW

> **Loại tài liệu**: thẩm định tài chính để ra quyết định tiền thật (financial due diligence). **KHÔNG phải pitch deck.**
> **Ngày lập**: 2026-07-30 · **Vai**: Senior Crypto Financial Analyst & Investor (10+ năm, 3 chu kỳ)
> **Đầu vào**: `docs/strategy/01-market-landscape.md` (829 dòng) · `docs/strategy/02-product-reality.md` (628 dòng) ·
> `specs/001-mmw-system-baseline/spec.md` (50 FR) · đọc trực tiếp mã nguồn tại `D:/KYLT/MMW`.
> **Giới hạn công cụ**: `WebSearch`/`WebFetch` đã thử **một lần** tại 2026-07-30 → vẫn lỗi backend
> (`There's an issue with the selected model (deepseek-v4-pro)`). Không retry. Toàn bộ số liệu thị trường
> **tái sử dụng từ file 01** với nguyên nguồn + ngày. Mọi con số tôi tự dựng đều đánh dấu `(ước lượng)` hoặc `(giả định)`.
> **Miễn trừ**: đây là phân tích chiến lược sản phẩm & kinh doanh. KHÔNG chứa lời khuyên đầu tư cá nhân,
> không khuyến nghị mua/bán bất kỳ tài sản nào, không thay thế tư vấn pháp lý hoặc tư vấn thuế.

---

## 0. Kết luận trước, luận cứ sau

Sáu câu, để bạn không phải đọc 1.000 dòng mới biết:

1. **Phát hiện lớn nhất của bản thẩm định này không nằm ở thị trường mà nằm ở hoá đơn LLM.**
   Với tần suất job hiện tại, MMW đốt **~313.000 lời gọi LLM / người dùng / tháng**
   (`spec.md:319-321`), tương đương **$48 – $197/người/tháng** tuỳ mô hình (ước lượng, mục 5).
   Giá bán khả thi của phân khúc là **$9–19/tháng** [file 01 §2.4]. Nghĩa là **biên gộp âm 250% đến âm 1.600%**.
   Không có mức giá nào cứu được kiến trúc này. Đây không phải "vấn đề tối ưu về sau" — đây là **mô hình
   kinh doanh không tồn tại** cho tới khi tần suất job được thiết kế lại.

2. **Và nó cũng đang ăn tiền của chính bạn ngay hôm nay.** Với cấu hình cá nhân (5 symbol watchlist,
   2 lệnh mở), chi phí LLM là **~$60/tháng ≈ $724/năm** (ước lượng, Tier B). Trên một tài khoản
   giao dịch $5.000 (giả định), đó là **14,5% vốn mỗi năm** — lớn hơn edge của phần lớn trader retail.
   **Việc kỹ thuật có ROI cao nhất trong toàn dự án, cho chính bạn, không phải thêm feature mà là cắt
   tần suất gọi AI.** Sửa xong, chi phí về ~$3–6/tháng (~1% vốn/năm). Đây là kết luận hành động số một.

3. **PMF thật sự nhỏ hơn nhiều so với cả ước lượng bi quan của file 01.** Phễu (a)→(d) có hai nút
   thắt chết người là **tự nhận thức** và **sẵn sàng trả tiền để bị chặn**. Ước lượng của tôi: toàn cầu
   chỉ có **~5.000–7.000 người** vừa có vấn đề kỷ luật, vừa thừa nhận, vừa chịu trả tiền để bị chặn
   (ước lượng, mục 1). Doanh thu toàn phân khúc "trả tiền để bị chặn" ≈ **$0,9–1,3 triệu/năm toàn cầu**.
   Đây là một *ngách trong một ngách*, không phải một thị trường.

4. **Thị trường trả tiền cho hy vọng gấp khoảng 15–40 lần so với kỷ luật**, và tỷ số này đo được chứ
   không phải cảm tính: Cryptohopper (bot, bán hy vọng) **500.000 người dùng**
   [UseThisAI.fyi, truy cập 29/07/2026] so với toàn bộ nhóm psychology-first cộng lại — Zero Tilt
   **12.000** [zerotilt.io, truy cập 29/07/2026] + TiltGuard + Mettle + Plancana — ước chừng dưới 40.000.
   Hàm ý cho định vị: **đừng bán "chặn" cho người mua "thắng". Bán "chặn" cho người đã có tiền của
   người khác để bảo vệ** — tức là prop firm, không phải retail.

5. **Token: KHÔNG. Dứt khoát không.** MMW không có trạng thái on-chain, không có bên thứ ba cần đồng
   thuận, không có tài sản chung để phân phối. Phát hành token ở đây là **huy động vốn trả bằng uy tín
   cá nhân**, và tệ hơn: token cần volume/đầu cơ để giữ giá, trong khi sản phẩm tồn tại để làm người
   ta **giao dịch ít hơn và nhỏ hơn**. Đó là xung đột động lực nằm ngay trong thiết kế, không thương
   lượng được (mục 4).

6. **ROI cho chính tác giả: đường thương mại hoá âm trong MỌI kịch bản B2C ở mốc 24 tháng**, kể cả
   kịch bản lạc quan (−$4.500). Đường đối chứng — không thương mại hoá, chỉ sửa 4 việc cho chính mình
   trong ~12–15 ngày-người — có **ROI dương ngay từ tháng thứ 3** và trả lại ~800 giờ cho 12 tháng
   tiếp theo. Đường duy nhất có kỳ vọng dương ngoài đối chứng là **B2B prop firm**, và nó hoà vốn ở
   **1–3 khách hàng** thay vì 109–186 khách B2C (mục 6).

**Verdict tài chính**: *Không thương mại hoá B2C. Sửa chi phí LLM cho chính mình trong 6 tuần tới.
Dành 1 buổi thăm dò B2B prop firm với chi phí bằng 0. Không phát hành token.*

---

## 1. Product-Market Fit thật sự

### 1.1 Đặt lại câu hỏi cho đúng

Câu hỏi không phải "có bao nhiêu người trade crypto futures". Câu hỏi là phễu bốn tầng:

| Tầng | Điều kiện | Bản chất |
|---|---|---|
| (a) | Giao dịch crypto futures bán lẻ | **Đo được gián tiếp** |
| (b) | Thua tiền vì thiếu **kỷ luật**, không phải thiếu **tín hiệu** | Có chỉ dấu định lượng |
| (c) | **TỰ NHẬN THỨC** được rằng (b) đúng với mình | ⚠️ **Nút thắt 1 — không có dữ liệu trực tiếp** |
| (d) | Sẵn sàng **trả tiền để bị phần mềm chặn lại** | ⚠️ **Nút thắt 2 — có bằng chứng gián tiếp mạnh** |

Ba tầng đầu quyết định thị trường **cảm xúc**. Tầng (d) quyết định thị trường **có ngân sách**.
File 01 §1.1 đã nói đúng một câu quan trọng: *"Người vừa mất 45% tài khoản không phải người sẵn sàng
mở ví trả $30/tháng."*

### 1.2 Tầng (a) — quy mô cơ sở

Không tồn tại số đầu người công bố cho "trader crypto futures bán lẻ toàn cầu". File 01 §1.2 đã chứng
minh rằng các báo cáo "market size" cho phân khúc này lệch nhau tới **215 lần** và **không dùng được**.
Vì vậy tôi dùng **trần quan sát được** — tổng số người dùng đã công bố của các công cụ chuyên dụng:

| Sản phẩm | Người dùng công bố | Nguồn |
|---|---:|---|
| Cryptohopper (bot) | 500.000 | [UseThisAI.fyi, truy cập 29/07/2026] |
| Trader Make Money (journal crypto-native) | 170.000 | [tradermake.money, truy cập 29/07/2026] |
| CoinMarketMan (journal crypto-native) | 13.000 | [coinmarketman.com, truy cập 29/07/2026] |
| Zero Tilt (psychology-first) | 12.000 (tự công bố) | [zerotilt.io, truy cập 29/07/2026] |
| **Tổng thô (có trùng lặp)** | **~695.000** | |

**Trần khả tiếp cận (ước lượng): ~500.000 – 700.000 người** — tức là tập trader crypto đủ nghiêm túc
để đã từng cài **một công cụ chuyên dụng nào đó**. Đây là trần trên hợp lý cho TAM tính theo đầu người,
vì một sản phẩm mới không thể tiếp cận người chưa từng tìm công cụ.

**Điều chỉnh chu kỳ, bắt buộc phải làm**: derivatives — đúng phân khúc MMW — đang teo nhanh nhất
thị trường: $14,6T → $12,0T, **−18% QoQ trong Q2/2026** [TokenInsight, Crypto Exchange Report Q2 2026,
20/07/2026], trong khi spot đang hồi. Fear & Greed = **14, Extreme Fear** [CoinStats AI, 01/07/2026].
Tập (a) đang **co lại**, không phải mở rộng.

### 1.3 Tầng (b) — bao nhiêu người thua vì kỷ luật?

Chỉ dấu định lượng tốt nhất tìm được trong toàn bộ nghiên cứu vòng 1, và nó khá mạnh:

> Crypto prop trading đạt quy mô **$20 tỷ trong 2025**; **chỉ 5–10% trader vượt được vòng đánh giá**,
> và **chỉ 7% từng nhận payout**; hơn 40 prop firm cung cấp crypto làm asset class chính
> [CryptoFundTrader, 24/12/2025 — *nguồn tự công bố của một prop firm, có xung đột lợi ích;
> dùng làm chỉ dấu định tính, không phải số liệu kiểm chứng*].

Vòng đánh giá prop firm **loại người vì vi phạm rule và quản trị rủi ro kém, không phải vì đoán sai
hướng** — đó chính là định nghĩa của (b). Nếu 90–95% trượt, thì (b) phủ gần như toàn bộ tập (a).

**Hệ số tôi dùng: 70% (giả định)** — thấp hơn con số prop firm gợi ý, để trừ hao rằng tập (a) là
những người đã chủ động tìm công cụ nên có kỷ luật hơn mặt bằng.

→ **(a) × (b) ≈ 350.000 – 490.000 người.**

### 1.4 Tầng (c) — TỰ NHẬN THỨC. Đây là nút thắt thứ nhất

**Không có dữ liệu công khai** về tỷ lệ trader tự nhận mình mất kỷ luật. Nhưng có một phép đo gián
tiếp rất sạch, và nó nằm ngay trong file 01: **so sánh quy mô nhóm "bán hy vọng" với nhóm
"bán kỷ luật".** Người mua công cụ psychology-first đã tự nhận thức — đó là điều kiện để họ bấm mua.

| Nhóm | Sản phẩm | Người dùng |
|---|---|---:|
| **Bán hy vọng** (bot, tự động hoá) | Cryptohopper | 500.000 |
| **Bán kỷ luật** (psychology-first) | Zero Tilt (tự công bố) | 12.000 |
| | TiltGuard, Mettle, Plancana, A-Trader, Tilt Proof, TradeMaxxing, TradeMindset, Tradefulness, TILT, tradeplanner.ai… | không công bố; **ước lượng cộng gộp 20.000–30.000** |
| | **Tổng nhóm kỷ luật (ước lượng)** | **~32.000 – 42.000** |

**Tỷ số hy vọng : kỷ luật ≈ 12:1 đến 15:1** nếu chỉ so với Cryptohopper; **và cao hơn nhiều** nếu
cộng 3Commas, Coinrule, Altrady và toàn bộ hệ sinh thái signal group.

Đối chiếu chéo: nhóm psychology-first (~32–42k) so với tập (b) (~350–490k) cho **tỷ lệ 7–12%**.

**Hệ số tôi dùng cho (c): 12% (ước lượng)**, neo vào chính tỷ số quan sát được ở trên chứ không phải
cảm tính.

→ **(a) × (b) × (c) ≈ 42.000 – 59.000 người tự nhận thức trên toàn cầu.**

**Cảnh báo phải nói thẳng**: hệ số này có thể còn *lạc quan*. Nhóm psychology-first bán chủ yếu
**công cụ tự nguyện** — panic button, mood diary, tag cảm xúc. Mua một app để *tự nhắc mình* dễ hơn
nhiều so với mua một app để *tước quyền quyết định của mình*. Tự nhận thức đủ để mua nhật ký cảm xúc
**không phải** tự nhận thức đủ để giao chìa khoá đặt lệnh cho phần mềm.

### 1.5 Tầng (d) — trả tiền để BỊ CHẶN. Nút thắt thứ hai, và nó có bằng chứng cứng

Đây là chỗ file 01 đã đào ra dữ kiện quan trọng nhất của toàn bộ nghiên cứu:

> **CoinMarketMan: "CMM UNLOCKED" = miễn phí trọn đời qua link giới thiệu sàn;
> "90% người dùng CMM đang ở UNLOCKED"** [coinmarketman.com/pricing, truy cập 29/07/2026].

Và: Trader Make Money có **170.000 trader** nhưng *"phần lớn ở gói Free"*
[tradermake.money, truy cập 29/07/2026]; TradesViz, Trademetria, Tradervue, Mettle, Plancana,
StonkJournal đều có gói Free [file 01 §2.4].

**Tỷ lệ trả tiền quan sát được trong phân khúc: ≤10%.** Đây không phải giả định của tôi — đây là con
số CMM tự công bố cho chính họ.

Và phải trừ thêm một tầng nữa mà file 01 chưa tách bạch: trong số người *chịu trả tiền cho một
journal*, bao nhiêu người chịu trả tiền cho một thứ **chủ động chặn tay mình**? TiltGuard — sản phẩm
duy nhất định vị y hệt MMW (*"It enforces rules"*) — chọn mô hình **mua đứt trọn đời**
[tiltguard.app, truy cập 29/07/2026], **không công bố giá**. Mua đứt là mô hình của người bán biết
rằng khách sẽ không tiếp tục trả hàng tháng. Đó là một tín hiệu thị trường, không phải một lựa chọn
marketing.

**Hệ số tôi dùng cho (d): 10% (ước lượng)** — chính bằng số của CMM, không nới thêm.

### 1.6 Tổng hợp TAM / SAM / SOM

```
TAM  — Tập có VẤN ĐỀ (không phải tập có ngân sách)
       500.000–700.000  trader crypto đã dùng ≥1 công cụ chuyên dụng   [tổng hợp công bố, file 01 §1.3]
     × 70%              thua vì kỷ luật, không vì tín hiệu             (giả định, neo prop firm 5-10% pass)
     = 350.000–490.000 người
     × $12–15/tháng ARPU giả định nếu TẤT CẢ đều trả  →  $50–88 triệu/năm  ← con số này VÔ NGHĨA

SAM  — Tập TỰ NHẬN THỨC (mới bắt đầu có nghĩa)
       350.000–490.000
     × 12%              tự nhận thức                                    (ước lượng, neo tỷ số 12:1 mục 1.4)
     = 42.000–59.000 người
     × $12–15/tháng  →  $6,0–10,6 triệu/năm

SOM  — Tập TRẢ TIỀN ĐỂ BỊ CHẶN (con số thật duy nhất đáng nhìn)
       42.000–59.000
     × 10%              chịu trả tiền                                   (ước lượng = tỷ lệ CMM công bố)
     = 4.200–5.900 người TOÀN CẦU
     × $12–19/tháng  →  $0,6–1,3 triệu/năm  ← ĐÂY LÀ TOÀN BỘ PHÂN KHÚC

SOM-MMW — Thị phần khả dĩ của một dev đơn lẻ, 1 sàn, 0 brand, 0 kênh phân phối
       4.200–5.900
     × 0,5–2,5%         thị phần khả dĩ                                 (giả định)
     = 21–148 khách hàng
     × $12–19/tháng  →  $3.000 – $33.700 doanh thu GỘP/năm, sau 2–3 năm xây dựng
```

**Đối chiếu chéo với file 01**: ước lượng riêng cho Việt Nam ở file 01 §4.1 cho ra **60–660 khách
tiềm năng tối đa**. Ước lượng toàn cầu của tôi (21–148) *thấp hơn* ước lượng VN của file 01, và điều
đó là **có chủ đích**: file 01 tính "tiềm năng tối đa" ở tầng (b), tôi tính ở tầng (d) sau khi trừ
hai nút thắt (c) và (d). **Hai con số không mâu thuẫn — chúng đo hai điểm khác nhau trên cùng một phễu.**
Nếu bạn muốn một con số để ra quyết định, dùng **21–148**, không dùng 60–660.

### 1.7 Ba sự thật khó nghe về PMF

**Thứ nhất: người mất kỷ luật không thừa nhận mình mất kỷ luật — nhưng vấn đề thật còn tệ hơn thế.**
Vấn đề không phải họ *không biết*. Phần lớn trader thua đều biết chính xác lệnh nào là lệnh trả thù.
Vấn đề là **họ biết SAU, và họ không muốn bị chặn TRƯỚC**. Đây là bài toán *time-inconsistent
preference* kinh điển: con người lúc bình tĩnh muốn bị ràng buộc, con người lúc tilt muốn được tự do,
và **người bấm nút mua hàng là con người lúc bình tĩnh, còn người bấm nút huỷ đăng ký là con người
lúc tilt**. TiltGuard hiểu điều này — họ có tính năng tên là **Non-Override Protection Mode**
[tiltguard.app, truy cập 29/07/2026]. MMW có `AllowOverrideRisk` — một cờ toàn cục **bật/tắt tự do**
(`spec.md:246`, `LiveOrderService.cs:158-161`). Về mặt sản phẩm, MMW đang thua ở đúng điểm quyết định
sự sống còn của category: **một ràng buộc có thể tự tháo không phải là ràng buộc.**

**Thứ hai: MMW chỉ chặn được đường mà MMW đi qua.** File 01 §3.3 đã nói. Tôi nhấn lại vì nó là vấn đề
*tài chính*, không phải kỹ thuật: khách hàng trả tiền cho một lời hứa mà sản phẩm **không thể giữ**
khi người dùng mở app Binance trên điện thoại. Tỷ lệ hoàn tiền và churn trong sản phẩm loại này sẽ cao
bất thường, và bạn không có cách nào sửa bằng code.

**Thứ ba: rào cản niềm tin ở đây cao nhất trong toàn ngành fintech ngách.** Bạn xin **khoá API có
quyền đặt lệnh futures** từ người lạ. Hiện `TradingAccount.cs:29-34` lưu khoá **plaintext trong SQL
Server**, và 9/18 lớp chặn của `LiveOrderService` **không có test nào chứng minh chúng chặn**
[file 02 §4.2]. Về mặt tài chính, ý nghĩa của hai dòng này là: **tỷ lệ chuyển đổi từ "quan tâm" sang
"cắm khoá" sẽ rất thấp** (ước lượng <5%), và mỗi khách hàng mang theo một khoản **nợ trách nhiệm
tiềm tàng lớn hơn nhiều lần doanh thu $12–19/tháng họ trả.** Một lệnh sai do bug ở lớp chặn không có
test có thể xoá sạch tài khoản của khách — và bạn là cá nhân, không phải pháp nhân có bảo hiểm.

---

## 2. Tâm lý đám đông — thị trường thực sự mua gì?

### 2.1 Bảng đối chiếu: sản phẩm bán hy vọng vs sản phẩm bán kỷ luật

| Trục | Nhóm **HY VỌNG** | Nhóm **KỶ LUẬT** |
|---|---|---|
| Đại diện | Cryptohopper · 3Commas · Coinrule · Altrady · signal group | TiltGuard · Zero Tilt · Mettle · Plancana · A-Trader · Edgewonk |
| Quy mô lớn nhất công bố | **500.000** (Cryptohopper) [UseThisAI.fyi, 29/07/2026] | **12.000** (Zero Tilt, tự công bố) [zerotilt.io, 29/07/2026] |
| Dải giá | $19–$140/tháng [ComparEdge 08/07 & 17/07/2026; uwuu.ai 08/05/2026] | $0 (Mettle, Plancana) · mua đứt (TiltGuard, Edgewonk $197) · không công khai (Zero Tilt, A-Trader) |
| Mô hình thu tiền | Subscription bậc thang, có bậc $99–140 | **Mua đứt / free / giấu giá** |
| Lời hứa | "kiếm được tiền khi bạn đang ngủ" | "bạn sẽ mất ít tiền hơn" |
| Cảm xúc lúc mua | Hưng phấn | Hối hận |

**Đọc bảng này ra tiền**: nhóm hy vọng dám định giá $99–140/tháng và có công ty đạt nửa triệu người
dùng. Nhóm kỷ luật **không sản phẩm nào công bố được một mức giá subscription cao**, và ba trong sáu
sản phẩm **giấu giá hoặc bán mua đứt** — hành vi điển hình của thị trường mà người bán biết là
willingness-to-pay thấp và churn cao.

### 2.2 Hai mô hình doanh thu thật của phân khúc journal crypto — và cả hai đều không phải subscription

**Mô hình 1 — CoinMarketMan: giá $0, sống bằng affiliate rebate của sàn.**
90% người dùng ở gói UNLOCKED miễn phí trọn đời [coinmarketman.com/pricing, truy cập 29/07/2026].
Nghĩa là **giá sàn của phân khúc không phải $6, mà là $0**, được sàn tài trợ. Bảng giá công khai
($699,99/năm PRO) chỉ là **giá neo tâm lý** để làm gói miễn phí trông giá trị — không phải nguồn
doanh thu chính.

**Mô hình 2 — Trader Make Money: $6/tháng làm mồi, 170k user, phần lớn ở Free.**
Gói Novice+ **$6/tháng** [tradermake.money/prices, truy cập 29/07/2026] là mức giá của một sản phẩm
đã chọn chiến lược **volume over margin**, và có **AI Coach bắt đúng `OversizedAfterLoss`** — trùng
1-1 với một trong ba detector của MMW — **ở cả gói $6**, cộng **MCP server miễn phí trên MỌI gói kể
cả Free** [tradermake.money, truy cập 29/07/2026].

**Hàm ý tài chính trực tiếp cho MMW**: bạn không cạnh tranh với $6. Bạn cạnh tranh với **$0 được sàn
trả tiền**, và với một đối thủ đã tặng miễn phí đúng cái tính năng AI mà bạn coi là giá trị lõi.
Một sản phẩm mới, một sàn, không brand, không community, đòi khoá đặt lệnh, tính $12–19/tháng —
**không có đường vào**.

### 2.3 Kênh phân phối Việt Nam bán chính xác thứ ngược lại

Nguyên văn từ một nhóm Telegram trader Việt: *"Group Signals VIP ⭐️ 3-6 signals per day ⭐️ 3 goals
per transaction ⭐️ **80-90% chance of winning** ⭐️ 2000 pips per month"*
[t.me/congdongtradevietnam, truy cập 29/07/2026].

MMW hứa: *"hệ thống không hứa hẹn tín hiệu thắng; nó hứa hẹn chặn lệnh sai kỷ luật"* (`spec.md:23`).

Đây không phải khác biệt về thông điệp marketing — đây là **khác biệt về sản phẩm mà người mua đang
tìm**. Bán hàng chống-hưng-phấn vào một kênh sống bằng hưng phấn có chi phí thu hút khách (CAC) cao
gấp nhiều lần bình thường. Và đường qua KOL bị khoá bởi nghịch động lực cấu trúc mà file 01 §4.3 đã
chỉ ra: **KOL Việt sống bằng affiliate rebate; MMW làm người ta giao dịch ít hơn và nhỏ hơn, tức là
làm giảm chính doanh thu của KOL.**

### 2.4 Vậy định vị nào còn lại?

Nếu thị trường trả tiền cho hy vọng nhiều hơn kỷ luật gấp 12–15 lần, có đúng **ba** lựa chọn định vị,
và chỉ một cái là trung thực:

| Lựa chọn | Nội dung | Đánh giá |
|---|---|---|
| **A. Đổi thông điệp, giữ sản phẩm** — bán "AI tìm setup chất lượng" thay vì "chặn lệnh sai" | Dùng `MarketScanService` làm mặt tiền, giấu rule engine phía sau | ❌ **Không nên.** Bạn sẽ cạnh tranh trực diện với Cryptohopper/3Commas ở đúng chỗ họ mạnh nhất, với 1 sàn so với 22–30 sàn. Và bạn sẽ **hứa điều bạn không kiểm chứng được** — MMW chưa có bất kỳ track record công khai nào. Đây là con đường ngắn nhất tới rủi ro CTA [FinanceFeeds dẫn Adam Tracy, 20/05/2026]. |
| **B. Giữ thông điệp, đổi người mua** — bán cho ai đã có tiền của người khác để bảo vệ | Prop firm, quỹ nhỏ, đội trading | ✅ **Đây là lựa chọn đúng.** Người mua B2B mua *giảm rủi ro*, không mua *hy vọng*. Họ đã cầm tài khoản của trader → giải luôn rào cản niềm tin lớn nhất. 40+ crypto prop firm tồn tại [CryptoFundTrader, 24/12/2025]. |
| **C. Không bán** | Dùng cho chính mình | ✅ **Rẻ nhất, chắc chắn nhất.** Xem mục 6. |

TiltGuard đã đi đường B (nhắm prop firm evaluation) nhưng tự khoá mình ở tầng trình duyệt:
*"It does not place trades. It does not access brokerage funds. It does not modify order execution."*
[tiltguard.app/features, truy cập 29/07/2026]. **Đó là chỗ MMW có lợi thế thật và duy nhất.**

---

## 3. Mô hình doanh thu — chấm điểm 9 phương án

### 3.1 Quy ước chấm điểm

Thang **1–10** trên 4 trục. **Lưu ý cách đọc trục pháp lý: 10 = rủi ro pháp lý THẤP NHẤT, 1 = cao nhất.**
Ba trục còn lại: 10 = tốt nhất.

- **KT** = khả thi kỹ thuật (dựa trên 82,5–124,5 ngày-người còn thiếu [file 02 §6.3] và 17,5 giờ/tuần khả dụng)
- **TĐ** = tốc độ ra tiền (thời gian tới dollar đầu tiên)
- **TR** = trần doanh thu (neo vào SOM $0,6–1,3 triệu/năm toàn cầu, mục 1.6)
- **PL** = mức an toàn pháp lý (VN NQ05 + US CTA + EU MiCA/CASP, file 01 §5)

**Mốc hoà vốn dùng chung**: mục tiêu **$1.730/tháng ròng** = ~45 triệu VND, tương đương thu nhập
một senior .NET dev tại VN *(giả định — xem Phụ lục A2 về cách dựng con số này)*.
Mọi tính toán biên gộp đều dùng **kiến trúc SAU khi sửa tần suất LLM** (mục 5.6); nếu không sửa,
**toàn bộ 9 phương án B2C đều có biên âm và số hoà vốn là vô cực**.

### 3.2 Bảng chấm điểm tổng hợp

| # | Phương án | KT | TĐ | TR | PL | **Tổng** | Giá gói (USD/tháng) | User hoà vốn ($1.730/th) | Tới dollar đầu tiên |
|---|---|---:|---:|---:|---:|---:|---|---|---|
| 1 | SaaS subscription B2C | 4 | 2 | 4 | 3 | **13** | $19 (dải $9–29) | **109** | 10–14 tháng |
| 2 | Freemium | 3 | 2 | 3 | 3 | **11** | $0 / $19 | **109 trả phí ⇒ ~3.600 free** | 12–16 tháng |
| 3 | Copy-trading fee | 3 | 2 | 6 | **1** | **12** | 10–20% performance | ~$17k AUM-profit/tháng | 12–18 tháng |
| 4 | Prop firm / funded account (tự vận hành) | **1** | 3 | 8 | 2 | **14** | $100–500/challenge | 4–17 challenge/tháng | 18–30 tháng |
| 5 | **B2B — bán cho prop firm / sàn** ⭐ | 6 | 5 | 7 | **7** | **25** | **$500–2.000/firm** hoặc $2–5/seat | **1–3 firm** | 6–12 tháng |
| 6 | Khoá học + công cụ | **9** | **8** | 3 | 5 | **25** | $99–299 mua đứt | 6–17 lượt bán/tháng | **1–3 tháng** |
| 7 | Affiliate sàn (mô hình CMM) | **10** | **9** | 6 | **2** | **27** | $0 cho user | **41–96 trader active** | **1–2 tháng** |
| 8 | Marketplace rule/strategy | 3 | 1 | 3 | 4 | **11** | 20–30% commission | >500 giao dịch/tháng | 18–24+ tháng |
| 9 | **Open-source + sponsor / license self-host** | 8 | 6 | **1** | **9** | **24** | $5–50 sponsor · $200–500 license | không đạt được | 3–6 tháng |

> **Cảnh báo về cách đọc bảng này**: tổng điểm cao **không** đồng nghĩa nên làm. Phương án 7
> (affiliate) đứng đầu bảng vì nó rẻ và nhanh, nhưng nó có **hai vấn đề giết chết** mà điểm số không
> thể hiện — xem 3.3.7. Phương án 5 là phương án duy nhất mà tôi khuyến nghị thăm dò.

### 3.3 Phân tích từng phương án

#### 3.3.1 — SaaS subscription B2C · **13/40**

| Trục | Điểm | Lý do |
|---|---:|---|
| Khả thi kỹ thuật | 4 | Cần trọn Giai đoạn A + B + C = **82,5–124,5 ngày-người** [file 02 §6.3]. Ở 17,5 giờ/tuần ≈ 2,2 ngày-người/tuần → **38–57 tuần** chỉ để build, chưa bán được đồng nào. |
| Tốc độ ra tiền | 2 | 10–14 tháng. Không có đường tắt: Giai đoạn B (billing, onboarding, landing, ToS) là 32–49 ngày-người và **không thể bỏ qua** với B2C. |
| Trần doanh thu | 4 | SOM-MMW = **21–148 khách** (mục 1.6) → $3.000–33.700/năm gộp. Trần thật, không phải trần lý thuyết. |
| Rủi ro pháp lý | 3 | Cao. AI sinh đề xuất futures + cầm khoá + tự đặt lệnh = **ô đỏ trong bảng file 01 §5.4** (VN Cao / US Cao-CTA / EU Cao-CASP). |

**Kinh tế đơn vị (sau refactor LLM)**:

| Khoản | $9 | $12 | $19 | $29 |
|---|---:|---:|---:|---:|
| Doanh thu gộp | 9,00 | 12,00 | 19,00 | 29,00 |
| Phí thanh toán (Paddle ~5% + $0,50, giả định) | −0,95 | −1,10 | −1,45 | −1,95 |
| LLM sau refactor (ước lượng, mục 5.6) | −1,00 | −1,00 | −1,00 | −1,00 |
| Hạ tầng phân bổ @100 user (ước lượng) | −0,60 | −0,60 | −0,60 | −0,60 |
| **Biên gộp** | **6,45 (72%)** | **9,30 (78%)** | **15,95 (84%)** | **25,45 (88%)** |
| **User hoà vốn $1.730/tháng** | **268** | **186** | **109** | **68** |

**Đối chiếu chí mạng**: cần **109 khách ở $19** hoặc **186 khách ở $12**. SOM-MMW là **21–148**.
Chỉ mức $19+ mới có cửa lý thuyết, và chỉ ở **đúng trần lạc quan nhất của SOM**. Mà $19 nằm ngay
ranh giới trên của dải khả thi mà file 01 §2.4 xác định ($9–19), nơi bạn bắt đầu chạm TradesViz Pro
$24,99 và TraderSync Pro $29,95 — những sản phẩm có 600+ cách cắt dữ liệu và auto-import broker rộng.

**Kết luận**: bài toán không đóng. **Không khuyến nghị.**

#### 3.3.2 — Freemium · **11/40**

| Trục | Điểm | Lý do |
|---|---:|---|
| Khả thi kỹ thuật | 3 | Tất cả của #1, **cộng** hệ thống hạn mức (B2 = 5–8 ngày-người) và đo chi phí theo user (B4 = 4–6 ngày-người) [file 02 §6.2]. |
| Tốc độ ra tiền | 2 | 12–16 tháng. Freemium luôn chậm hơn paid-only. |
| Trần doanh thu | 3 | Ở tỷ lệ chuyển đổi 3% (giả định — CMM cho thấy ≤10% và thực tế 90% ở free), cần **~3.600 user free** để có 109 trả phí. SOM tổng chỉ có 4.200–5.900 người *chịu trả tiền*; tập free lớn hơn nhưng bạn không có kênh để tiếp cận 3.600 người. |
| Rủi ro pháp lý | 3 | Như #1. |

**Lý do bác bỏ mang tính kiến trúc, không phải marketing**: trong MMW, **một user free vẫn kích hoạt
đầy đủ 288 lượt `market-scan`/ngày** vì job chạy theo watchlist chứ không theo gói dịch vụ
(`MarketScanService.cs:159`, quét mọi watch item). Nghĩa là **mỗi user miễn phí đốt $48–197/tháng
tiền LLM của bạn**. Freemium ở đây không phải chiến lược tăng trưởng — nó là **máy đốt tiền tuyến
tính theo số người không trả tiền**. Kể cả sau refactor, free tier vẫn phải bị chặn khỏi mọi lời gọi
AI, tức là gói free chỉ còn journal thuần — cạnh tranh trực tiếp với TMM Free và CMM UNLOCKED
(cả hai đều tốt hơn và có 10 sàn).

**Kết luận**: **Không khuyến nghị.**

#### 3.3.3 — Copy-trading fee · **12/40**

| Trục | Điểm | Lý do |
|---|---:|---|
| Khả thi kỹ thuật | 3 | Cần: track record đã kiểm chứng, hạ tầng phân bổ lệnh, tính phí theo high-water mark. MMW hiện **không có track record nào**, và tệ hơn: `TradeResultSyncService.cs:134` + `TradesController.cs:458` hardcode `useTestnet: false` trong khi `LiveTrading.UseTestnet=true` là mặc định (`spec.md:329`) → **sổ giao dịch hiện tại trộn venue testnet/mainnet** [file 02, C-01 🔴]. Nghĩa là **chính dữ liệu để chứng minh track record đang sai**. |
| Tốc độ ra tiền | 2 | 12–18 tháng, và tháng đầu tiên chỉ đến sau khi có ≥6 tháng lịch sử sạch. |
| Trần doanh thu | 6 | Cao nhất trong nhóm B2C nếu thành công. Ở phí 20% và mục tiêu $1.730/tháng, cần tạo ra **~$8.650 lợi nhuận/tháng** cho follower. |
| Rủi ro pháp lý | **1** | **Thấp nhất bảng.** Đây là quản lý tài sản người khác. US: CTA + có thể CPO [FinanceFeeds dẫn Adam Tracy, 20/05/2026]. EU: vượt xa vùng xám CASP. VN: NQ05 đặt hoạt động này vào đúng vùng phải cấp phép, mà điều kiện là **vốn điều lệ đã góp tối thiểu 10.000 tỷ VND** [Dentons LuatViet, 22/10/2025]. Và tiền lệ đã có: 12/2025 SEC kiện 3 nền tảng + 4 investment club lừa đảo **$14 triệu** với chiêu bài *"tín hiệu do AI sinh"* [FinanceFeeds, 20/05/2026]. |

**Kết luận**: **Không. Đây là phương án duy nhất trong bảng mà tôi khuyến nghị loại bỏ hoàn toàn,
không thăm dò.**

#### 3.3.4 — Prop firm / funded account tự vận hành · **14/40**

| Trục | Điểm | Lý do |
|---|---:|---|
| Khả thi kỹ thuật | **1** | Cần vốn thật để cấp cho trader, KYC/AML, cổng thanh toán, pháp nhân, hệ thống đánh giá, marketing, chăm sóc. Một dev đơn lẻ 17,5 giờ/tuần **không thể**. Đây không phải dự án phần mềm. |
| Tốc độ ra tiền | 3 | 18–30 tháng. |
| Trần doanh thu | 8 | Ngành $20 tỷ (2025) [CryptoFundTrader, 24/12/2025]. Ở $200/challenge (giả định), cần **9 challenge/tháng** để đạt $1.730 — nghe dễ, nhưng đó là doanh thu gộp trước chi phí marketing, payout và vốn. |
| Rủi ro pháp lý | 2 | Nhận tiền của công chúng để cấp "tài khoản ảo" là mô hình đang bị soi kỹ ở nhiều tài phán. Ở VN sau NQ05 là vùng đỏ. |

**Kết luận**: **Không khả thi cho một cá nhân.** Nhưng nó chỉ ra đúng khách hàng của #5.

#### 3.3.5 — ⭐ B2B: bán cho prop firm / sàn · **25/40** — PHƯƠNG ÁN KHUYẾN NGHỊ THĂM DÒ

| Trục | Điểm | Lý do |
|---|---:|---|
| Khả thi kỹ thuật | 6 | **Bỏ được gần trọn Giai đoạn B (32–49 ngày-người)**: không cần Stripe/Paddle (xuất hoá đơn), không cần landing page/pricing page, không cần onboarding tự phục vụ, không cần đăng ký/quên mật khẩu. Vẫn cần A5 (mã hoá khoá, 3–5 nd), A6 (bịt IDOR, 3–4 nd), C6 (trả nợ test, 8–12 nd), C2 (rate-limit, 3–5 nd) và **đa sàn** (D3 trong file 01 §8). Ước lượng **35–55 ngày-người** thay vì 82,5–124,5. |
| Tốc độ ra tiền | 5 | 6–12 tháng, phần lớn là chu kỳ bán chứ không phải chu kỳ code. **Và bước thăm dò đầu tiên có chi phí bằng 0** (5 email). |
| Trần doanh thu | 7 | 40+ crypto prop firm [CryptoFundTrader, 24/12/2025]. Ở $1.000/firm/tháng (giả định), 10 firm = $10.000/tháng = **gấp 5,8 lần mục tiêu lương**. |
| Rủi ro pháp lý | **7 — tốt nhất trong nhóm có doanh thu** | Bạn là **nhà cung cấp phần mềm cho pháp nhân**, không phải cố vấn cho cá nhân. **Prop firm đã cầm tài khoản/khoá của trader rồi** → bạn không cầm khoá của người lạ → thoát CASP, giảm mạnh CTA. Rủi ro NQ05 chuyển sang khách hàng và biến mất nếu khách là pháp nhân ngoài VN. |

**Kinh tế đơn vị B2B (giả định)**:

| Mô hình giá | Giá | Firm hoà vốn ($1.730/th) | Ghi chú |
|---|---|---:|---|
| Flat per firm — nhỏ (<50 trader) | $500/tháng | **4** | |
| Flat per firm — trung (50–300 trader) | $1.500/tháng | **2** | |
| Per-seat | $3/trader/tháng | **~580 seat** ≈ 2–4 firm | Biên tốt hơn nhưng đàm phán khó hơn |
| **Chi phí biên/firm (sau refactor, ước lượng)** | **$30–120/tháng** | | LLM dùng chung ở tầng symbol + advisor event-driven |
| **Biên gộp** | **76–94%** | | |

**Sự bất đối xứng đáng chú ý nhất trong toàn bản thẩm định**:

> **1–4 khách hàng B2B thay thế được 109–186 khách hàng B2C.**
> Và chi phí để có khách B2B đầu tiên là **5 email + 1 buổi demo**, trong khi chi phí để có 109 khách
> B2C là 82,5–124,5 ngày-người build + một kênh phân phối bạn chưa có.

**Ba rủi ro phải nói rõ**:
1. **Prop firm có thể tự xây.** Enforcement rule là logic họ đã có ở tầng tài khoản. Điều họ **chưa**
   có là chặn ở tầng lệnh + tín hiệu hành vi. Nhưng đây là rủi ro "build vs buy" cổ điển và bạn ở
   phía yếu (không thương hiệu, không SLA, không pháp nhân).
2. **A-Trader đã đi trước** với 22 tín hiệu hành vi và **SDK-callable** [arizet.com, truy cập
   29/07/2026]. Nếu họ thêm execution gating, khoảng trống biến mất.
3. **Bạn chưa từng bán B2B.** Chu kỳ bán 3–9 tháng (giả định), cần SLA, cần hợp đồng, cần pháp nhân
   để nhận thanh toán quốc tế. Đây là kỹ năng, không phải tính năng.

**Kết luận**: **phương án duy nhất mà toán học đóng được.** Chi phí thăm dò gần bằng 0 → **nên làm
bước thăm dò ngay, độc lập với mọi quyết định khác** (khớp với D8 trong file 01 §8).

#### 3.3.6 — Khoá học + công cụ · **25/40**

| Trục | Điểm | Lý do |
|---|---:|---|
| Khả thi kỹ thuật | **9** | **Không cần viết dòng code nào.** Nguyên liệu đã tồn tại và đã viết xong: `.specify/memory/constitution.md` v1.0.0 (7 nguyên tắc), `spec.md` (50 FR), và ~2.700 dòng know-how giao dịch thật mà file 02 §7.1 đã định danh từng món (thứ tự 18 lớp chặn, Hedge Mode vs One-way, snap `stepSize` xuống cho qty và lên cho min-notional, tách rào rủi ro nới được vs rào kỹ thuật luôn giữ). |
| Tốc độ ra tiền | **8** | **1–3 tháng — nhanh nhất bảng cùng #7.** |
| Trần doanh thu | 3 | Ở $149/khoá (giả định), cần **12 lượt bán/tháng** để đạt $1.730. Khả thi trong 3–6 tháng đầu, nhưng khoá học **không có doanh thu định kỳ** và cạn nhanh trong một ngách nhỏ. |
| Rủi ro pháp lý | 5 | Trung bình. Dạy về giao dịch không phải hoạt động cấp phép. Nhưng nếu nội dung hướng dẫn dùng sàn chưa cấp phép sau mốc NQ05, đó là vùng xám [Dentons LuatViet, 22/10/2025]. Phải có tuyên bố miễn trừ tư vấn đầu tư rõ ràng. |

**Vấn đề thật, và nó nghiêm trọng**: khoá học bán được cần **track record công khai**. Bạn không có.
Và nội dung "làm sao không cháy tài khoản" phải cạnh tranh với "80-90% chance of winning"
[t.me/congdongtradevietnam, truy cập 29/07/2026] trên đúng một kênh.

**Nhưng có một biến thể đáng cân nhắc và nó khác hẳn**: không bán khoá học cho trader, mà **viết
public về kiến trúc** — "18 lớp chặn cho lệnh futures crypto: những gì tôi học được sau khi ăn đủ lỗi
-4061, -1106, -1111". Đây là nội dung kỹ thuật, đúng thứ bạn có thật, và nó nuôi cả #5 (B2B) lẫn #9
(open-source) lẫn giá trị nghề nghiệp cá nhân. **Doanh thu ≈ 0, giá trị chiến lược cao.**

#### 3.3.7 — Affiliate sàn (mô hình CoinMarketMan) · **27/40** — điểm cao nhất, và tôi vẫn khuyên không

| Trục | Điểm | Lý do |
|---|---:|---|
| Khả thi kỹ thuật | **10** | Thêm một referral link. Nửa ngày. |
| Tốc độ ra tiền | **9** | 1–2 tháng. |
| Trần doanh thu | 6 | Xem tính toán dưới. |
| Rủi ro pháp lý | **2** | Xem 3.3.7-b. |

**a) Toán học (mọi con số dưới đây là giả định — biểu phí affiliate của Binance không công khai chi tiết)**:

| Thông số | Giá trị | Ghi chú |
|---|---|---|
| Tài khoản trader tiêu biểu | $2.000 | (giả định) |
| Đòn bẩy hiệu dụng | 5x | (giả định — thấp hơn mặc định 20x của MMW ở `spec.md:329`) |
| Vòng lệnh/ngày (mở + đóng) | 1 | (giả định) |
| Volume/tháng/trader | $600.000 | $2.000 × 5 × 2 × 30 |
| Phí blended (50% maker 0,02% / 50% taker 0,05%) | 0,035% | (giả định) |
| Phí sàn thu/trader/tháng | $210 | |
| Tỷ lệ rebate cho partner nhỏ | 20% | (giả định) |
| **Doanh thu/trader/tháng** | **$42** | |
| **Trader active cần để đạt $1.730/tháng** | **41** | |

**b) Hai vấn đề giết chết phương án này, và cả hai đều không sửa được bằng code:**

**Vấn đề 1 — nghịch động lực nằm ngay trong sản phẩm.** MMW tồn tại để làm người ta giao dịch **ít
hơn và nhỏ hơn**. Affiliate trả tiền cho volume. Nếu MMW hoạt động đúng như quảng cáo và cắt 40% số
lệnh + 30% size của người dùng, doanh thu affiliate rơi:
`(1 − 0,40) × (1 − 0,30) = 0,42` → **giảm 58%**, từ $42 xuống **$17,64/trader/tháng**
→ số trader cần tăng từ 41 lên **98**.

Nói thẳng: **bạn kiếm tiền nhiều nhất khi sản phẩm của bạn thất bại.** Đây là xung đột lợi ích với
chính người dùng, và nó là xung đột *cấu trúc*, không phải xung đột *đạo đức có thể quản lý bằng
minh bạch*. Bất kỳ trader tinh ý nào nhìn ra điều này sẽ mất niềm tin vào toàn bộ sản phẩm — mà niềm
tin là tài sản duy nhất bạn có.

**Vấn đề 2 — pháp lý VN, và nó là vùng đỏ nhất trong tất cả.** Sau mốc 6 tháng kể từ khi sàn nội địa
đầu tiên được cấp phép, người Việt giao dịch qua sàn chưa cấp phép *"tùy theo tính chất, mức độ vi
phạm sẽ bị **xử lý vi phạm hành chính hoặc truy cứu trách nhiệm hình sự**"*
[Dentons LuatViet, 22/10/2025, dẫn NQ05/2025/NQ-CP]. Dự thảo cấm giao dịch trên sàn nước ngoài
**nêu đích danh Binance, OKX, Bybit**, phạt tới 30 triệu VND cá nhân [sanvietnam.com, 06/06/2026].

Affiliate không phải là "hỗ trợ" hành vi đó — nó là **chủ động giới thiệu người Việt vào sàn chưa
cấp phép và ăn hoa hồng trên mỗi giao dịch của họ**. Đó là vị thế pháp lý xấu nhất có thể có, tệ hơn
cả việc bán phần mềm.

**Kết luận**: điểm cao nhất bảng, **và tôi vẫn khuyến nghị KHÔNG**, vì hai vấn đề trên không xuất
hiện trong thang điểm 4 trục. Đây là ví dụ vì sao không được đọc bảng điểm mà bỏ qua phần chữ.

#### 3.3.8 — Marketplace rule/strategy · **11/40**

| Trục | Điểm | Lý do |
|---|---:|---|
| Khả thi kỹ thuật | 3 | Cần multi-tenant hoàn chỉnh + hệ thống publish/version/rating + thanh toán hai chiều. Trên nền 82,5–124,5 ngày-người của #1. |
| Tốc độ ra tiền | **1** | 18–24+ tháng. Marketplace cần cả hai phía; bạn có **0 người dùng**. Bài toán con gà quả trứng ở quy mô 0. |
| Trần doanh thu | 3 | Ở 25% commission và giá rule $10, cần **>690 giao dịch/tháng**. Với SOM 4.200–5.900 người toàn cầu, đó là 12–16% toàn thị trường mua rule mỗi tháng. |
| Rủi ro pháp lý | 4 | Phân phối rule set có tham số vào lệnh cho công chúng bắt đầu giống phân phối lời khuyên đầu tư. |

**Và có một phản biện về sản phẩm, không chỉ về tài chính**: giá trị của rule engine MMW nằm ở chỗ
ngưỡng là **cá nhân hoá theo tài khoản** (`FR-011`, `spec.md:192`). Một marketplace bán rule của
người khác **mâu thuẫn với chính nguyên tắc nền của sản phẩm**. Ngưỡng rủi ro 1%/lệnh của người có
$50.000 không có nghĩa gì với người có $2.000.

**Kết luận**: **Không.**

#### 3.3.9 — Open-source + sponsor / license self-host · **24/40**

| Trục | Điểm | Lý do |
|---|---:|---|
| Khả thi kỹ thuật | 8 | Cần dọn: mã hoá khoá (A5), xoá seed `admin/Admin@123` (`SeedData.cs:11-12`, S-02 🔴), `git rm -r --cached .vs/` (12 file rác gồm `CodeChunks.db` chứa đoạn mã nguồn), nâng AutoMapper 14.0.0 (NU1903/GHSA-rvv3-g6hj-g44x). **Ước lượng 5–8 ngày-người.** |
| Tốc độ ra tiền | 6 | 3–6 tháng tới đồng sponsor đầu tiên, nếu có. |
| Trần doanh thu | **1** | **Gần như bằng 0.** Sponsorship cho một dự án .NET ngách, tiếng Việt, một sàn — thực tế là $0–200/tháng (ước lượng). Đừng tự lừa mình về con số này. |
| Rủi ro pháp lý | **9 — an toàn nhất bảng** | Bạn công bố mã, không cung cấp dịch vụ, không cầm khoá của ai. File 01 §5.4 xếp mô hình này ⚠️ "chấp nhận được" — cao hơn mọi mô hình có doanh thu. |

**Giá trị thật không nằm ở doanh thu mà ở ba chỗ khác, và cả ba đều định lượng được**:
1. **Giá trị nghề nghiệp.** Một repo .NET 8 clean-architecture với execution-layer risk gating là
   portfolio piece hạng nặng. Ở thị trường lao động VN, chênh lệch lương giữa một senior .NET và một
   senior .NET có repo được nhắc tên là **thật** — nếu nó góp phần nâng 10% thu nhập (giả định), đó
   là **4,5 triệu VND/tháng = 54 triệu VND/năm ≈ $2.077/năm**, cao hơn mọi kịch bản doanh thu B2C ở
   năm thứ nhất (mục 6).
2. **Kênh dẫn vào #5.** Prop firm không mua từ người lạ, nhưng họ đọc code.
3. **Bảo hiểm mất dữ liệu.** Push lên remote giải quyết luôn D-01 🔴 (59 ngày công việc chưa commit).

**Kết luận**: **Nên làm, nhưng phải gọi đúng tên — đây là đầu tư vào sự nghiệp và bảo hiểm rủi ro,
không phải mô hình doanh thu.**

### 3.4 Xếp hạng cuối cùng theo khuyến nghị (không theo điểm số)

| Hạng | Phương án | Khuyến nghị |
|---|---|---|
| **1** | #5 B2B prop firm | ✅ **Thăm dò ngay** — 5 email, 0 ngày-người, 0 rủi ro |
| **2** | #9 Open-source | ✅ **Làm sau khi sửa khoá API** — mục tiêu là uy tín + backup, không phải tiền |
| **3** | #6 Viết public về kiến trúc (biến thể của khoá học) | ✅ **Làm song song** — chi phí thấp, nuôi #1 và #2 |
| — | #1 SaaS B2C · #2 Freemium · #4 Prop tự vận hành · #8 Marketplace | ⛔ **Không** — toán học không đóng |
| — | #7 Affiliate | ⛔ **Không** — xung đột lợi ích cấu trúc + vùng đỏ NQ05 |
| — | #3 Copy-trading | ⛔ **Loại bỏ hoàn toàn** — rủi ro pháp lý cá nhân |

---

## 4. Tokenomics — phản biện thẳng thắn

### 4.1 Kết luận trước: **KHÔNG PHÁT HÀNH TOKEN.**

Không phải "chưa phải lúc". Là **không**, cho mọi phiên bản của MMW mà tôi có thể hình dung.

### 4.2 Kiểm tra bốn điều kiện cần của một token có giá trị thật

Một token chỉ tạo giá trị kinh tế thật khi thoả **ít nhất một** trong bốn điều kiện. MMW **trượt cả bốn**:

| # | Điều kiện | MMW có? | Bằng chứng |
|---|---|---|---|
| 1 | **Có trạng thái chung cần đồng thuận phi tập trung** — nhiều bên không tin nhau phải thống nhất một sổ cái | ❌ **Không** | Toàn bộ dữ liệu MMW là **dữ liệu riêng tư của một người**: lệnh, khoá API, cờ vi phạm, ngưỡng rủi ro. Không có bên thứ hai nào cần xác thực nó. `MmwDbContext` là nguồn sự thật duy nhất và **đúng ra phải như vậy**. |
| 2 | **Có tài nguyên khan hiếm cần phân bổ bằng thị trường** — băng thông, lưu trữ, năng lực tính toán của mạng lưới | ❌ **Không** | Tài nguyên duy nhất khan hiếm là **hạn mức gọi LLM** — và nó đã có một cơ chế phân bổ hoàn hảo tên là **tiền**. Thay USD bằng token chỉ thêm một tầng biến động giá giữa chi phí (USD, trả cho Google/DeepSeek) và doanh thu (token). |
| 3 | **Có hiệu ứng mạng cần bootstrap bằng trợ cấp** — giá trị của mạng tăng theo số người tham gia | ❌ **Không** | MMW là công cụ **single-player**. Việc bạn có kỷ luật không làm tăng giá trị MMW cho tôi. Không có hiệu ứng mạng nào để trợ cấp. |
| 4 | **Có tài sản/dòng tiền chung cần phân phối minh bạch** — quỹ, pool, doanh thu giao thức | ❌ **Không** | Doanh thu = $0. Không có pool. Không có treasury. |

**Trượt 4/4.** Đây không phải chấm điểm chặt — đây là bốn câu hỏi cơ bản mà bất kỳ tokenomics nào
cũng phải trả lời được ít nhất một.

### 4.3 Bốn thiết kế token thường được đề xuất — và vì sao từng cái sụp đổ

| Thiết kế | Nghe có vẻ | Sụp đổ ở đâu |
|---|---|---|
| **Token thanh toán** — dùng $MMW mua subscription | "tạo cầu cho token" | Người dùng phải: mua crypto → chuyển vào ví → swap sang $MMW → chịu slippage + gas → chịu biến động giá. **Tệ hơn nghiêm ngặt so với thẻ tín dụng ở mọi chiều.** Và bạn — người trả tiền LLM bằng USD — giờ gánh thêm rủi ro tỷ giá token/USD trên **100% doanh thu**. Đây là **hạ cấp** kinh tế đơn vị, không phải nâng cấp. |
| **Stake để giảm giá / mở tính năng** | "khoá cung, tạo cầu dài hạn" | Đây chỉ là **giảm giá có điều kiện, cộng thêm biến động**. Cùng hiệu quả kinh tế có thể đạt bằng gói trả trước 1 năm giảm 25% — đúng như TradeZella đang làm (annual −25%) [TradeZella blog, 14/07/2026]. Nhưng có thêm một tác dụng phụ chết người: **người dùng bây giờ có vị thế tài chính trong token**, nên khi giá token giảm họ mất tiền vì đã dùng sản phẩm của bạn. Bạn vừa biến khách hàng thành nhà đầu tư bị lỗ. |
| **Governance — vote về tham số rule** | "phi tập trung hoá kỷ luật" | **Governance rỗng ở mức nguy hiểm, không chỉ vô dụng.** `FR-011` (`spec.md:192`) ghi rõ: *"Mọi ngưỡng PHẢI đọc từ cấu hình **theo tài khoản**"*. Ngưỡng rủi ro là **cá nhân, riêng tư, và phải như vậy**. Cho người lạ bỏ phiếu về ngưỡng rủi ro của tôi là **phản sản phẩm**: nó phá đúng nguyên tắc nền số 1. Cái duy nhất có thể vote là tham số mặc định — thứ mà một pull request giải quyết tốt hơn. |
| **Reward chia sẻ dữ liệu hành vi** | "xây dataset, tạo moat" | Dữ liệu hành vi giao dịch là **dữ liệu tài chính cá nhân nhạy cảm nhất** một người có. Trả token để mua nó tạo ra: nghĩa vụ GDPR/PDPA, rủi ro rò rỉ thảm hoạ, và một tập dữ liệu quá nhỏ để có giá trị (SOM = 4.200–5.900 người toàn cầu ở tầng d, mục 1.6). A-Trader xây moat này bằng cách **có sẵn hàng nghìn người dùng** [arizet.com, truy cập 29/07/2026] — không phải bằng token. |

### 4.4 Xung đột động lực — lý do mang tính quyết định

Đây là lập luận mạnh nhất và nó không liên quan gì tới kỹ thuật:

> Một token cần **volume, đầu cơ, người mua mới và narrative tăng trưởng** để giữ giá.
> MMW tồn tại để làm trader **giao dịch ít hơn, nhỏ hơn, và đôi khi không giao dịch gì cả**.

Đây là **cùng một nghịch động lực đã khoá đường KOL** (file 01 §4.3) và **cùng một nghịch động lực
làm affiliate trở thành phương án tồi** (mục 3.3.7-b), nhưng ở dạng nghiêm trọng nhất: bây giờ nó
không nằm ở kênh phân phối mà nằm **trong bảng cân đối của chính bạn**. Ngày token ra mắt, bạn có
một tài sản mà giá của nó tăng khi có nhiều người đầu cơ hơn — và một sản phẩm mà giá trị của nó
tăng khi có ít đầu cơ hơn. **Bạn sẽ không thắng được cuộc chiến đó với chính mình.**

### 4.5 Pháp lý — token biến rủi ro sản phẩm thành rủi ro hình sự cá nhân

| Tài phán | Vấn đề |
|---|---|
| **Việt Nam** | Luật Công nghiệp Công nghệ số hiệu lực 01/01/2026 công nhận tài sản số là **tài sản hợp pháp**, nhưng crypto vẫn **cấm** làm phương tiện thanh toán [Thư Viện Pháp Luật, 17/07/2025; sanvietnam.com, 06/06/2026] → **thiết kế "token thanh toán" ở 4.3 là bất hợp pháp tại VN**. NQ05 giới hạn **tối đa 5 tổ chức** được cấp phép toàn quốc, vốn điều lệ đã góp **≥10.000 tỷ VND** [Dentons LuatViet, 22/10/2025]. Thông tư 32/2026/TT-BTC áp **thuế 0,1% mỗi giao dịch chuyển nhượng** từ 27/03/2026 [Báo Chính phủ, 01/04/2026]. Phát hành token bởi một cá nhân nằm ngoài toàn bộ khung này. |
| **Hoa Kỳ** | Bán token để gây quỹ phát triển một sản phẩm, người mua kỳ vọng lợi nhuận từ nỗ lực của bạn — đây là bài kiểm tra Howey ở dạng sách giáo khoa. SEC + CFTC ra **joint interpretation 17/03/2026** về cách luật chứng khoán áp dụng cho crypto assets [SEC.gov press release 2026-30, 17/03/2026; Sullivan & Cromwell, 19/03/2026]. **Chồng lên rủi ro CTA đã có sẵn** vì MMW là công cụ futures dùng AI sinh đề xuất [FinanceFeeds, 20/05/2026]. |
| **EU** | MiCA — hạn chuyển tiếp CASP **01/07/2026 đã qua** [nhiều nguồn tư vấn pháp lý 2026]. Phát hành + phân phối token cho cư dân EU kích hoạt cả nghĩa vụ whitepaper của MiCA lẫn CASP nếu có giao dịch. |

**Tóm lại về pháp lý**: MMW hiện có rủi ro **thấp** ở mô hình self-host 1 người [file 01 §5.4].
Phát hành token đưa nó thẳng từ ô xanh sang ô đỏ, ở **cả ba tài phán cùng lúc**, và chuyển rủi ro
từ "kinh doanh" sang "cá nhân người phát hành".

### 4.6 Còn trường hợp nào token hợp lý không? Có đúng một — và nó vẫn không cần token

Trường hợp duy nhất tôi có thể xây dựng được một lập luận nghiêm túc:

> Nếu MMW trở thành **tầng chứng thực kỷ luật liên tổ chức** — nơi một prop firm hoặc một nhà cấp
> vốn cần **bằng chứng không thể chối cãi** rằng trader X đã tuân thủ rule Y trong khoảng thời gian Z,
> và bằng chứng đó phải kiểm chứng được bởi bên thứ ba mà không cần tin MMW.

Đây là một sản phẩm có thật và có thị trường (chính là #5 mở rộng). Nhưng ngay cả ở đây:

- Nguyên thuỷ đúng là **attestation có chữ ký + neo hash lên một chuỗi công cộng bất kỳ**
  (~$0,01/lần). Không cần token riêng.
- MMW đã có sẵn hạ tầng cho việc này: `ExchangeApiAuditRecord` + `AiSignalScanRecord` với redact
  secret đúng cách (`BinanceFuturesOrderProvider.cs:518-534`) [file 02 §2, mục 15]. Thêm chữ ký số
  và neo Merkle root là **~3–5 ngày-người (ước lượng)**, không phải một tokenomics.
- Và điều kiện tiên quyết vẫn chưa có: **khách hàng thứ hai.**

**Do đó**: kể cả kịch bản duy nhất mà blockchain có ích cho MMW cũng **không cần một token**.
Nó cần một chữ ký và một hash.

### 4.7 Phán quyết

| Câu hỏi | Trả lời |
|---|---|
| Token có tạo giá trị THẬT không? | **Không.** Trượt cả 4 điều kiện cần (4.2). |
| Hay chỉ là huy động vốn trả bằng uy tín? | **Đúng — và đây chính xác là bản chất.** Bạn đổi uy tín cá nhân, tên thật, và (ở VN) rủi ro pháp lý cá nhân lấy vốn mà bạn không thực sự cần: đường đi đúng (mục 3.4) tốn 5 email và 5–8 ngày-người, không tốn vốn. |
| Nếu CÓ thì thiết kế gì? | **Không có thiết kế nào qua được.** Cả 4 mô hình phổ biến đều sụp (4.3), và mô hình duy nhất có logic (4.6) không cần token. |
| Nếu KHÔNG thì tại sao? | 4 lý do độc lập, **mỗi lý do đủ để dừng**: (1) không có trạng thái chung/hiệu ứng mạng/tài nguyên khan hiếm/dòng tiền chung để token hoá; (2) xung đột động lực nằm trong bảng cân đối — token cần đầu cơ, sản phẩm chống đầu cơ; (3) hạ cấp kinh tế đơn vị — chi phí USD, doanh thu token biến động; (4) chuyển rủi ro pháp lý từ kinh doanh sang cá nhân ở cả VN, US, EU cùng lúc. |

---

## 5. Đơn vị kinh tế (unit economics)

> **Cảnh báo nguồn**: `WebSearch`/`WebFetch` lỗi backend tại 2026-07-30, **không xác minh được biểu
> giá LLM hiện hành**. Toàn bộ đơn giá dưới đây là **(ước lượng)** dựa trên mặt bằng giá 2025–đầu 2026,
> trình bày theo **3 tầng giá** để kết luận không phụ thuộc vào một con số. File 01 §4.4 đã đánh dấu
> khoản này là "−$0,50 đến −$3,00 (ước lượng)" — **tính toán dưới đây cho thấy con số đó thấp hơn
> thực tế 40–65 lần**, và đây là hiệu chỉnh quan trọng nhất mà bản thẩm định này đóng góp.

### 5.1 Đếm số lời gọi LLM — từ mã nguồn, không phải phỏng đoán

| Job | Chu kỳ | Nhân với | Lời gọi/ngày | Nguồn |
|---|---|---|---:|---|
| `market-scan` | 5 phút → **288 lượt/ngày** | **× số symbol trong watchlist** — vòng lặp `foreach` qua mọi watch item | **288 × W** | `spec.md:319`; `MarketScanService.cs:157-227` |
| `market-scan` repair | khi JSON hỏng → gọi lại 1 lần | ~10% (giả định) | 28,8 × W | `MarketScanService.cs:94-96` (`AiSignalRepairPrompt`) |
| preflight vòng 2 | mỗi **đề xuất** vượt cửa | ~3% số lượt quét (giả định) | 8,6 × W | `FR-022` (`spec.md:209`) |
| `trade-advisor` | 1 phút → **1.440 lượt/ngày** | **× số lệnh đang mở** — `EnhanceWithLlmAsync` cho **từng** trade | **1.440 × T** | `spec.md:321`; `TradeAdvisorService.cs:87-92, 230-251` |

**Điểm khuếch đại mà file 01 chưa bắt được**: cả hai job đều **nhân với một biến**, không phải hằng số.
Đề bài nêu "288 lần/ngày/user" cho market-scan — con số đúng là **288 × số symbol**. Với watchlist 10
symbol, đó là **2.880 lời gọi/ngày**, không phải 288.

**Tổng lời gọi/user/tháng (30 ngày)**, W = 10 symbol, T = 5 lệnh mở (theo đúng đề bài):

| Nguồn | Lời gọi/tháng |
|---|---:|
| market-scan | 86.400 |
| repair | 8.640 |
| preflight | 2.580 |
| trade-advisor | **216.000** |
| **TỔNG** | **313.620 lời gọi/user/tháng** |

### 5.2 Kích thước mỗi lời gọi *(ước lượng, đo từ mã nguồn)*

| Lời gọi | Input | Output | Cơ sở ước lượng |
|---|---:|---:|---|
| market-scan (payload đầy đủ) | **~2.500 token** | ~150 | System prompt ~2.400 ký tự tiếng Việt (`MarketScanService.cs:31-92`) ≈ 1.100 token; payload gồm **24 nến gần nhất × 6 trường** + technical snapshot + riskPolicy + account + marketNews + outputRules (`MarketScanService.cs:344-432`) ≈ 1.400 token |
| repair | ~2.700 | ~150 | payload gốc + prompt sửa lỗi |
| preflight vòng 2 | ~3.000 | ~400 | payload phong phú hơn, output có SL/TP đề xuất lại |
| trade-advisor | **~300** | ~120 | System prompt ~350 ký tự + user message ~450 ký tự (`TradeAdvisorService.cs:27-32, 234-243`); output "tối đa 3 câu" |

*Ghi chú tokenizer: tiếng Việt có dấu tốn token hơn tiếng Anh đáng kể (≈2–2,5 ký tự/token thay vì
~4). Ước lượng trên đã tính điều này.*

**Tổng token/user/tháng (W=10, T=5)**:

| Nguồn | Input (triệu token) | Output (triệu token) |
|---|---:|---:|
| market-scan | 216,0 | 12,96 |
| repair | 23,3 | 1,30 |
| preflight | 7,7 | 1,03 |
| trade-advisor | 64,8 | 25,92 |
| **TỔNG** | **311,8 M** | **41,2 M** |

### 5.3 Chi phí LLM ở ba tầng giá *(mọi đơn giá là ước lượng — không xác minh được tại 07/2026)*

| Tầng | Đơn giá (in / out per 1M token) | Đại diện | Input | Output | **Tổng/user/tháng** |
|---|---|---|---:|---:|---:|
| **A** — flash/frontier rẻ | $0,30 / $2,50 | Gemini 2.5 Flash — **mặc định hiện tại** (`LlmOptions.cs:9`) | $93,54 | $103,00 | **$196,54** |
| **B** — DeepSeek-class | $0,27 / $1,10 | DeepSeek chat | $84,19 | $45,32 | **$129,51** |
| **C** — siêu rẻ | $0,10 / $0,40 | MiniMax-class | $31,18 | $16,48 | **$47,66** |
| **D** — sàn giả định | $0,05 / $0,15 | *(không tồn tại ở chất lượng dùng được)* | $15,59 | $6,18 | **$21,77** |

### 5.4 Biên gộp — bảng phá sản

Giá bán khả thi: **$9–19/tháng** [file 01 §2.4]. Đối chiếu với Tier B ($129,51):

| Giá gói | Doanh thu | Phí TT (~5%+$0,5) | LLM (Tier B) | Hạ tầng | **Biên gộp** | **Biên %** |
|---|---:|---:|---:|---:|---:|---:|
| $9 | 9,00 | −0,95 | −129,51 | −0,60 | **−122,06** | **−1.356%** |
| $12 | 12,00 | −1,10 | −129,51 | −0,60 | **−119,21** | **−993%** |
| $19 | 19,00 | −1,45 | −129,51 | −0,60 | **−112,56** | **−592%** |
| $29 | 29,00 | −1,95 | −129,51 | −0,60 | **−103,06** | **−355%** |
| $49 | 49,00 | −2,95 | −129,51 | −0,60 | **−84,06** | **−172%** |
| $99 | 99,00 | −5,45 | −129,51 | −0,60 | **−36,56** | **−37%** |
| **$140** (giá Cryptohopper Hero cao nhất khảo sát) | 140,00 | −7,50 | −129,51 | −0,60 | **+2,39** | **+1,7%** |

Kể cả ở Tier C rẻ nhất ($47,66):

| Giá gói | **Biên gộp Tier C** | **Biên %** |
|---|---:|---:|
| $9 | **−40,21** | −447% |
| $12 | **−37,36** | −311% |
| $19 | **−30,71** | −162% |
| $29 | **−20,21** | −70% |
| $49 | **−1,21** | −2,5% |
| $59 | **+8,29** | +14% |

> ## ⚠️ CẢNH BÁO TÀI CHÍNH — ĐỌC KỸ
>
> **Với kiến trúc job hiện tại, MMW không có mức giá nào cho biên gộp dương trong dải giá mà thị
> trường chấp nhận.** Ở mô hình LLM mặc định trong mã (`gemini-2.5-flash`, `LlmOptions.cs:9`),
> điểm hoà vốn biên gộp là **~$140/tháng/user** — cao hơn gói đắt nhất của mọi sản phẩm trong 18 sản
> phẩm khảo sát [file 01 §2.4], và gấp **7,4 lần** giá tham chiếu $19.
>
> **Kết luận không nhạy với sai số**: kể cả nếu ước lượng token của tôi sai **5 lần theo hướng có lợi**,
> chi phí Tier B vẫn là $25,90/user/tháng — vẫn âm ở $19. Kể cả nếu sai **10 lần**, vẫn âm ở $12.
>
> **Đây không phải vấn đề định giá. Đây là vấn đề kiến trúc, và nó phải được sửa TRƯỚC mọi việc khác.**

### 5.5 Bốn chi phí khác — nhỏ hơn nhưng có một quả mìn

| Khoản | Ước tính | Ghi chú |
|---|---|---|
| **Lưu trữ audit** | **~560 MB/user/tháng ≈ 6,7 GB/user/năm** | `AiSignalScanRecord` lưu **prompt + payload + phản hồi thô** cho **mọi** lượt quét kể cả khi từ chối (`FR-021`, `spec.md:208`; `MarketScanService.cs:238-313`). 2.880 bản ghi/ngày × ~4KB = 11,5 MB/ngày. Cộng 216.000 bản ghi advisor. **⚠️ SQL Server Express giới hạn 10 GB → chạm trần trong ~18 tháng với ĐÚNG MỘT người dùng.** Đây không phải vấn đề multi-tenant, đây là vấn đề **của bạn, năm sau**. |
| Compute DB | $0,30–0,60/user/tháng @100 user (ước lượng) | Azure SQL S1 ~$30/tháng (giả định). SQL Server đắt hơn Postgres đáng kể — nếu có ngày thương mại hoá, đây là khoản nên xét lại. |
| App hosting + Hangfire | $0,25–0,60/user/tháng @100 user (ước lượng) | Hangfire in-process, dùng chung DB với app (`Program.cs:126-155`) → tải job đè lên tải web. C1 trong file 02 §6.2. |
| Băng thông | <$0,05/user/tháng (ước lượng) | Không đáng kể. |
| Feed tin vĩ mô | $0–2,00/user/tháng | Provider mặc định là `NoopMacroEventProvider` trả mảng rỗng [file 02 §2, mục 14]. Muốn thật phải mua (TradingEconomics). |

**Một trần kỹ thuật cần biết**: `market-scan` gọi Binance klines cho **mỗi symbol × mỗi user**.
Ở 100 user × 10 symbol × 288 lượt = **288.000 request/ngày ≈ 200 request/phút** liên tục. Bản thân
con số này nằm trong hạn mức weight của Binance Futures, **nhưng** hệ thống hiện **không có
rate-limit, không Polly, không xử lý HTTP 429/418, không backoff luỹ thừa** (P-03,
`Infrastructure/DependencyInjection.cs:44-72`), và `RetryAsync` retry mù cả lỗi 4xx
(`LiveOrderService.cs:531-546`). Job chạy **theo lô** (một lượt quét đẩy toàn bộ watchlist cùng lúc)
→ chế độ hỏng là **burst → 429 → IP ban tạm thời → toàn bộ user mất dữ liệu cùng lúc**.

### 5.6 Kiến trúc sau khi sửa — và đây là chỗ toàn bộ bài toán thay đổi

**Nguyên nhân gốc của chi phí, và nó nằm ở đúng một chỗ:**

Payload gửi AI **nhúng dữ liệu riêng của từng user** — `riskPolicy` (ngưỡng theo tài khoản) và
`account` (tên, tiền tệ, số dư) — ở `MarketScanService.cs:363-381`. Chính vì vậy **một lượt quét
BTCUSDT không thể dùng lại cho user thứ hai**, và chi phí trở thành tuyến tính theo số user.

**Nhưng mã nguồn đã có sẵn lời giải và nó đang nằm không dùng**: `BuildCompactAiSignalPayload`
(`MarketScanService.cs:434-463`) **chỉ chứa symbol + interval + minScore + market + news** — không có
`account`, không có `riskPolicy`. Payload này **có thể chia sẻ giữa mọi user**.

| Thay đổi | Nội dung | Ngày-người (ước lượng) | Tác động |
|---|---|---:|---|
| **R1** | Tách `market-scan` thành 2 tầng: (i) tầng **symbol** dùng payload compact, chạy 1 lần cho toàn hệ thống; (ii) tầng **user** dùng luật cứng deterministic (`MarketScanService.cs:644-669` đã có sẵn: đúng phía giá, RR ≥ ngưỡng, khoảng rủi ro > 0) — **không gọi AI** | 3–5 | Chi phí scan chuyển từ **biến theo user** sang **cố định toàn hệ thống** |
| **R2** | `trade-advisor` chuyển từ cron 1 phút sang **event-driven**: chỉ gọi LLM khi giá đổi ≥0,5%, hoặc khoảng cách tới SL/TP xuống dưới ngưỡng, hoặc `RiskLevel` đổi bậc. Kèm debounce tối thiểu 15 phút/lệnh | 2–3 | **−95% đến −98% lời gọi advisor** |
| **R3** | Cache phản hồi AI theo `(symbol, interval, nến đóng gần nhất)` — trong một cây nến 1h, quét lại 12 lần cho cùng dữ liệu là lãng phí thuần | 1–2 | −60% đến −80% lời gọi scan còn lại |
| **R4** | Đưa `SignalGenerator` thuần quy tắc (mã chết hiện tại, có 5 test [file 02 §2, mục 20]) trở lại luồng chính làm **tầng lọc trước AI**: chỉ gọi AI khi tín hiệu deterministic đã vượt ngưỡng | 1–2 | −50% đến −70% lời gọi scan. **Khớp với D9 trong file 01 §8** |
| **R5** | Tự động dọn `AiSignalScanRecord` cũ (giữ 90 ngày chi tiết, sau đó chỉ giữ tóm tắt) | 1 | Giải quyết trần 10 GB của SQL Express |
| | **TỔNG** | **8–13 ngày-người** | |

**Chi phí sau refactor (ước lượng, Tier B)**:

| Khoản | Trước | Sau |
|---|---:|---:|
| Scan (chia sẻ toàn hệ thống, 20 symbol union, payload compact ~600 in / 150 out) | $84,19/user | **~$56/tháng cho TOÀN BỘ nền tảng** |
| Preflight (chỉ khi có tín hiệu thật, ~30 lượt/user/tháng) | — | **$0,04/user** |
| Advisor (event-driven, ~72 lượt/ngày cho 3 lệnh) | $45,32/user | **$0,46/user** |
| **Tổng chi phí biên/user** | **$129,51** | **≈ $0,50 – $1,50** |
| **Cải thiện** | | **~86× đến 260×** |

**Biên gộp sau refactor**: 72% (@$9) → 88% (@$29). Xem bảng ở mục 3.3.1.

**Đây là kết luận hành động quan trọng nhất của toàn bản thẩm định**: 8–13 ngày-người biến một mô
hình kinh doanh **không tồn tại** thành một mô hình có biên SaaS bình thường. Không có việc nào khác
trong dự án có tỷ lệ đòn bẩy tương tự. Và — quan trọng hơn — **nó có giá trị ngay cả khi bạn không
bao giờ bán cho ai** (mục 6.4).

---

## 6. ROI cho chính tác giả

### 6.1 Định giá thời gian

| Thông số | Giá trị | Nguồn / ghi chú |
|---|---|---|
| Thu nhập bình quân lao động VN Q2/2026 | 9,0 triệu VND/tháng | [Cục Thống kê, Báo cáo KT-XH Q2/2026, qua Thư Viện Pháp Luật, 04/07/2026] |
| Lao động làm công khu vực thành thị | 11,1 triệu VND/tháng | [cùng nguồn] |
| **Senior .NET dev tại HCM/HN, tổng thu nhập** | **45 triệu VND/tháng** | **(giả định)** — không tìm được khảo sát lương công khai đáng tin cho 2026. Con số này là **~4× thu nhập lao động làm công thành thị**, phù hợp với vị trí senior ngành phần mềm. Xem Phụ lục A2 cho phân tích độ nhạy. |
| Tỷ giá USD/VND | 26.000 | **(giả định)** |
| ⇒ Thu nhập tháng | **$1.731** | |
| ⇒ Giá trị giờ (176 giờ/tháng) | **256.000 VND ≈ $9,84/giờ** | |
| Thời gian khả dụng cho dự án | **17,5 giờ/tuần** (trung điểm 15–20) | theo mô tả tác giả |
| ⇒ Ngày-người/tuần (8 giờ/ngày-người) | **2,19** | |

| Mốc | Tuần | Giờ | Ngày-người khả dụng | Chi phí cơ hội (VND) | Chi phí cơ hội (USD) |
|---|---:|---:|---:|---:|---:|
| **6 tháng** | 26 | 455 | **57** | 116,5 triệu | **$4.481** |
| **12 tháng** | 52 | 910 | **114** | 233,0 triệu | **$8.962** |
| **24 tháng** | 104 | 1.820 | **228** | 466,0 triệu | **$17.923** |

### 6.2 Đối chiếu ngân sách thời gian với khối lượng công việc — sự thật đầu tiên

Khối lượng còn thiếu để thương mại hoá: **82,5 – 124,5 ngày-người** [file 02 §6.3].

| Mốc | Ngày-người có | Ngày-người cần (build) | Còn lại cho **bán hàng, marketing, support** |
|---|---:|---:|---|
| 6 tháng | 57 | 82,5 – 124,5 | **−25,5 đến −67,5** ⇒ **chưa build xong**, chưa nói tới bán |
| 12 tháng | 114 | 82,5 – 124,5 | **−10,5 đến +31,5** ⇒ vừa đủ build ở kịch bản lạc quan |
| 24 tháng | 228 | 82,5 – 124,5 | **+103,5 đến +145,5** ⇒ có ngân sách để bán |

> **Sự thật thứ nhất, và nó không thể tranh cãi**:
> **12 tháng làm buổi tối và cuối tuần mua được PHẦN BUILD, không mua được một khách hàng nào.**
> Đồng hồ doanh thu chỉ bắt đầu chạy từ khoảng **tháng thứ 13**.

Và đó là chưa tính: nợ test (C6: 8–12 nd), pháp lý ToS/miễn trừ (B7: cần luật sư ngoài), và
**tuyệt đối không tính** khả năng bạn ốm, bận, hoặc mất động lực trong 52 tuần liên tiếp.

### 6.3 Ba kịch bản × ba mốc — đường thương mại hoá

**Giả định chung (mọi con số là giả định của người phân tích)**: giá $19/tháng, biên gộp $15,95 sau
refactor LLM (mục 3.3.1), churn 8%/tháng (cao — phản ánh vấn đề retention cấu trúc của category mà
file 01 §3.5 đã ghi nhận: *"abandon it after two weeks"* [plancana.com, 12/03/2026]), SOM-MMW
21–148 khách (mục 1.6).

#### Đường 1 — B2C SaaS

| Mốc | Kịch bản | Khách cuối kỳ | Doanh thu ròng luỹ kế | Chi phí cơ hội | **ROI ròng** |
|---|---|---:|---:|---:|---:|
| **6 tháng** | Lạc quan | 5 (beta trả phí) | $160 | $4.481 | **−$4.321** |
| | Cơ sở | 0 | $0 | $4.481 | **−$4.481** |
| | Bi quan | 0 | $0 | $4.481 | **−$4.481** |
| **12 tháng** | Lạc quan | 40 | $1.900 | $8.962 | **−$7.062** |
| | Cơ sở | 8 | $380 | $8.962 | **−$8.582** |
| | Bi quan | 0–2 | $0–100 | $8.962 | **−$8.862** |
| **24 tháng** | Lạc quan | **148** (trần SOM) | $13.400 | $17.923 | **−$4.523** |
| | Cơ sở | 35 | $3.445 | $17.923 | **−$14.478** |
| | Bi quan | 5 | $574 | $17.923 | **−$17.349** |

**Đọc bảng này**: đường B2C **âm ở cả 9 ô**, kể cả ô lạc quan nhất ở mốc xa nhất.

**Phản biện công bằng — giá trị tài sản, không phải dòng tiền.** Ở kịch bản lạc quan 24 tháng, 148
khách × $19 = $2.812 MRR = **$33.744 ARR**. Bội số mua lại micro-SaaS cho một sản phẩm solo, 1 sàn,
không moat, churn cao: **2–3,5× ARR (giả định)** → **giá trị doanh nghiệp $67.000 – $118.000**.
Con số này **thực sự vượt** chi phí cơ hội $17.923.

**Nhưng nó đòi hỏi đồng thời 5 điều, mỗi điều đều không chắc chắn**:
1. Chạm **đúng trần SOM** — 148/148 (mục 1.6)
2. Đồng hồ NQ05 **không kích hoạt** trong 24 tháng — file 01 §5.1 dự kiến cấp phép Q3/2026 → mốc 6
   tháng rơi vào **Q1/2027**, tức là **tháng thứ 6–9 của kế hoạch 24 tháng**. Xác suất né được: thấp.
3. Churn thực **không phá vỡ** mô hình — category này có vấn đề retention cấu trúc đã được chính các
   nhà cung cấp thừa nhận [getmettle.app, 22/06/2026; plancana.com, 12/03/2026]
4. TMM hoặc CMM **không ra tính năng chặn lệnh** (trigger theo dõi trong file 01 §7.5)
5. Tìm được người mua cho một sản phẩm .NET/SQL Server, giao diện tiếng Việt, một sàn

Nhân xác suất chủ quan (giả định: 0,15 × 0,25 × 0,5 × 0,7 × 0,4) ≈ **0,5%**.
**Kỳ vọng có trọng số của nhánh thoái vốn ≈ $340.** Không thay đổi kết luận.

#### Đường 2 — B2B prop firm

Giả định: $1.000/firm/tháng, chi phí biên $80/firm/tháng, chu kỳ bán 4–8 tháng, cần **35–55
ngày-người** build (mục 3.3.5, thay vì 82,5–124,5).

| Mốc | Kịch bản | Firm trả phí | Doanh thu ròng luỹ kế | Chi phí cơ hội | **ROI ròng** |
|---|---|---:|---:|---:|---:|
| **6 tháng** | Lạc quan | 0 (1 pilot miễn phí) | $0 | $4.481 | **−$4.481** |
| | Cơ sở | 0 | $0 | ~$800 (chỉ thăm dò) | **−$800** |
| | Bi quan | 0 (phát hiện họ tự xây) | $0 | ~$800 | **−$800** — *nhưng câu trả lời đáng giá* |
| **12 tháng** | Lạc quan | 2 | $7.360 | $8.962 | **−$1.602** |
| | Cơ sở | 1 (pilot có phí, 3 tháng) | $2.760 | $8.962 | **−$6.202** |
| | Bi quan | 0 | $0 | $8.962 | **−$8.962** |
| **24 tháng** | Lạc quan | **5** | **$66.240** | $17.923 | **+$48.317** ✅ |
| | Cơ sở | 2 | $17.664 | $17.923 | **−$259** (hoà) |
| | Bi quan | 0 | $0 | $17.923 | **−$17.923** |

**Kỳ vọng có trọng số ở mốc 24 tháng** *(giả định: lạc quan 15% · cơ sở 35% · bi quan 50%)*:
`0,15 × $48.317 + 0,35 × (−$259) + 0,50 × (−$17.923)` = `$7.248 − $91 − $8.962` = **−$1.805**

**Gần bằng 0, nhưng với hai đặc điểm quan trọng mà đường B2C không có**:
- **Đuôi phải rất dày**: kịch bản lạc quan trả về +$48.317, gấp 2,7 lần chi phí cơ hội, và có thể
  tiếp tục tăng ở năm thứ 3 (10 firm = $110.000/năm). B2C lạc quan chỉ về −$4.523.
- **Có điểm dừng rẻ**: bước thăm dò tốn **5 email + 1 buổi**. Nếu 5 prop firm đều im lặng hoặc trả
  lời "chúng tôi tự xây", bạn dừng ở mốc **−$800** thay vì −$8.962. **Đây là giá trị của quyền chọn
  (option value), và nó là lý do duy nhất để làm bước này.**

#### 6.4 Đường 3 — ĐỐI CHỨNG: không thương mại hoá, chỉ tự dùng

**Đây là kịch bản mà mọi kịch bản khác phải đánh bại, và phần lớn không đánh bại được.**

**Công việc cần làm (đã hội tụ từ ba nguồn độc lập)**:

| # | Việc | Ngày-người | Nguồn |
|---|---|---:|---|
| 1 | `git add -A && git commit` + push remote riêng tư + `git rm -r --cached .vs/` | **0,25** | file 02 §8.2 (D-01 🔴 — 59 ngày công việc chưa commit) |
| 2 | **Refactor tần suất LLM (R1–R5)** | **8–13** | mục 5.6 — **hạng mục có ROI cao nhất** |
| 3 | Mã hoá `TradingAccount.ApiKey/ApiSecret` (EF `ValueConverter` + DPAPI) | 3–5 | `TradingAccount.cs:29-34` (S-01 🔴); file 01 §8.1; file 02 §8.2 |
| 4 | Test cho 9/18 lớp chặn còn thiếu, ưu tiên #14 (chấm lại rule sau khi tăng qty), #17, #15, #7 | 2–3 | file 02 §4.2 |
| 5 | Sửa C-01 (trộn venue testnet/mainnet) + C-03 (ngày giao dịch theo UTC thay vì UTC+7) | 3–4 | file 02 §5.2 — **hai lỗi này làm SAI chính cơ chế kỷ luật mà sản phẩm hứa** |
| 6 | Sửa C-04 (vốn đầu ngày đang ước lượng) — `DailyLossLimitRule` mức Critical đang dựa vào nó | 1–2 | `TradingDayService.cs:74`; `spec.md:361` |
| | **TỔNG** | **17,25 – 27,25 ngày-người** | ≈ **8–12 tuần** ở 2,19 nd/tuần |

**Chi phí cơ hội: $1.360 – $2.145.** Sau đó **~800 giờ của 12 tháng còn lại được trả về** cho lương,
gia đình, hoặc chính việc giao dịch.

**Lợi ích định lượng — ba khoản, và khoản đầu tiên gây bất ngờ:**

**Khoản A — Tiết kiệm chi phí LLM cho chính bạn. Đây là khoản chắc chắn nhất và không ai nói tới.**

Cấu hình cá nhân giả định: **W = 5 symbol, T = 2 lệnh mở**.

| Nguồn | Lời gọi/tháng | Input (M) | Output (M) |
|---|---:|---:|---:|
| market-scan | 43.200 | 108,0 | 6,48 |
| repair (10%) | 4.320 | 11,7 | 0,65 |
| preflight (3%) | 1.296 | 3,9 | 0,52 |
| trade-advisor | 86.400 | 25,9 | 10,37 |
| **TỔNG** | **135.216** | **149,5** | **18,0** |

| Tầng giá | Chi phí/tháng | Chi phí/năm | **% của tài khoản $5.000** | % của tài khoản $2.000 | % của tài khoản $20.000 |
|---|---:|---:|---:|---:|---:|
| **A** (Gemini 2.5 Flash — **mặc định hiện tại**) | **$89,85** | **$1.078** | **21,6%/năm** | 53,9% | 5,4% |
| **B** (DeepSeek-class) | **$60,17** | **$722** | **14,4%/năm** | 36,1% | 3,6% |
| **C** (MiniMax-class) | **$22,15** | **$266** | **5,3%/năm** | 13,3% | 1,3% |
| **Sau refactor R1–R5** | **$3–6** | **$36–72** | **0,7–1,4%/năm** | 1,8–3,6% | 0,2–0,4% |

> ## ⚠️ CẢNH BÁO THỨ HAI — VÀ NÓ ÁP DỤNG NGAY HÔM NAY, KHÔNG PHẢI "KHI CÓ KHÁCH HÀNG"
>
> Ở mô hình mặc định trong mã (`gemini-2.5-flash`, `LlmOptions.cs:9`) và cấu hình cá nhân khiêm tốn
> (5 symbol, 2 lệnh mở), **MMW đang đốt ~$1.078/năm tiền LLM = 21,6% một tài khoản giao dịch $5.000
> mỗi năm** (ước lượng).
>
> **Đó lớn hơn edge kỳ vọng của phần lớn trader retail.** Một công cụ được xây để bảo vệ vốn đang
> **tiêu thụ vốn với tốc độ cao hơn tốc độ nó bảo vệ**, và nó làm điều đó âm thầm qua hoá đơn API
> chứ không qua sổ giao dịch — nên nó không xuất hiện ở bất kỳ báo cáo PnL nào trong hệ thống.
>
> **8–13 ngày-người refactor đưa con số này về 0,7–1,4%/năm.**
> Tính theo tỷ suất: ~$1.006/năm tiết kiệm chia cho ~$790 chi phí cơ hội (10 nd × $79/nd)
> = **hoàn vốn trong ~9,4 tháng, sau đó lãi vĩnh viễn** — và đó là chưa tính một xu giá trị giao dịch.

**Khoản B — Giá trị bảo vệ vốn.**

Neo duy nhất có nguồn: prop firm báo cáo **chỉ 5–10% trader vượt vòng đánh giá, chỉ 7% từng nhận
payout**, nguyên nhân chủ đạo là **vi phạm rule và quản trị rủi ro kém** [CryptoFundTrader, 24/12/2025
— *nguồn tự công bố, xung đột lợi ích*].

Mô hình (mọi số là giả định): tài khoản $5.000; đòn bẩy mặc định của hệ thống **20x**
(`spec.md:329`); giới hạn kỷ luật mặc định 1%/lệnh, 3%/ngày, 5 lệnh/ngày (`spec.md:327`).

| | Không có enforcement | Có enforcement hoạt động đúng |
|---|---|---|
| Một chuỗi tilt/revenge điển hình | 20–50% tài khoản trong 1–2 phiên (giả định — hệ quả trực tiếp của 20x không có trần lỗ ngày) | Chặn ở **3% = $150** |
| Thiệt hại/lần | $1.000 – $2.500 | $150 |
| Tần suất (giả định — bối cảnh BTC −45,5% từ đỉnh, F&G = 14 [CoinStats AI, 01/07/2026] là điều kiện sinh tilt) | 1–2 lần/năm | |
| **Giá trị bảo vệ/năm** | | **$850 – $4.700** |

**Phải nói thẳng về độ tin cậy của khoản này**: đây là ước lượng có sai số rất lớn, và nó **chỉ hiện
thực hoá nếu (i) bạn thực sự đi qua MMW để vào lệnh chứ không mở app Binance, và (ii) bạn không tự
bật `AllowOverrideRisk` đúng lúc đang tilt** (mục 1.7). Cả hai điều kiện đều nằm ở phía bạn, không
phía phần mềm. Tôi khuyến nghị dùng **cận dưới $850** cho mọi tính toán, và coi phần trên là thưởng.

Và điều kiện tiên quyết: **giá trị này bằng 0 cho tới khi C-01, C-03, C-04 được sửa** — vì hiện tại
"giới hạn 5 lệnh/ngày" và "lỗ tối đa 3%/ngày" đang **reset lúc 07:00 sáng giờ VN** (`TradingDayService.cs:36`),
và vốn đầu ngày là số ước lượng (`TradingDayService.cs:74`). Một cơ chế kỷ luật tính sai ngày và sai
mẫu số **không bảo vệ được gì**.

**Khoản C — Giá trị nghề nghiệp.** Xem mục 3.3.9: nếu repo được mở nguồn và góp phần nâng thu nhập
10% (giả định), đó là **$2.077/năm**. Không chắc chắn, nhưng chi phí cận biên gần bằng 0 vì việc
dọn dẹp đã nằm trong danh sách 1–6 ở trên.

**Tổng hợp đối chứng:**

| Mốc | Chi phí cơ hội | Khoản A (LLM) | Khoản B (bảo vệ vốn, cận dưới) | Khoản C (nghề nghiệp) | **ROI ròng** |
|---|---:|---:|---:|---:|---:|
| **6 tháng** | $1.360 – $2.145 | $503 | $425 | $0 | **−$432 đến −$1.217** |
| **12 tháng** | $1.360 – $2.145 | $1.006 | $850 | $0–2.077 | **+$496 đến +$2.573** ✅ |
| **24 tháng** | $1.360 – $2.145 | $2.012 | $1.700 | $0–4.154 | **+$2.567 đến +$6.721** ✅ |

*(Chi phí cơ hội là một lần, dồn vào 8–12 tuần đầu; lợi ích tích luỹ hàng năm.)*

**Hoà vốn ở khoảng tháng thứ 8–10.** Dương từ đó trở đi, vĩnh viễn, không cần một khách hàng nào,
không có rủi ro pháp lý, không có gánh nặng support.

### 6.5 So sánh trực diện ba đường ở mốc 24 tháng

| | **B2C SaaS** | **B2B prop firm** | **ĐỐI CHỨNG (tự dùng)** |
|---|---:|---:|---:|
| Ngày-người phải bỏ | 82,5 – 124,5 | 35 – 55 | **17,25 – 27,25** |
| Chi phí cơ hội | $17.923 | $17.923 | **$1.360 – $2.145** |
| ROI lạc quan | −$4.523 | **+$48.317** | +$6.721 |
| ROI cơ sở | −$14.478 | −$259 | **+$4.500** *(trung điểm)* |
| ROI bi quan | −$17.349 | −$17.923 | **+$2.567** |
| **Kỳ vọng có trọng số** *(15/35/50)* | **−$14.150** | **−$1.805** | **+$4.100** |
| Rủi ro pháp lý | Cao (VN/US/EU) [file 01 §5.4] | Trung bình–thấp | **Gần 0** |
| Rủi ro trách nhiệm với người khác | Cao (khoá API người lạ) | Trung bình | **Không có** |
| **Điểm dừng rẻ?** | ❌ phải build xong mới biết | ✅ **5 email** | ✅ mỗi hạng mục độc lập |
| **Giá trị ngay cả khi thất bại** | Gần 0 | Kiến thức thị trường B2B | **Toàn bộ — bạn vẫn có công cụ** |

### 6.6 Kết luận về ROI

> **Kịch bản đối chứng thắng ở mọi mức của thang xác suất, và nó thắng không sát sao.**
>
> Nó tốn **1/5 đến 1/7 thời gian**, cho **kỳ vọng dương duy nhất** trong ba đường,
> hoà vốn ở **tháng thứ 8–10**, và — quan trọng nhất — **mọi hạng mục trong nó đều là điều kiện cần
> cho hai đường kia**. Không có một dòng code nào bị lãng phí nếu sau này bạn đổi ý.
>
> Đường B2B có đuôi phải đáng kể (+$48.317) và một bước thăm dò gần như miễn phí. **Làm bước thăm
> dò đó, nhưng không viết một dòng code nào cho nó cho tới khi có prop firm thứ nhất nói "có".**
>
> Đường B2C **âm ở cả 9 ô kịch bản**. Không có phiên bản nào của nó đáng làm trong 24 tháng tới.

---

## 7. Danh sách quyết định tài chính

| # | Quyết định | Khuyến nghị | Cơ sở |
|---|---|---|---|
| **F1** | Thương mại hoá B2C SaaS? | ⛔ **Không, trong 24 tháng** | Âm 9/9 ô kịch bản (6.3); SOM 21–148 < hoà vốn 109–186 |
| **F2** | Refactor tần suất LLM (R1–R5)? | ✅ **CÓ — ưu tiên số 1 tuyệt đối, 8–13 ngày-người** | Tiết kiệm $1.006/năm cho chính bạn (6.4-A); biến biên gộp từ −993% thành +78% (5.4, 5.6); hoàn vốn 9,4 tháng kể cả khi không bao giờ bán |
| **F3** | Phát hành token? | ⛔ **KHÔNG — dứt khoát** | Trượt 4/4 điều kiện cần (4.2); xung đột động lực cấu trúc (4.4); đỏ ở cả VN/US/EU (4.5) |
| **F4** | Thăm dò B2B prop firm? | ✅ **CÓ — làm tuần này. 5 email, 0 ngày-người** | Hoà vốn ở 1–3 khách thay vì 109–186 (3.3.5); đuôi phải +$48.317; điểm dừng rẻ (6.3) |
| **F5** | Mở nguồn? | ✅ **Có, sau F2 và mã hoá khoá** | Rủi ro pháp lý 9/10 — an toàn nhất (3.3.9); giải quyết luôn D-01 🔴; giá trị nghề nghiệp ~$2.077/năm |
| **F6** | Affiliate sàn? | ⛔ **Không** | Xung đột lợi ích cấu trúc — bạn kiếm nhiều nhất khi sản phẩm thất bại (3.3.7-b); vùng đỏ NQ05 |
| **F7** | Copy-trading / quản lý vốn người khác? | ⛔ **Loại bỏ hoàn toàn** | Rủi ro pháp lý cá nhân; và `useTestnet: false` hardcode nghĩa là track record hiện tại **không đáng tin** (C-01) |
| **F8** | Nhận khoá API của người thứ hai? | ⛔ **Không**, cho tới khi mã hoá xong + 18/18 lớp chặn có test | `TradingAccount.cs:29-34`; file 02 §4.2 — **khớp D2 file 01** |
| **F9** | Giá bán nếu có ngày bán? | **$19–29/tháng B2C · $500–1.500/tháng B2B** | Dưới $19 không đóng được toán ở SOM 21–148 (3.3.1) |
| **F10** | Freemium? | ⛔ **Không** | Mỗi user free đốt $48–197/tháng ở kiến trúc hiện tại (3.3.2) |
| **F11** | Dọn `AiSignalScanRecord` cũ (R5)? | ✅ **Có, trong 12 tháng tới** | SQL Express chạm trần 10 GB trong ~18 tháng với **1 người dùng** (5.5) |
| **F12** | Đưa `SignalGenerator` trở lại luồng chính (R4)? | ✅ **Có** | Giảm 50–70% lời gọi AI; **khớp D9 file 01 §8**; đúng nguyên tắc "Deterministic trước, AI sau" (`spec.md:24`) |

### 7.1 Thứ tự hành động 90 ngày, theo ROI trên mỗi giờ

| Tuần | Việc | Ngày-người | Giá trị |
|---|---|---:|---|
| **0** | `git commit` + push remote riêng tư + `git rm -r --cached .vs/` | **0,25** | Bảo vệ 59 ngày công việc + toàn bộ tài liệu thiết kế chưa commit (D-01 🔴). **Không làm việc này thì mọi việc khác có thể thành vô nghĩa.** |
| **0** | Gửi 5 email cho 5 crypto prop firm: *"tôi có execution-layer risk gating cho futures crypto, các anh có quan tâm không?"* | **0,1** | Quyền chọn trị giá $48.317 ở đuôi phải, chi phí gần 0 (F4) |
| **1–5** | **R1–R5 — refactor tần suất LLM** | **8–13** | **$1.006/năm tiền mặt cho chính bạn** + mở khoá mọi đường thương mại hoá tương lai (F2) |
| **6–7** | Mã hoá `ApiKey`/`ApiSecret` | 3–5 | Chặn rủi ro mất tiền thật; điều kiện cần của F5, F8 (S-01 🔴) |
| **8–9** | Sửa C-01 (venue) + C-03 (múi giờ) + C-04 (vốn đầu ngày) | 4–6 | **Làm cho cơ chế kỷ luật thực sự đúng** — nếu không, khoản B ở 6.4 bằng 0 |
| **10–11** | Test cho 9 lớp chặn còn thiếu | 2–3 | Bảo vệ đúng chỗ tiền chảy ra (file 02 §8.2) |
| **12** | Đánh giá lại: có prop firm nào trả lời không? | 0 | **Cổng quyết định** — có "có" thì mới xét đầu tư tiếp theo |

**Tổng: 17,35 – 27,35 ngày-người ≈ 8–13 tuần ở nhịp 17,5 giờ/tuần.**

**Và đây là điều đáng chú ý nhất về danh sách này**: nó **hoàn toàn trùng khớp** với kịch bản đối
chứng ở mục 6.4, **cộng thêm đúng 0,1 ngày-người** cho bước thăm dò B2B. Nghĩa là bạn có thể theo
đuổi kịch bản có kỳ vọng cao nhất **và** giữ nguyên quyền chọn đuôi phải, mà **không phải đánh đổi
gì cả**. Đó là một cấu trúc quyết định hiếm gặp và nên tận dụng.

---

## 8. Ba điều có thể làm bản thẩm định này SAI

Một thẩm định trung thực phải nói rõ điều kiện tự bác bỏ. Bổ sung cho §7.4 của file 01:

| # | Điều kiện | Xác suất *(chủ quan)* | Cách kiểm chứng — **rẻ nhất trước** |
|---|---|---|---|
| **1** | **Ước lượng token của tôi sai nhiều lần theo hướng có lợi.** Nếu output thực tế ngắn hơn nhiều, hoặc provider có caching/batch giảm mạnh giá | Thấp–trung bình | **Bật logging token usage vào `AiSignalScanRecord` (đã có sẵn trường lưu phản hồi thô) và đọc hoá đơn LLM tháng 8/2026.** Chi phí: 2 giờ. **Đây là việc rẻ nhất và quan trọng nhất — làm trước cả R1.** Nếu hoá đơn thật < $10/tháng thì mục 5 cần viết lại. |
| **2** | **Hệ số (c) tự nhận thức = 12% quá bi quan.** Nếu thực tế là 30–40%, SOM tăng 3× lên 12.000–20.000 người và bài toán B2C thay đổi | Thấp | **Hỏi 10 trader quen câu hỏi của file 01 §8.1 mục 3 — và THU TIỀN TRƯỚC của người nào nói có.** Chi phí: một buổi. Gật đầu không tính; chuyển khoản mới tính. |
| **3** | **Prop firm không tự xây được và trả tiền ngay.** Nếu 2/5 email trả lời tích cực trong 30 ngày | Thấp–trung bình | Chính bước F4. Chi phí: 0,1 ngày-người. |

Điều kiện **#1 là quan trọng nhất và rẻ nhất** — nó có thể lật đổ mục 5 và 6 của chính tài liệu này
với 2 giờ công. **Làm nó trước.** Toàn bộ mục 5 dựa trên ước lượng token, và ước lượng token là loại
số dễ sai nhất trong toàn bộ bản thẩm định.

---

## Phụ lục A — Danh mục giả định và độ nhạy

### A1. Bảng toàn bộ giả định của người phân tích

| Ký hiệu | Giả định | Giá trị dùng | Ảnh hưởng nếu sai |
|---|---|---|---|
| G1 | Watchlist tiêu biểu | 10 symbol (thương mại) / 5 (cá nhân) | **Chi phí LLM tỷ lệ thuận tuyến tính.** Đây là biến nhạy nhất của mục 5. |
| G2 | Lệnh mở đồng thời | 5 (thương mại, theo đề bài) / 2 (cá nhân) | Chi phí advisor tỷ lệ thuận |
| G3 | Token/lời gọi scan | 2.500 in / 150 out | ±50% → chi phí ±50% |
| G4 | Token/lời gọi advisor | 300 in / 120 out | như trên |
| G5 | Tỷ lệ JSON hỏng cần repair | 10% | Tác động nhỏ (<8% tổng) |
| G6 | Tỷ lệ quét sinh đề xuất → preflight | 3% | Tác động nhỏ (<3%) |
| G7 | Đơn giá LLM | 3 tầng A/B/C | **Đã trình bày dạng dải để kết luận không phụ thuộc một con số** |
| G8 | Hệ số (b) — thua vì kỷ luật | 70% | Ảnh hưởng TAM, không ảnh hưởng SOM nhiều |
| G9 | Hệ số (c) — tự nhận thức | 12% | **Ảnh hưởng SOM tuyến tính.** Neo vào tỷ số 12:1 quan sát được |
| G10 | Hệ số (d) — chịu trả tiền | 10% | Neo vào con số CMM tự công bố (90% ở gói free) |
| G11 | Thị phần khả dĩ của MMW | 0,5–2,5% | Giả định thuần |
| G12 | Lương senior .NET VN | 45 triệu VND/tháng | Xem A2 |
| G13 | Tỷ giá USD/VND | 26.000 | ±5% không đổi kết luận |
| G14 | Vốn giao dịch cá nhân | $5.000 (nhạy $2.000/$20.000) | Xem bảng 6.4-A |
| G15 | Churn B2C | 8%/tháng | Neo định tính vào vấn đề retention của category [plancana.com, 12/03/2026] |
| G16 | Phí thanh toán | 5% + $0,50 (Paddle) | Nhỏ |
| G17 | Giá B2B/firm | $500–2.000/tháng | Giả định thuần — không có benchmark công khai |
| G18 | Bội số mua lại micro-SaaS | 2–3,5× ARR | Giả định thuần |
| G19 | Phân bổ xác suất kịch bản | 15% / 35% / 50% | Chủ quan; đã trình bày cả 3 ô để người đọc tự gán trọng số |

### A2. Độ nhạy của giá trị thời gian tác giả

Không tìm được khảo sát lương senior .NET công khai đáng tin cho VN 2026. Neo duy nhất có nguồn:
lao động làm công thành thị **11,1 triệu VND/tháng** [Cục Thống kê Q2/2026, qua Thư Viện Pháp Luật,
04/07/2026]. Con số 45 triệu là **giả định** (≈4× mức đó).

| Lương giả định | $/giờ | Chi phí cơ hội 12 tháng | Chi phí cơ hội 24 tháng | Kết luận đổi không? |
|---|---:|---:|---:|---|
| 30 triệu VND | $6,56 | $5.970 | $11.940 | **Không** — B2C vẫn âm ở mọi ô |
| **45 triệu VND** *(dùng)* | $9,84 | $8.962 | $17.923 | — |
| 70 triệu VND | $15,30 | $13.940 | $27.880 | **Không** — B2C âm sâu hơn, đối chứng thắng đậm hơn |
| 100 triệu VND | $21,86 | $19.890 | $39.780 | **Không** — chỉ càng củng cố kết luận |

**Kết luận về độ nhạy**: giả định lương **không** ảnh hưởng tới hướng của kết luận. Lương càng cao,
đường thương mại hoá càng tệ và kịch bản đối chứng (ít giờ hơn) càng thắng.

---

## Phụ lục B — Nguồn đã dùng

**Tái sử dụng từ `01-market-landscape.md`** (mọi truy cập 29/07/2026 trừ khi ghi khác):

- **Chu kỳ**: CoinStats AI (01/07/2026) · TokenInsight Crypto Exchange Report Q2 2026 (20/07/2026) ·
  Phemex News (20/04/2026) · The Market Periodical (10/04/2026) · Yahoo Finance (29/06/2026)
- **Đối thủ & giá**: coinmarketman.com/pricing · tradermake.money/prices · tiltguard.app/features ·
  zerotilt.io · arizet.com · getmettle.app/blog (22/06/2026) · plancana.com/blog (12/03/2026) ·
  UseThisAI.fyi · TradeZella blog (14/07/2026) · ComparEdge (08/07 & 17/07/2026) · uwuu.ai (08/05/2026)
- **Việt Nam**: Cục Thống kê qua thuvienphapluat.vn (04/07/2026) · Dentons LuatViet (22/10/2025) ·
  sanvietnam.com (06/06/2026) · Thư Viện Pháp Luật (17/07/2025) · Báo Chính phủ (01/04/2026) ·
  VnEconomy (06/05/2026) · t.me/congdongtradevietnam · AppsFlyer State of Finance 2026 qua
  Advertising Vietnam
- **Pháp lý quốc tế**: FinanceFeeds dẫn Adam Tracy (20/05/2026) · SEC.gov press release 2026-30
  (17/03/2026) · Sullivan & Cromwell (19/03/2026) · thirdweb blog (17/06/2026)
- **Prop firm**: CryptoFundTrader (24/12/2025 — *tự công bố, xung đột lợi ích*)

**Tái sử dụng từ `02-product-reality.md`**: ước lượng 82,5–124,5 ngày-người (§6.3) · 9/18 lớp chặn
có test (§4.2) · danh mục nợ kỹ thuật S-01…S-06, C-01…C-08, P-01…P-07, D-01…D-05 (§5) ·
~2.700 dòng know-house thật vs ~75–80% boilerplate (§7)

**Mã nguồn đọc trực tiếp tại 2026-07-30**:
`src/MMW.Infrastructure/Ai/LlmOptions.cs:6-11` ·
`src/MMW.Application/Services/MarketScanService.cs:31-96, 344-463` ·
`src/MMW.Application/Services/TradeAdvisorService.cs:17, 27-32, 52-111, 230-251` ·
`src/MMW.Domain/Entities/TradingAccount.cs:29-34` ·
`specs/001-mmw-system-baseline/spec.md:23-24, 192, 208-209, 319-321, 327-331, 361`

**Không tìm được dữ liệu công khai cho**:
biểu giá LLM hiện hành tại 07/2026 (công cụ web lỗi) · tỷ lệ trader tự nhận thức mất kỷ luật ·
khảo sát lương senior .NET VN 2026 · giá của TiltGuard, Zero Tilt, A-Trader, Plancana premium ·
biểu phí affiliate chi tiết của Binance · benchmark giá B2B cho risk-guardrail infrastructure ·
quy mô thị trường "trading journal software" tách riêng

---

*Tài liệu này là thẩm định chiến lược sản phẩm và kinh doanh. Nó **KHÔNG** chứa lời khuyên đầu tư cá
nhân, không khuyến nghị mua/bán bất kỳ tài sản nào, và không thay thế tư vấn pháp lý hoặc thuế chuyên
nghiệp. Mọi nhận định pháp lý là tóm tắt nguồn công khai từ file 01 và cần luật sư xác nhận trước khi
hành động. Mọi con số không kèm `[nguồn, ngày]` đều là **ước lượng hoặc giả định của người phân tích**
và được đánh dấu như vậy tại chỗ.*
