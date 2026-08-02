# Phase 0 — Research & Technical Decisions

**Feature**: Deterministic Intraday Trading Engine
**Date**: 2026-08-02
**Input**: [spec.md](./spec.md) · [plan.md](./plan.md)

Không có mục `NEEDS CLARIFICATION` nào tồn đọng từ spec. Các mục dưới đây là quyết định kỹ thuật cần chốt trước khi thiết kế dữ liệu và hợp đồng.

---

## R-001 · Bảo đảm kiểm thử lịch sử tái lập đúng chạy thật

**Vấn đề**: FR-053 cấm nhánh mã riêng cho kiểm thử; FR-054 đòi chuỗi quyết định trùng khớp tuyệt đối. Đây là ràng buộc khó nhất của feature — hầu hết hệ thống giao dịch thất bại đúng ở đây.

**Decision**: Cô lập **hai** nguồn phi tất định duy nhất sau hai cổng, rồi thay cài đặt của chúng khi chạy kiểm thử. Không thay gì khác.

| Nguồn phi tất định | Cổng | Cài đặt chạy thật | Cài đặt kiểm thử |
|---|---|---|---|
| Thời gian hiện tại | `IClock` | `SystemClock` → `DateTime.UtcNow` | `BacktestClock` → con trỏ thời gian mô phỏng |
| Dữ liệu thị trường | `IMarketDataProvider` | `BinanceMarketDataProvider` | `ArchiveMarketDataProvider` đọc từ kho nến |

`DailyPlanService`, `TimeGuardService`, `EntryScorer`, `DisciplineGateRunner`, `ScoreBasedPositionSizer` **là cùng một lớp, cùng một thực thể** trong cả hai chế độ. Vòng lặp kiểm thử chỉ tua `BacktestClock` và gọi đúng các service đó.

**Rationale**: Nếu chỉ một dòng logic khác nhau giữa hai chế độ thì mọi con số kiểm thử mất giá trị, và điều tệ hơn là sự khác biệt sẽ không lộ ra cho tới khi mất tiền thật. Quy về hai cổng làm cho tính tương đương trở thành thuộc tính **cấu trúc** thay vì thuộc tính phải liên tục kiểm tra bằng tay.

**Enforcement**: Một test quét bằng reflection toàn bộ namespace `MMW.Application.Trading`, khẳng định không lớp nào tham chiếu trực tiếp `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`, hay `Random`. Vi phạm làm đỏ bộ test.

**Alternatives considered**:
- *Engine kiểm thử viết riêng, đọc lại logic từ spec* — cách phổ biến nhất và cũng là cách sai nhất; hai bản cài đặt sẽ trôi xa nhau ngay từ tuần đầu.
- *Ghi lại (record) mọi phản hồi API rồi phát lại* — giải quyết được dữ liệu nhưng không giải quyết được thời gian, và không chạy được trên dữ liệu trước thời điểm bắt đầu ghi.
- *Truyền `DateTime utcNow` làm tham số cho mọi phương thức* — làm được nhưng rò rỉ ra toàn bộ chữ ký hàm và dễ bị quên ở một nhánh nào đó; cổng `IClock` gọn hơn và kiểm tra tự động được.

---

## R-002 · Xác định nến đã đóng

**Vấn đề**: `/fapi/v1/klines` của Binance trả cây nến **đang chạy** làm phần tử cuối. Mã hiện tại tính chỉ báo trên cả cây nến đó (FR-001).

**Decision**: Thêm `IsClosed` vào bản ghi `Candle`, suy ra bằng `clock.UtcNow >= CloseTime`. Bổ sung một phương thức mở rộng `ClosedOnly()` trả về chuỗi đã cắt bỏ nến chưa đóng. `IIndicatorService` nhận chuỗi đã cắt; gọi với chuỗi chưa cắt là lỗi lập trình, không phải trường hợp hợp lệ.

Giá dùng để tính khoảng cách tới các mức giá lấy riêng từ `GetTickerAsync` (FR-002).

**Rationale**: Suy ra từ `CloseTime` thay vì tin vào một cờ do sàn trả về giữ cho `ArchiveMarketDataProvider` và `BinanceMarketDataProvider` hành xử giống hệt nhau — kho lịch sử chỉ cần lưu `CloseTime`, không cần lưu trạng thái đóng tại thời điểm ghi.

**Lưu ý về độ trễ**: Binance đóng nến theo đồng hồ sàn. Job đánh giá phải chạy trễ vài giây sau mốc để chắc chắn nến cuối đã chốt — xem R-011.

**Alternatives considered**:
- *Luôn bỏ phần tử cuối* — đơn giản nhưng sai khi gọi ngay sau mốc đóng: sẽ bỏ mất đúng cây nến vừa hoàn thành mà ta cần.
- *Dựa vào cờ `x` trong luồng WebSocket kline* — chính xác nhất nhưng đòi kết nối thời gian thực, ngoài phạm vi (xem mục Ngoài phạm vi của spec).

---

## R-003 · Nguồn dữ liệu futures công khai bổ sung

**Decision**: Dùng các endpoint công khai của Binance USDⓈ-M Futures, không cần khoá (FR-004).

> ✅ **Đã đối chiếu trực tiếp với API thật (T001, 2026-08-02)** — không đọc tài liệu mà gọi thẳng 7 endpoint trên `https://fapi.binance.com` và đo phản hồi. Bảng dưới đây là số liệu quan sát được, không phải số liệu chép lại. Ba dòng của bản nháp trước đã sai và được sửa; ba cạm bẫy chưa từng được ghi đã bổ sung ở mục "Bẫy cài đặt".

| Dữ liệu | Endpoint | Ràng buộc đã kiểm chứng | Trường phản hồi |
|---|---|---|---|
| Phí vốn hiện tại + giá đánh dấu | `GET /fapi/v1/premiumIndex` | weight 1 | `symbol, markPrice, indexPrice, estimatedSettlePrice, lastFundingRate, interestRate, nextFundingTime, time` |
| Lịch sử phí vốn | `GET /fapi/v1/fundingRate` | **Tối đa 500 bản ghi/lần** (`limit=1000` vẫn chỉ trả 500). **Lịch sử đầy đủ ≥ 2 năm** | `symbol, fundingTime, fundingRate, markPrice, rateType` |
| Lượng hợp đồng mở hiện tại | `GET /fapi/v1/openInterest` | weight 2 | `symbol, openInterest, time` |
| Lịch sử lượng hợp đồng mở | `GET /futures/data/openInterestHist` | **Chỉ 30 ngày**, `limit` tối đa **1000** (1500 bị từ chối) | `symbol, sumOpenInterest, sumOpenInterestValue, CMCCirculatingSupply, timestamp` |
| Tỷ lệ tài khoản mua/bán toàn thị trường | `GET /futures/data/globalLongShortAccountRatio` | Cùng ràng buộc 30 ngày | `symbol, longAccount, longShortRatio, shortAccount, timestamp` |
| Tỷ lệ khối lượng chủ động mua/bán | `GET /futures/data/takerlongshortRatio` | Cùng ràng buộc 30 ngày; **không có trường `symbol`** trong phản hồi; bucket ngày trễ 1 ngày so với hai endpoint trên | `buySellRatio, sellVol, buyVol, timestamp` |
| Độ sâu sổ lệnh | `GET /fapi/v1/depth` | weight đo được: **2 / 5 / 10 / 20** ứng với `limit` 5 / 100 / 500 / 1000. Dùng `limit=100` (weight 5) | `lastUpdateId, E, T, bids, asks` |

**Tập `period` hợp lệ của nhóm `/futures/data/*`** (kiểm chứng từng giá trị): `5m, 15m, 30m, 1h, 2h, 4h, 6h, 12h, 1d`.

### Bẫy cài đặt — ba điểm phải xử lý, không phải ghi chú cho vui

**B1 — `period` sai và `symbol` sai trả HTTP 200 kèm mảng rỗng, không phải lỗi.**
`period=3m`, `period=1w`, `symbol=NOTREAL` đều cho `200 []`. Chỉ chuỗi vô nghĩa hoàn toàn (`period=xyz`) mới cho `400 code -1130`. Kết hợp với FR-006 ("thiếu dữ liệu ⟹ tiêu chí nhận 0 điểm"), một lỗi đánh máy trong `period` sẽ **âm thầm giết một tiêu chí vĩnh viễn** mà không test nào đỏ và không log nào kêu.
→ **Bắt buộc**: `BinanceMarketDataProvider` kiểm tra `period` với danh sách trắng ở phía client và **ném ngoại lệ** khi sai. Đây là lỗi lập trình, không phải điều kiện dữ liệu — nó không được đi qua đường "trả `null`".

**B2 — `startTime` quá 30 ngày bị từ chối cứng bằng `400 code -1130 "parameter 'startTime' is invalid"`.**
Biên đo được: `-31 ngày` bị từ chối, `-29 ngày` chấp nhận. Không phải cắt bớt âm thầm mà là lỗi HTTP. Hợp đồng "trả `null` khi lỗi" của [contracts/market-data.md](./contracts/market-data.md) sẽ nuốt lỗi này → `IKlineArchiveService` phải biết trước rằng ba nguồn này **không nạp lịch sử được về bản chất**, và không được đưa chúng vào vòng lặp nạp bổ sung.

**B3 — `/fapi/v1/fundingRate?limit=1001` trả HTTP 200 với phong bì lỗi phi tiêu chuẩn:**
```json
{"status":"ERROR","type":"GENERAL","code":"99099990","errorData":"illegal params.","data":null}
```
Đây **không** phải dạng `{"code":-1130,"msg":"..."}` quen thuộc, và **không** phải mã HTTP lỗi. Một bộ bóc tách gọi thẳng `Deserialize<List<T>>` sẽ ném ngoại lệ hoặc trả rác. → `BinanceFuturesDataParser` (T032) phải phát hiện phản hồi dạng **đối tượng** ở nơi đang chờ **mảng** và coi đó là lỗi. T031 phải có test cho đúng phong bì này.

### Hệ quả cho kiểm thử lịch sử — nhẹ hơn bản nháp trước

Chỉ nhóm `/futures/data/*` bị giới hạn 30 ngày. **Lịch sử phí vốn thì không** — `/fapi/v1/fundingRate` trả về tới 2024-08-02, đủ 2 năm.

| Tiêu chí | Điểm | Kiểm thử lịch sử |
|---|---|---|
| `market.funding_crowding` | 4 | ✅ **Dựng lại được** từ `/fapi/v1/fundingRate` |
| `liquidity.open_interest` | 5 | ❌ Mất — `openInterestHist` chỉ 30 ngày |
| `liquidity.spread_depth` | 5 | ❌ Mất — sổ lệnh không có lịch sử ở bất kỳ độ sâu nào |
| `liquidity.zone_position` | 5 | ✅ Xấp xỉ từ nến theo R-010, không phụ thuộc nhóm 30 ngày |

→ **Quyết định (sửa)**: kiểm thử lịch sử mất **10/100 điểm**, không phải 14. Nó đánh giá phiên bản **75/85 điểm** của thuật toán. `BacktestRun.Limitations` ghi con số này.

⚠️ Kèm một cảnh báo về độ trung thực: `lastFundingRate` ở chế độ chạy thật là tỷ lệ **dự phóng** cho kỳ thanh toán sắp tới, còn `fundingRate` lịch sử là tỷ lệ **đã thanh toán**. Dùng tỷ lệ đã thanh toán làm giá trị hiệu lực cho 8 giờ trước đó là một xấp xỉ hợp lý và vẫn hơn hẳn việc chấm 0 điểm — nhưng nó là xấp xỉ, và phải nằm trong `Limitations`.

Bù cho 10 điểm còn mất: một job nhỏ lưu snapshot hàng giờ (T139) sẽ dựng dần kho dữ liệu để về sau kiểm thử được đầy đủ.

**Alternatives considered**:
- *Mua dữ liệu lịch sử từ nhà cung cấp bên thứ ba* — tốn tiền và thêm phụ thuộc, không tương xứng với quy mô 2 symbol.
- *Bỏ hẳn số điểm này khỏi thuật toán* — mất thông tin có giá trị thật ở chế độ chạy thật chỉ vì hạn chế của kiểm thử; sai hướng.
- *Suy ra phí vốn lịch sử từ chênh lệch giá đánh dấu và giá chỉ số* — không còn cần thiết sau khi xác minh `/fapi/v1/fundingRate` có đủ lịch sử.

---

## R-004 · Chỉ số tâm lý thị trường

**Decision**: `alternative.me/crypto/fear-and-greed-index` — API công khai, miễn phí, không cần khoá, trả một giá trị 0–100 mỗi ngày.

**Rationale**: Là nguồn duy nhất miễn phí, ổn định nhiều năm, và chỉ đóng góp một phần nhỏ vào phân loại trạng thái ngày nên rủi ro phụ thuộc thấp. Khi không truy cập được, tiêu chí liên quan nhận 0 điểm theo FR-006 và kế hoạch ngày vẫn sinh được.

**Đã kiểm chứng (T001)**: `GET https://api.alternative.me/fng/?limit=1` → HTTP 200.

```json
{"name":"Fear and Greed Index","data":[{"value":"27","value_classification":"Fear",
 "timestamp":"1785628800","time_until_update":"41744"}],"metadata":{"error":null}}
```

Hai chi tiết bộ bóc tách phải xử lý: `value` là **chuỗi** chứ không phải số, và `timestamp` là **giây** chứ không phải mili-giây. `AlternativeMeFearGreedProvider` (T034) trả `int?` nên phải tự chuyển kiểu; chuyển kiểu thất bại ⟹ trả `null`, không ném.

**Alternatives considered**:
- *Tự tính chỉ số tâm lý từ biến động và khối lượng* — trùng lặp với các tiêu chí đã có, không thêm thông tin độc lập.
- *Bỏ hẳn* — mất một góc nhìn không tương quan với dữ liệu giá, mà chi phí tích hợp gần bằng không.

---

## R-005 · Nguồn lịch sự kiện kinh tế

**Decision**: Bảng `ScheduledEvent` nạp tay mỗi năm một lần từ lịch công bố chính thức đã có sẵn trước:

- Cục Thống kê Lao động Hoa Kỳ công bố lịch phát hành cả năm cho chỉ số giá tiêu dùng, chỉ số giá sản xuất, và bảng lương phi nông nghiệp
- Cục Dự trữ Liên bang công bố lịch họp chính sách cả năm, gồm cả các cuộc có họp báo

Khối lượng: khoảng **40 dòng/năm**. Nạp qua `SeedData` cho năm hiện tại, và một màn hình quản trị đơn giản để bổ sung năm sau.

**Rationale**: Ba lý do khiến đây là lựa chọn đúng bất chấp việc phải làm tay:

1. **Mô hình ngôn ngữ bịa ngày giờ sự kiện** một cách trôi chảy và không thể phát hiện được. Cho AI làm nguồn lịch là đưa dữ liệu sai vào đúng lớp bảo vệ quan trọng nhất.
2. Dữ liệu này **được công bố trước cả năm và gần như không đổi**. Chi phí "tự động hoá" một thứ thay đổi mỗi năm một lần là chi phí âm.
3. Không phụ thuộc API bên ngoài → lớp chặn theo giờ không bao giờ chết vì nhà cung cấp đổi chính sách.

**Alternatives considered**:
- *API lịch kinh tế trả phí* — khoảng 50–100 USD/tháng cho một thứ tự làm mất 30 phút/năm. Không tương xứng.
- *Tầng miễn phí của các nhà cung cấp dữ liệu tài chính* — có giới hạn gọi, có thể đổi điều khoản, và vẫn cần đối chiếu; đổi một phụ thuộc chắc chắn lấy một phụ thuộc bấp bênh.
- *Trích xuất từ trang lịch kinh tế bằng bóc tách HTML* — dễ vỡ, và thường vi phạm điều khoản sử dụng.

**Cảnh báo quá hạn**: hệ thống phải cảnh báo khi sự kiện cuối cùng trong lịch đã ở quá khứ (FR-014) — nếu không, một cuốn lịch quên cập nhật sẽ im lặng biến lớp chặn thành vô dụng.

---

## R-006 · Sự kiện sinh bằng công thức

**Decision**: Ba nhóm sự kiện dưới đây **không** nạp tay mà tính bằng lịch (FR-009):

| Sự kiện | Quy tắc | Nguồn quy tắc |
|---|---|---|
| Thanh toán phí vốn | 00:00, 08:00, 16:00 UTC mỗi ngày | Chu kỳ 8 giờ chuẩn của Binance USDⓈ-M |
| Đáo hạn quyền chọn hàng tuần | Thứ Sáu 08:00 UTC | Quy ước Deribit |
| Đáo hạn quyền chọn hàng tháng | Thứ Sáu cuối cùng của tháng, 08:00 UTC | Quy ước Deribit |
| Khoảng trống cuối tuần | 21:00–23:00 UTC Chủ nhật | Quanh giờ mở lại của hợp đồng tương lai truyền thống |

**Rationale**: Đây là hàm thuần của ngày tháng, kiểm thử được bằng bảng đầu vào/đầu ra và không bao giờ quá hạn. Mốc thanh toán phí vốn đặc biệt đáng chặn vì hay xuất hiện các cây nến râu dài quét dừng lỗ.

**Lưu ý**: chu kỳ phí vốn của Binance có thể khác 8 giờ ở một số symbol trong điều kiện thị trường bất thường. Với hai symbol thanh khoản nhất thì giả định 8 giờ là an toàn; `DerivedEventGenerator` cần cho phép ghi đè chu kỳ theo symbol để không phải sửa mã nếu điều đó thay đổi.

---

## R-007 · Phát hiện cấu trúc thị trường

**Vấn đề**: FR-026 yêu cầu tiêu chí "phá vỡ cấu trúc và kiểm định lại" 0–10 điểm. Khái niệm này thường được mô tả bằng lời và mỗi người hiểu một kiểu — không chấp nhận được với yêu cầu tất định.

**Decision**: Định nghĩa bằng điểm xoay (pivot) kiểu fractal, tham số hoá:

1. **Điểm xoay đỉnh** tại chỉ số `i` khi `High[i]` lớn hơn `High` của `N` nến trước và `N` nến sau (`N` mặc định 2, đọc từ `EngineSetting`). Định nghĩa đối xứng cho điểm xoay đáy.
2. **Hệ quả về độ trễ**: một điểm xoay chỉ được xác nhận sau `N` nến. Điều này là **cố ý** — nó loại bỏ hoàn toàn khả năng nhìn trước tương lai (look-ahead bias) trong kiểm thử.
3. **Phá vỡ cấu trúc tăng** khi giá đóng cửa vượt điểm xoay đỉnh đã xác nhận gần nhất. Đối xứng cho chiều giảm.
4. **Kiểm định lại thành công** khi sau khi phá vỡ, giá quay về chạm vùng phá vỡ ±0.25 lần biên độ dao động rồi đóng cửa trở lại đúng chiều phá vỡ, trong vòng `M` nến (`M` mặc định 6).

Thang điểm: phá vỡ có kiểm định lại thành công 10 điểm; phá vỡ chưa kiểm định lại 5 điểm; không có phá vỡ trong `K` nến gần nhất 0 điểm.

**Rationale**: Định nghĩa fractal là cách phổ biến nhất và quan trọng hơn cả — nó **tính được ngược về quá khứ mà không cần biết tương lai**, điều kiện bắt buộc để kiểm thử lịch sử trung thực. Độ trễ `N` nến là cái giá phải trả và nên trả.

**Alternatives considered**:
- *Đỉnh/đáy theo cửa sổ trượt tối đa* — không xác nhận được, đỉnh "gần nhất" thay đổi mỗi nến.
- *Đường xu hướng vẽ tự động* — nhiều tham số ẩn, khó tái lập, khó giải thích khi sai.
- *Vùng cung cầu / khối lệnh* — định nghĩa còn mơ hồ hơn cả phá vỡ cấu trúc.

---

## R-008 · Neo VWAP

**Decision**: VWAP neo theo **ngày UTC**, khởi động lại tại 00:00 UTC, tính trên nến 15 phút.

**Rationale**: Trùng mốc ngày giao dịch của hệ thống (FR-024), trùng mốc nến ngày đóng, và trùng một mốc thanh toán phí vốn. Một mốc neo duy nhất dùng chung khắp nơi loại bỏ cả một lớp lỗi lệch múi giờ.

**Alternatives considered**:
- *VWAP trượt N phiên* — không có ý nghĩa "vùng giá trị của phiên hôm nay", vốn là thứ tiêu chí "vị trí vào lệnh" cần.
- *Neo theo phiên giao dịch (Á/Âu/Mỹ)* — hợp lý cho người đánh theo phiên nhưng cần ba đường VWAP song song, thêm phức tạp mà tiêu chí chấm điểm chưa dùng tới.

---

## R-009 · Phương pháp tính phân vị

**Vấn đề**: FR-017 dùng phân vị biên độ dao động so với 90 phiên. Có nhiều định nghĩa phân vị cho ra kết quả khác nhau ở cùng dữ liệu — phải chốt một, nếu không kiểm thử và chạy thật sẽ lệch nhau ở đúng vùng biên.

**Decision**: **Phân vị theo thứ hạng gần nhất** (nearest-rank), không nội suy:

```
rank = ceil(p / 100 × n)
value = sorted[rank - 1]
```

Ngược lại, phân vị của một giá trị `v` = `(số phần tử ≤ v) / n × 100`.

Yêu cầu tối thiểu 60 phiên có dữ liệu; dưới ngưỡng đó trả về "không xác định" và tiêu chí liên quan nhận 0 điểm theo FR-006.

**Rationale**: Nearest-rank là phương pháp đơn giản nhất, không phụ thuộc thư viện, và cho kết quả giống hệt nhau trên mọi nền tảng — không có sai số dấu phẩy động từ bước nội suy. Ranh giới regime (25/75/90) đều là bậc thang chứ không phải điểm nhạy cảm, nên mất mát từ việc không nội suy là không đáng kể.

---

## R-010 · Xấp xỉ vùng thanh khoản

**Vấn đề**: FR-029 cần "vị trí các vùng thanh khoản so với mức chốt lời và mức cắt lỗ". Dữ liệu bản đồ thanh lý thật không có công khai.

**Decision**: Xấp xỉ bằng hợp của ba nguồn, tất cả tính được từ dữ liệu giá:

1. Các điểm xoay đỉnh/đáy đã xác nhận trong 100 nến gần nhất (nơi dừng lỗ thường tụ)
2. Đỉnh/đáy phiên trước và giá mở tuần (đã có trong kế hoạch ngày)
3. Các mức giá tròn theo bước phù hợp với từng symbol

Chấm điểm: có vùng thanh khoản nằm giữa giá vào và mức chốt lời → tối đa 5 điểm (đích có lực hút); có vùng nằm ngay ngoài mức cắt lỗ trong phạm vi 0.3 lần biên độ dao động → **trừ về 0** (dừng lỗ nằm trong tầm quét).

**Rationale**: Không phải dữ liệu thật và **phải được ghi vết đúng như vậy** trong phiếu chấm điểm để về sau không ai nhầm tưởng. Dù là xấp xỉ, nó vẫn nắm được phần cơ chế quan trọng nhất: dừng lỗ đặt ngay sau một đỉnh/đáy rõ ràng là dừng lỗ dễ bị quét.

**Alternatives considered**:
- *Bỏ hẳn tiêu chí, chia 5 điểm cho các tiêu chí khác* — mất một trong số ít tiêu chí nói về **vị trí dừng lỗ** thay vì về hướng đi; đây là góc nhìn không trùng với tiêu chí nào khác.
- *Mua dữ liệu bản đồ thanh lý* — ngoài phạm vi, đã ghi trong spec.

---

## R-011 · Lịch chạy job và chống chồng lấn

**Decision**:

| Job | Lịch | Độ trễ chủ ý | Gọi AI |
|---|---|---|---|
| `daily-plan` | `30 23 * * *` | — | 1 |
| `signal-eval` | `1,16,31,46 * * * *` | **+1 phút** sau mốc đóng nến | 0 |
| `position-manage` | `*/1 * * * *` | — | 0 |
| `news-scan` | `*/15 * * * *` | — | 0–2 |

Độ trễ 1 phút của `signal-eval` bảo đảm nến 15 phút đã thực sự chốt phía sàn trước khi đọc, kể cả khi đồng hồ máy chủ lệch nhẹ.

Chống chồng lấn: mọi job dùng `[DisableConcurrentExecution]` của Hangfire, cộng một khoá theo `(symbol, thời điểm nến đóng)` ở tầng nghiệp vụ để hai lần chạy khác thời điểm cũng không sinh trùng lệnh cho cùng một cây nến (FR-051).

**Rationale**: Chỉ dựa vào `DisableConcurrentExecution` là chưa đủ — nó chặn hai lần chạy đồng thời nhưng không chặn hai lần chạy nối tiếp cùng đọc một cây nến. Khoá theo thời điểm nến đóng mới là thứ bảo đảm bất biến thật.

**Cảnh báo lệch đồng hồ**: `position-manage` so sánh thời gian máy chủ với thời gian sàn trả về; lệch quá 30 giây thì cảnh báo, vì toàn bộ lớp chặn theo giờ phụ thuộc đồng hồ đúng (đã ghi trong mục Edge Cases của spec).

---

## R-012 · Mô hình phí và trượt giá trong kiểm thử

**Decision**: FR-056 cấm bỏ qua hai yếu tố này. Mô hình dùng:

| Thành phần | Giá trị mặc định | Ghi chú |
|---|---|---|
| Phí lệnh chủ động khớp | 0.05% mỗi chiều | Mức công khai của Binance USDⓈ-M, không tính giảm giá |
| Trượt giá khi vào lệnh | 1 điểm cơ bản | Cấu hình trong `EngineSetting` |
| Trượt giá khi dừng lỗ khớp | 3 điểm cơ bản | Cao hơn vì dừng lỗ khớp vào lúc thanh khoản mỏng nhất |
| Phí vốn khi giữ qua mốc | Bỏ qua ở v1 | Giữ lệnh 1–4 giờ nên hiếm khi qua mốc; ghi rõ là hạn chế |

Toàn bộ đọc từ cấu hình, không hardcode.

**Rationale**: Với quy mô danh nghĩa nhỏ, **phí là thành phần chi phí lớn nhất chứ không phải trượt giá** — 0.1% khứ hồi trên 220 lệnh/tháng là con số quyết định việc chiến lược sống hay chết. Một kết quả kiểm thử bỏ qua phí không chỉ lạc quan mà là sai về mặt định tính: nó có thể biến một chiến lược lỗ thành một chiến lược lãi.

Giả định trượt giá bất đối xứng (dừng lỗ trượt nhiều hơn vào lệnh) phản ánh đúng thực tế và nghiêng về phía thận trọng.

---

## Tổng hợp rủi ro kỹ thuật

| Rủi ro | Mức | Giảm thiểu |
|---|---|---|
| Nhóm `/futures/data/*` chỉ có 30 ngày → kiểm thử thiếu **10/100 điểm** (đã kiểm chứng T001; phí vốn dựng lại được nên nhẹ hơn con số 14 ở bản nháp) | Trung bình | Ghi rõ trong báo cáo kiểm thử; chạy job snapshot hàng giờ để dựng dần kho dữ liệu |
| `period` sai trả mảng rỗng thay vì lỗi → một tiêu chí chết âm thầm mãi mãi | **Cao** | Danh sách trắng `period` phía client, sai thì **ném ngoại lệ** (B1) — không đi qua đường trả `null` |
| Cấu trúc thị trường định nghĩa sai → toàn bộ 10 điểm nhóm A vô nghĩa | Trung bình | Test bằng bộ nến dựng tay có kết quả biết trước; vẽ lại điểm xoay lên biểu đồ để mắt người kiểm tra |
| Kiểm thử lệch chạy thật dù đã có `IClock` | Trung bình | Test tương đương là tiêu chí chấp nhận bắt buộc (SC-003), không phải mục tuỳ chọn |
| Cuốn lịch sự kiện quên cập nhật sang năm | Trung bình | Cảnh báo tự động khi sự kiện cuối đã ở quá khứ (FR-014) |
| Endpoint Binance đổi đường dẫn hoặc ràng buộc | Thấp | Task đối chiếu tài liệu trước khi cài đặt; lỗi một nguồn chỉ làm tiêu chí đó về 0 điểm, không sập hệ thống |
| Kho nến phình to ảnh hưởng hiệu năng | Thấp | 2 symbol × 2 năm × 15 phút ≈ 140k dòng — không đáng kể với SQL Server; đánh chỉ mục theo `(Symbol, Interval, OpenTime)` |
