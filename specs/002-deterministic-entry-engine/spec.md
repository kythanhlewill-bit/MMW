# Feature Specification: Deterministic Intraday Trading Engine

**Feature Directory**: `specs/002-deterministic-entry-engine`

**Created**: 2026-08-02

**Status**: Draft

**Input**: Thay thế đường sinh tín hiệu do AI quyết định bằng thuật toán tất định 4 tầng (Kế hoạch ngày → Chặn theo khung giờ → Chấm điểm vào lệnh → Thực thi), đưa AI về đúng vai trò lớp bối cảnh chỉ-được-veto.

---

## Bối cảnh & Vấn đề

Hệ thống baseline (`001-mmw-system-baseline`) đã dựng đủ hạ tầng: nhật ký lệnh, bộ quy tắc kỷ luật, phát hiện hành vi, quét thị trường, nhiều lớp chặn trước sàn, đồng bộ kết quả. Nhưng **quyết định vào lệnh hiện đang nằm hoàn toàn trong tay mô hình ngôn ngữ**:

| Hiện trạng | Hệ quả |
|---|---|
| AI quyết định hướng lệnh, tự đặt giá vào / cắt lỗ / chốt lời; hệ thống chỉ kiểm tra hình thức phía sau | Vi phạm tinh thần Nguyên tắc II của hiến chương — con số ra quyết định không tất định |
| Bộ suy ra thiên hướng tất định chỉ có **2 yếu tố** (tương quan giá/đường trung bình, động lượng), dải điểm −2..+2, và chỉ được dùng làm dữ liệu nhét vào prompt | Không có lớp tất định nào đủ mạnh để đứng độc lập |
| Bộ sinh đề xuất tất định (cắt lỗ/chốt lời theo biên độ dao động) tồn tại nhưng **không được gọi** — là mã chết | Nhánh dự phòng tất định trên giấy tờ, không có thật |
| Lớp chặn theo lịch tin vĩ mô đã có logic nhưng **không hoạt động** vì nhà cung cấp lịch chưa bật; nguồn RSS hiện tại chỉ có thông cáo, không có sự kiện kèm giờ | Trader không được bảo vệ khỏi các khung giờ tin mạnh |
| Không tồn tại khái niệm **phiên giao dịch**, **khung giờ**, hay **kế hoạch ngày** ở bất kỳ đâu | Mọi giờ trong ngày được đối xử như nhau, dù thanh khoản chênh nhau nhiều lần |
| Chỉ báo được tính trên cây nến **đang chạy chưa đóng** | Chỉ báo dao động liên tục trong suốt chu kỳ nến; kết quả kiểm thử lịch sử sẽ không bao giờ tái lập được kết quả chạy thật |
| Ba bộ phát hiện hành vi chỉ **cảnh báo**, không chặn | Kỷ luật là lời khuyên, không phải rào chắn |

**Vấn đề cốt lõi**: sản phẩm tồn tại để cưỡng chế kỷ luật, nhưng thành phần ra quyết định lại là thứ duy nhất trong hệ thống không tất định, không kiểm thử lịch sử được, và có thể hỏng bất cứ lúc nào.

**Mục tiêu feature này**: đảo ngược quan hệ. Thuật toán tất định ra quyết định; AI chỉ được **veto hoặc giảm** kích thước lệnh, không bao giờ được tạo lệnh, chọn hướng, hay tăng kích thước.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Không vào lệnh quanh khung giờ tin mạnh (Priority: P1)

Trader không muốn hệ thống đặt lệnh 30 phút trước khi công bố CPI, cũng không muốn để một vị thế trần trong lúc chủ tịch ngân hàng trung ương họp báo. Hệ thống phải tự biết những khung giờ đó từ một cuốn lịch nội bộ và tự đứng ngoài.

**Why this priority**: Có giá trị **ngay cả khi dừng lại ở đây** — một cái chặn tự động quanh giờ CPI đã dùng được kể cả khi trader vẫn vào lệnh tay. Không phụ thuộc bất kỳ tầng nào khác. Đây là lát cắt MVP thật sự.

**Independent Test**: Nạp lịch sự kiện, đặt đồng hồ hệ thống vào từng mốc trước/trong/sau cửa sổ chặn, xác nhận hệ thống trả đúng trạng thái "được phép" hoặc "bị chặn kèm lý do". Không cần tầng chấm điểm, không cần kế hoạch ngày.

**Acceptance Scenarios**:

1. **Given** lịch có sự kiện tác động cao lúc 13:30 UTC, **When** thời điểm hiện tại là 12:45 UTC, **Then** hệ thống chặn mọi lệnh mới kèm lý do nêu rõ tên sự kiện và thời điểm theo giờ Việt Nam.
2. **Given** cùng sự kiện đó, **When** thời điểm hiện tại là 12:25 UTC (ngoài cửa sổ T−60), **Then** hệ thống không chặn.
3. **Given** cùng sự kiện đó, **When** thời điểm hiện tại là 14:05 UTC (sau T+30), **Then** hệ thống không chặn.
4. **Given** một vị thế đang mở và một cửa sổ chặn sắp bắt đầu trong 5 phút, **When** hệ thống rà soát vị thế, **Then** vị thế được xử lý giảm rủi ro (không để nguyên trạng) và trader nhận thông báo.
5. **Given** thời điểm hiện tại rơi vào mốc thanh toán phí vốn (funding settlement), **When** có đề xuất vào lệnh, **Then** hệ thống chặn trong cửa sổ ±5 phút quanh mốc.
6. **Given** cuốn lịch nội bộ rỗng hoặc chưa nạp, **When** hệ thống rà soát, **Then** các cửa sổ chặn tính được bằng công thức (thanh toán phí vốn, đáo hạn quyền chọn, khoảng trống cuối tuần) **vẫn hoạt động**, và hệ thống cảnh báo lịch sự kiện kinh tế đang thiếu.

---

### User Story 2 — Mỗi ngày có một kế hoạch, và cả ngày đi theo nó (Priority: P1)

Đầu mỗi ngày giao dịch, hệ thống tự nhận định trạng thái thị trường và ra một bản kế hoạch: hôm nay được đánh chiều nào, tối đa bao nhiêu lệnh, hệ số rủi ro bao nhiêu, các mức giá quan trọng ở đâu. Mọi lệnh trong ngày phải nằm trong khuôn khổ đó.

**Why this priority**: Đây là thứ chặn được sai lầm đắt nhất của giao dịch trong ngày — đánh ngược xu hướng ngày và tăng tần suất vào những ngày biến động cực đoan. Nó cũng là ràng buộc đầu vào của tầng chấm điểm, nên phải có trước.

**Independent Test**: Cho hệ thống dữ liệu giá lịch sử của nhiều ngày có tính chất khác nhau (tăng mạnh, giảm mạnh, đi ngang, biến động cực đoan, ngày có tin lớn), xác nhận bản kế hoạch sinh ra khớp bảng quyết định đã định nghĩa. Có thể kiểm thử hoàn toàn không cần tầng chấm điểm.

**Acceptance Scenarios**:

1. **Given** cấu trúc giá ngày cho thấy đỉnh sau cao hơn đỉnh trước và đáy sau cao hơn đáy trước, biên độ dao động ở mức bình thường, **When** sinh kế hoạch ngày, **Then** kế hoạch cho phép **chỉ chiều mua**, hệ số rủi ro 1.0, tối đa 5 lệnh.
2. **Given** biên độ dao động nằm trên phân vị 90 của 90 ngày gần nhất, **When** sinh kế hoạch ngày, **Then** hệ số rủi ro là 0.3 và tối đa 2 lệnh, bất kể cấu trúc giá.
3. **Given** hôm nay có sự kiện tác động cao trong lịch, **When** sinh kế hoạch ngày, **Then** hệ số rủi ro tối đa 0.4, tối đa 2 lệnh, và các cửa sổ chặn của ngày được đưa sẵn vào kế hoạch.
4. **Given** kế hoạch ngày cho phép chỉ chiều mua, **When** xuất hiện một cơ hội chiều bán đạt điểm rất cao, **Then** hệ thống **không vào lệnh** và ghi lý do "ngược kế hoạch ngày".
5. **Given** một nguồn dữ liệu phụ trợ (chỉ số tâm lý, tỷ lệ mua/bán) không truy cập được, **When** sinh kế hoạch ngày, **Then** kế hoạch vẫn được sinh từ các đầu vào còn lại và ghi rõ thành phần nào bị thiếu.
6. **Given** thời điểm hiện tại là 00:30 UTC, **When** truy vấn kế hoạch, **Then** trả về kế hoạch của ngày UTC hiện tại, không phải ngày hôm trước.

---

### User Story 3 — Vào lệnh theo điểm số tất định, không theo lời AI (Priority: P1)

Với mỗi cây nến 15 phút vừa đóng, hệ thống tự chấm cơ hội theo bốn nhóm tiêu chí và ra một điểm số 0–100. Điểm quyết định vào hay không vào, và vào với kích thước bao nhiêu. Không có mô hình ngôn ngữ nào tham gia vào con số này.

**Why this priority**: Đây là lõi của feature. Đồng thời là thứ khiến kiểm thử lịch sử trở nên khả thi — điều kiện tiên quyết để biết chiến lược có lợi thế hay không.

**Independent Test**: Cho một tập trạng thái thị trường dựng sẵn, xác nhận điểm số tính ra đúng theo bảng trọng số, và cùng một đầu vào luôn cho cùng một đầu ra. Chạy được offline, không cần mạng, không cần AI.

**Acceptance Scenarios**:

1. **Given** một trạng thái thị trường bất kỳ, **When** chấm điểm hai lần liên tiếp, **Then** hai kết quả **giống hệt nhau đến từng chữ số**.
2. **Given** dịch vụ AI hoàn toàn không cấu hình, **When** chạy vòng đánh giá vào lệnh, **Then** vòng chạy trọn vẹn và ra quyết định bình thường.
3. **Given** điểm tổng là 54, **When** áp ngưỡng, **Then** không vào lệnh.
4. **Given** điểm tổng là 72 và hệ số rủi ro ngày là 1.0, **When** áp ngưỡng, **Then** vào lệnh với 1.0 đơn vị rủi ro.
5. **Given** điểm tổng là 88 và hệ số rủi ro ngày là 0.3, **When** áp ngưỡng, **Then** kích thước cuối cùng là 1.5 × 0.3 = 0.45 đơn vị rủi ro.
6. **Given** giá đã chạy quá 1.5 lần biên độ dao động khỏi vùng xác nhận, **When** chấm nhóm kỹ thuật, **Then** tiêu chí "vị trí vào lệnh" được 0 điểm.
7. **Given** thiên hướng khung lớn ngược với kế hoạch ngày, **When** chấm điểm, **Then** trả về veto cứng và **không** tính tiếp điểm số.
8. **Given** trong một ngày không có cơ hội nào đạt 55 điểm, **When** kết thúc ngày, **Then** số lệnh trong ngày là 0 và hệ thống **không** hạ ngưỡng để tạo lệnh.

---

### User Story 4 — Kỷ luật là rào chắn, không phải lời khuyên (Priority: P2)

Khi trader đã thua liên tiếp, đã chạm giới hạn lỗ ngày, hay vừa cắt lỗ xong đã muốn vào lại — hệ thống phải **chặn**, không phải hiện một dòng cảnh báo rồi vẫn cho qua.

**Why this priority**: Nguyên tắc I của hiến chương nói sản phẩm hứa hẹn chặn lệnh sai kỷ luật. Ba bộ phát hiện đã tồn tại; việc còn lại là nâng chúng từ cảnh báo lên rào chắn. Xếp P2 vì phụ thuộc tầng thực thi ở US3 đã có chỗ để cắm vào.

**Independent Test**: Dựng lịch sử lệnh giả với các mẫu hành vi khác nhau, xác nhận từng gate chặn đúng và không chặn nhầm ở ngay dưới ngưỡng.

**Acceptance Scenarios**:

1. **Given** hai lệnh thua liên tiếp gần nhất, **When** có cơ hội mới, **Then** kích thước lệnh bị nhân 0.5.
2. **Given** ba lệnh thua liên tiếp, **When** có cơ hội mới, **Then** hệ thống dừng giao dịch đến hết ngày UTC hiện tại kèm thông báo.
3. **Given** tổng lỗ trong ngày đã chạm ngưỡng cấu hình, **When** có cơ hội mới, **Then** hệ thống dừng giao dịch đến hết ngày.
4. **Given** lệnh thua gần nhất đóng cách đây 10 phút, **When** có cơ hội mới, **Then** hệ thống chặn với lý do nghi ngờ vào lệnh trả thù.
5. **Given** lệnh thua gần nhất đóng cách đây 20 phút (ngoài cửa sổ 15 phút), **When** có cơ hội mới, **Then** hệ thống **không** chặn vì lý do này.
6. **Given** đã đủ số lệnh tối đa theo kế hoạch ngày, **When** có cơ hội mới, **Then** hệ thống chặn.
7. **Given** tài khoản đã có từ 50 lệnh đã đóng trở lên và giờ hiện tại nằm trong 2 khung giờ thua nhiều nhất của trader, **When** chấm điểm, **Then** trừ 10 điểm.
8. **Given** tài khoản có dưới 50 lệnh đã đóng, **When** chấm điểm chất lượng khung giờ, **Then** dùng bảng phiên chuẩn, **không** dùng thống kê cá nhân.

---

### User Story 5 — Kiểm thử lịch sử tái lập được kết quả chạy thật (Priority: P2)

Trader cần biết chiến lược có lợi thế hay không **trước khi** đụng tiền thật. Kết quả chạy trên dữ liệu lịch sử phải khớp với cách hệ thống hành xử khi chạy thật — nếu không, con số kiểm thử là vô nghĩa.

**Why this priority**: Không có nó thì không có cách nào biết tầng 1–3 có giá trị hay không. Nhưng nó chỉ chạy được sau khi tầng 1–3 tồn tại.

**Independent Test**: Chạy kiểm thử trên một khoảng thời gian, ghi lại các quyết định; cho hệ thống chạy chế độ mô phỏng trên đúng khoảng đó với đúng dữ liệu; hai chuỗi quyết định phải trùng khớp.

**Acceptance Scenarios**:

1. **Given** một khoảng thời gian lịch sử, **When** chạy kiểm thử hai lần, **Then** kết quả giống hệt nhau.
2. **Given** cùng một tập dữ liệu, **When** so sánh chuỗi quyết định của kiểm thử lịch sử với chuỗi quyết định của chế độ mô phỏng, **Then** hai chuỗi trùng khớp hoàn toàn.
3. **Given** dữ liệu giá bao gồm cả cây nến đang hình thành, **When** tính chỉ báo, **Then** cây nến chưa đóng **không** được đưa vào tính toán chỉ báo.
4. **Given** một chỉ báo được tính tại thời điểm giữa chu kỳ nến và tính lại tại thời điểm nến vừa đóng, **When** so sánh, **Then** hai giá trị **giống nhau** (không dao động trong chu kỳ).
5. **Given** kết quả kiểm thử, **When** trader xem báo cáo, **Then** báo cáo nêu tỷ lệ thắng, kỳ vọng theo đơn vị rủi ro, mức sụt giảm vốn lớn nhất, số lệnh, và phân bố kết quả theo khung giờ và theo trạng thái ngày.
6. **Given** kiểm thử chạy hoàn toàn offline, **When** ngắt mạng, **Then** kiểm thử vẫn chạy được đến hết.

---

### User Story 6 — AI chỉ được nói "không", không bao giờ được nói "vào" (Priority: P2)

AI đọc lịch sự kiện và tin tức, tóm tắt bối cảnh, và có quyền cảnh báo hoặc yêu cầu giảm kích thước lệnh. Nó không được phép chọn hướng, đặt giá, hay làm lệnh to lên.

**Why this priority**: Giữ lại giá trị thật của mô hình ngôn ngữ (đọc hiểu văn bản phi cấu trúc) mà không giao cho nó quyền gây thiệt hại. Xếp P2 vì tầng 1–3 phải chạy được độc lập trước.

**Independent Test**: Cho AI trả về mọi kiểu phản hồi dị thường — đòi vào lệnh, đòi tăng kích thước, bịa ra sự kiện không có trong dữ liệu đầu vào, trả sai định dạng, trả về rỗng — xác nhận không phản hồi nào làm thay đổi quyết định theo hướng rủi ro hơn.

**Acceptance Scenarios**:

1. **Given** AI trả về đề xuất vào lệnh kèm giá cụ thể, **When** hệ thống xử lý phản hồi, **Then** phần đề xuất bị bỏ qua hoàn toàn và ghi vết là phản hồi ngoài phạm vi.
2. **Given** AI đề nghị hệ số rủi ro 1.5, **When** hệ thống áp dụng, **Then** hệ số không tăng — chỉ giá trị **nhỏ hơn hoặc bằng** giá trị tất định mới được áp.
3. **Given** AI trả về một sự kiện kèm giờ **không** có trong dữ liệu lịch được cung cấp, **When** hệ thống xử lý, **Then** sự kiện đó bị loại bỏ và ghi vết.
4. **Given** AI trả về văn bản không phải định dạng dữ liệu hợp lệ, **When** hệ thống xử lý, **Then** bối cảnh được coi là trung tính và vòng quyết định vẫn chạy bình thường.
5. **Given** một bối cảnh có thời hạn 60 phút được ghi nhận lúc 10:00, **When** thời điểm hiện tại là 11:30, **Then** bối cảnh đó được coi là trung tính (đã hết hạn).
6. **Given** dịch vụ AI hết hạn ngạch cả ngày, **When** hệ thống chạy trọn một ngày giao dịch, **Then** không có lệnh nào bị lỗi vì lý do này.
7. **Given** một ngày giao dịch bình thường, **When** đếm số lần gọi dịch vụ AI, **Then** tổng số dưới 30 lần/ngày.

---

### User Story 7 — So sánh khách quan giữa thuật toán và AI (Priority: P3)

Đường sinh tín hiệu bằng AI cũ vẫn chạy song song nhưng chỉ ghi nhật ký, không tạo lệnh. Sau một thời gian, trader có dữ liệu thật để so sánh hai cách tiếp cận thay vì tranh luận bằng cảm tính.

**Why this priority**: Không chặn việc gì cả, nhưng là cách duy nhất để trả lời câu hỏi "gỡ AI ra có đúng không" bằng bằng chứng.

**Independent Test**: Chạy một ngày, xác nhận cả hai đường đều để lại bản ghi, và **chỉ** đường tất định tạo ra lệnh.

**Acceptance Scenarios**:

1. **Given** cả hai đường cùng chạy, **When** kết thúc một chu kỳ đánh giá, **Then** bản ghi của đường AI được lưu và **không** có lệnh nào được tạo từ đường đó.
2. **Given** cả hai đường cùng chạy, **When** đường AI đề xuất vào lệnh còn đường tất định từ chối, **Then** hệ thống ghi nhận điểm bất đồng để đối chiếu về sau.
3. **Given** một khoảng thời gian đã tích luỹ dữ liệu, **When** trader mở báo cáo so sánh, **Then** báo cáo nêu số đề xuất mỗi bên, số điểm bất đồng, và kết quả giả định của các đề xuất bên AI nếu chúng đã được thực thi.
4. **Given** chế độ so sánh bị tắt qua cấu hình, **When** hệ thống chạy, **Then** không có lần gọi AI nào cho mục đích sinh tín hiệu.

---

### Edge Cases

- **Nến thiếu hoặc gián đoạn dữ liệu**: sàn trả về ít nến hơn yêu cầu, hoặc thiếu nến ở giữa. Hệ thống phải từ chối chấm điểm thay vì chấm trên dữ liệu khuyết.
- **Mất kết nối sàn giữa chu kỳ**: một symbol lỗi không được chặn symbol còn lại; chu kỳ lỗi hoàn toàn không được để lại kế hoạch ngày rỗng ghi đè lên kế hoạch cũ còn hợp lệ.
- **Job chạy chồng lấn**: chu kỳ đánh giá trước chưa xong đã tới chu kỳ sau. Không được sinh hai lệnh cho cùng một cơ hội.
- **Đổi ngày UTC giữa lúc một vị thế đang mở**: bộ đếm số lệnh trong ngày và trạng thái dừng-ngày phải reset theo mốc 00:00 UTC, nhưng vị thế đang mở không bị ảnh hưởng.
- **Kế hoạch ngày chưa được sinh** (job lỗi, hệ thống vừa khởi động giữa ngày): tầng chấm điểm không được chạy với kế hoạch mặc định phóng khoáng — phải chặn cho tới khi có kế hoạch hợp lệ.
- **Sự kiện trong lịch không có giờ cụ thể**: không được coi là không tồn tại; phải xử lý theo hướng an toàn.
- **Hai cửa sổ chặn chồng lấn nhau**: hợp nhất, lấy biên rộng nhất.
- **Đồng hồ máy chủ lệch**: mọi mốc thời gian nghiệp vụ theo UTC; lệch giờ máy chủ làm sai toàn bộ tầng chặn theo giờ, cần phát hiện và cảnh báo.
- **Tài khoản vừa đủ 50 lệnh đóng**: chuyển từ bảng phiên chuẩn sang thống kê cá nhân không được gây nhảy bậc đột ngột gây khó hiểu; phải ghi vết thời điểm chuyển.
- **Ngày cuối tuần**: thanh khoản mỏng và khoảng trống giá đầu tuần; kế hoạch ngày phải phản ánh được điều này.
- **AI trả về cửa sổ chặn kéo dài bất thường** (ví dụ 20 tiếng): phải có trần cho độ dài cửa sổ chặn do AI đề xuất.

---

## Requirements *(mandatory)*

### Functional Requirements

#### Nền dữ liệu

- **FR-001**: Hệ thống PHẢI phân biệt được cây nến **đã đóng** và cây nến **đang hình thành**, và PHẢI tính toàn bộ chỉ báo **chỉ trên nến đã đóng**.
- **FR-002**: Giá hiện tại dùng cho mục đích hiển thị và tính khoảng cách tới các mức giá PHẢI lấy từ nguồn giá thời gian thực, tách biệt khỏi chuỗi nến dùng để tính chỉ báo.
- **FR-003**: Hệ thống PHẢI truy xuất được các dữ liệu thị trường công khai bổ sung: phí vốn (funding rate), lượng hợp đồng mở (open interest) theo chuỗi thời gian, tỷ lệ tài khoản mua/bán toàn thị trường, độ sâu sổ lệnh, và tỷ lệ khối lượng chủ động mua/bán.
- **FR-004**: Việc bổ sung các nguồn dữ liệu tại FR-003 KHÔNG ĐƯỢC yêu cầu khoá truy cập tài khoản; chúng PHẢI là dữ liệu công khai.
- **FR-005**: Hệ thống PHẢI lưu trữ được lịch sử nến để chạy kiểm thử offline, có khả năng nạp bổ sung theo khoảng thời gian mà không nạp trùng.
- **FR-006**: Khi một nguồn dữ liệu tại FR-003 không khả dụng, tiêu chí chấm điểm phụ thuộc nguồn đó PHẢI nhận **0 điểm** (không phải điểm tối đa, không phải điểm trung bình), và sự kiện thiếu dữ liệu PHẢI được ghi vết.

#### Lịch sự kiện & cửa sổ chặn

- **FR-007**: Hệ thống PHẢI có một cuốn lịch sự kiện nội bộ lưu: loại sự kiện, thời điểm theo UTC, mức tác động, và nguồn gốc.
- **FR-008**: Các sự kiện kinh tế định kỳ (chỉ số giá tiêu dùng, bảng lương phi nông nghiệp, các cuộc họp chính sách tiền tệ) PHẢI được nạp từ lịch công bố chính thức đã có sẵn trước cả năm, KHÔNG được sinh ra bởi mô hình ngôn ngữ.
- **FR-009**: Các sự kiện tính được bằng công thức lịch (thanh toán phí vốn theo chu kỳ 8 giờ, đáo hạn quyền chọn hàng tuần và hàng tháng, khoảng trống cuối tuần) PHẢI được sinh tự động, không cần nạp tay.
- **FR-010**: Hệ thống PHẢI chặn vào lệnh mới trong các cửa sổ sau, tính theo thời điểm sự kiện T:
  | Nhóm sự kiện | Cửa sổ chặn |
  |---|---|
  | Chỉ số giá tiêu dùng / chỉ số giá sản xuất / bảng lương phi nông nghiệp | T−60 phút → T+30 phút |
  | Công bố quyết định chính sách tiền tệ | T−90 phút → T+30 phút |
  | Họp báo sau quyết định chính sách | Toàn bộ 60 phút của buổi họp báo |
  | Chi tiêu tiêu dùng cá nhân / tổng sản phẩm quốc nội / trợ cấp thất nghiệp | T−30 phút → T+15 phút |
  | Đáo hạn quyền chọn | T−30 phút → T+30 phút |
  | Thanh toán phí vốn | T−5 phút → T+5 phút |
  | Khoảng trống cuối tuần | 21:00 → 23:00 UTC Chủ nhật |
  | Tin đột xuất mức tác động cao trở lên | T → T+60 phút |
- **FR-011**: Cửa sổ chặn do AI đề xuất PHẢI bị giới hạn độ dài tối đa; đề xuất vượt trần bị cắt về trần và ghi vết.
- **FR-012**: Các cửa sổ chặn chồng lấn PHẢI được hợp nhất thành một cửa sổ liên tục.
- **FR-013**: Khi một vị thế đang mở và một cửa sổ chặn sắp bắt đầu, hệ thống PHẢI thực hiện hành động giảm rủi ro đã cấu hình và thông báo cho trader. Để nguyên trạng KHÔNG phải là hành vi hợp lệ.
- **FR-014**: Khi lịch sự kiện kinh tế rỗng hoặc quá hạn cập nhật, hệ thống PHẢI vẫn cưỡng chế các cửa sổ tính được bằng công thức, và PHẢI cảnh báo trader rằng lịch đang thiếu.
- **FR-015**: Mọi lần chặn PHẢI ghi vết lý do dạng cấu trúc gồm: loại sự kiện, thời điểm sự kiện, biên cửa sổ, và thời điểm đánh giá.

#### Kế hoạch ngày

- **FR-016**: Hệ thống PHẢI sinh một kế hoạch cho mỗi ngày giao dịch, một lần, trước khi ngày UTC bắt đầu.
- **FR-017**: Kế hoạch ngày PHẢI được suy ra bằng công thức xác định từ: cấu trúc giá khung ngày (20 phiên gần nhất), phân vị biên độ dao động so với 90 phiên gần nhất, các mức giá tham chiếu (đỉnh/đáy phiên trước, giá mở tuần, giá mở ngày), phí vốn, biến động lượng hợp đồng mở 24 giờ, tỷ lệ tài khoản mua/bán, và chỉ số tâm lý thị trường.
- **FR-018**: Kế hoạch ngày PHẢI chứa: trạng thái ngày, các chiều được phép vào lệnh, hệ số rủi ro, số lệnh tối đa trong ngày, danh sách mức giá tham chiếu, và danh sách cửa sổ chặn của ngày.
- **FR-019**: Ánh xạ từ trạng thái ngày sang tham số PHẢI theo đúng bảng sau:
  | Trạng thái ngày | Chiều được phép | Hệ số rủi ro | Số lệnh tối đa |
  |---|---|---|---|
  | Xu hướng tăng + dao động bình thường | Chỉ mua | 1.0 | 5 |
  | Xu hướng giảm + dao động bình thường | Chỉ bán | 1.0 | 5 |
  | Đi ngang + dao động thấp | Cả hai | 0.5 | 3 |
  | Bất kỳ + dao động cực đoan | Cả hai | 0.3 | 2 |
  | Ngày có sự kiện kinh tế tác động cao | Cả hai | 0.4 | 2 |
- **FR-020**: Khi nhiều dòng của bảng FR-019 cùng khớp, hệ thống PHẢI lấy **hệ số rủi ro nhỏ nhất** và **số lệnh tối đa nhỏ nhất** trong các dòng khớp.
- **FR-021**: Lệnh có chiều không nằm trong danh sách chiều được phép PHẢI bị từ chối, bất kể điểm số.
- **FR-022**: Khi một hoặc vài đầu vào của FR-017 không lấy được, kế hoạch PHẢI vẫn được sinh từ phần còn lại, ghi rõ thành phần thiếu, và hệ số rủi ro KHÔNG ĐƯỢC cao hơn giá trị lẽ ra có nếu đủ dữ liệu.
- **FR-023**: Khi chưa có kế hoạch hợp lệ cho ngày hiện tại, hệ thống PHẢI chặn mọi lệnh mới. KHÔNG ĐƯỢC dùng kế hoạch mặc định cho phép giao dịch.
- **FR-024**: Ngày giao dịch PHẢI bắt đầu và kết thúc tại 00:00 UTC.

#### Chấm điểm vào lệnh

- **FR-025**: Hệ thống PHẢI chấm mỗi cơ hội theo thang 0–100, tổng hợp từ bốn nhóm với trọng số: Kỹ thuật 40, Bối cảnh thị trường 30, Thanh khoản 15, Kỷ luật trader (chỉ trừ điểm và veto).
- **FR-026**: Nhóm **Kỹ thuật** (40 điểm) PHẢI gồm: đồng thuận thiên hướng khung lớn (0–10), cấu trúc thị trường phá vỡ và kiểm định lại (0–10), vị trí vào lệnh so với vùng giá trị (0–8), động lượng (0–7), xác nhận khối lượng (0–5).
- **FR-027**: Tiêu chí "vị trí vào lệnh" PHẢI nhận **0 điểm** khi giá đã di chuyển quá 1.5 lần biên độ dao động khỏi vùng xác nhận.
- **FR-028**: Nhóm **Bối cảnh thị trường** (30 điểm) PHẢI gồm: khớp trạng thái ngày (0–10), phân vị biên độ dao động (0–6), chất lượng khung giờ (0–6), tương quan với tài sản dẫn dắt (0–4), mức độ đông đúc của vị thế theo phí vốn (0–4).
- **FR-029**: Nhóm **Thanh khoản** (15 điểm) PHẢI gồm: biến động lượng hợp đồng mở (0–5), vị trí các vùng thanh khoản so với mức chốt lời và mức cắt lỗ (0–5), chênh lệch giá mua-bán và độ sâu sổ lệnh (0–5).
- **FR-030**: Chất lượng khung giờ PHẢI dùng bảng phiên chuẩn theo giờ UTC khi tài khoản có **dưới 50** lệnh đã đóng, và chuyển sang tỷ lệ thắng thực tế theo giờ của chính trader khi đạt **từ 50** lệnh đã đóng trở lên.
- **FR-031**: Bảng phiên chuẩn PHẢI theo đúng phân bổ sau (thang 0–6):
  | Khung giờ UTC | Điểm |
  |---|---|
  | 00:00–07:00 | 2 |
  | 07:00–09:00 | 5 |
  | 09:00–13:00 | 5 |
  | 13:00–16:00 | 6 |
  | 16:00–21:00 | 4 |
  | 21:00–00:00 | 1 |
- **FR-032**: Các điều kiện sau PHẢI là **veto cứng** — dừng chấm điểm ngay và từ chối cơ hội: thiên hướng khung lớn ngược kế hoạch ngày; chiều lệnh không nằm trong chiều được phép; đang trong cửa sổ chặn; chưa có kế hoạch ngày hợp lệ; bất kỳ gate kỷ luật chặn nào tại FR-035.
- **FR-033**: Ánh xạ từ điểm sang kích thước PHẢI theo đúng bảng sau:
  | Điểm | Kích thước cơ sở |
  |---|---|
  | < 55 | Không vào lệnh |
  | 55–69 | 0.5 đơn vị rủi ro |
  | 70–84 | 1.0 đơn vị rủi ro |
  | ≥ 85 | 1.5 đơn vị rủi ro (trần) |
- **FR-034**: Kích thước cuối cùng = kích thước cơ sở × hệ số rủi ro của kế hoạch ngày × hệ số điều chỉnh từ gate kỷ luật. Kết quả KHÔNG BAO GIỜ được lớn hơn kích thước cơ sở.
- **FR-035**: Nhóm **Kỷ luật trader** PHẢI cưỡng chế các gate sau:
  | Điều kiện | Hành động |
  |---|---|
  | 2 lệnh thua liên tiếp | Nhân kích thước × 0.5 |
  | 3 lệnh thua liên tiếp | Chặn đến hết ngày |
  | Chạm giới hạn lỗ ngày | Chặn đến hết ngày |
  | Vào lệnh trong vòng 15 phút sau một lệnh thua | Chặn cơ hội này |
  | Kích thước vượt 1.5 lần trung bình 20 lệnh gần nhất | Chặn cơ hội này |
  | Đã đủ số lệnh tối đa của kế hoạch ngày | Chặn cơ hội này |
  | Giờ hiện tại nằm trong 2 khung giờ thua nhiều nhất của trader | Trừ 10 điểm |
- **FR-036**: Mọi ngưỡng tại FR-035 PHẢI đọc từ cấu hình theo tài khoản, KHÔNG được hardcode.
- **FR-037**: Cùng một trạng thái đầu vào PHẢI luôn cho cùng một điểm số và cùng một quyết định.
- **FR-038**: Hệ thống KHÔNG ĐƯỢC có bất kỳ cơ chế nào tự động nới ngưỡng, hạ tiêu chí, hay tăng số lệnh tối đa nhằm đạt một số lệnh mục tiêu. Không có lệnh nào trong ngày là kết quả hợp lệ.
- **FR-039**: Mỗi lần chấm điểm PHẢI ghi vết đầy đủ: điểm từng tiêu chí, điểm tổng, veto (nếu có) kèm lý do, kích thước tính ra, và toàn bộ đầu vào dùng để tính — kể cả khi không vào lệnh.

#### Lớp bối cảnh AI

- **FR-040**: AI CHỈ được phép sinh ra: mức rủi ro của ngày, mô tả bối cảnh dạng văn bản, các cửa sổ chặn bổ sung cho tin đột xuất, và phân loại mức độ của một tin tức.
- **FR-041**: AI KHÔNG BAO GIỜ được: chọn chiều lệnh, đặt giá vào / cắt lỗ / chốt lời, tạo lệnh, tăng kích thước, tăng hệ số rủi ro, hay nới bất kỳ ngưỡng nào. Phản hồi chứa các nội dung này PHẢI bị loại bỏ và ghi vết.
- **FR-042**: Giá trị do AI đề xuất chỉ được áp dụng khi nó làm cho quyết định **thận trọng hơn hoặc bằng** quyết định tất định.
- **FR-043**: Dữ liệu lịch gửi cho AI PHẢI đi kèm ràng buộc chỉ được dùng đúng các sự kiện trong dữ liệu đó. Sự kiện AI trả về mà không đối chiếu được với dữ liệu đầu vào PHẢI bị loại bỏ và ghi vết.
- **FR-044**: Mỗi mẩu bối cảnh do AI sinh ra PHẢI có thời hạn; sau thời hạn, bối cảnh được coi là trung tính.
- **FR-045**: Khi AI không cấu hình, hết hạn ngạch, timeout, hay trả sai định dạng, toàn bộ vòng quyết định vào lệnh PHẢI chạy trọn vẹn và không bị ảnh hưởng.
- **FR-046**: Tổng số lần gọi dịch vụ AI trong một ngày giao dịch bình thường PHẢI dưới 30 lần.
- **FR-047**: Mọi văn bản do AI sinh ra hướng tới trader PHẢI bằng tiếng Việt.

#### Chu kỳ chạy

- **FR-048**: Hệ thống PHẢI tách các chu kỳ nền thành: sinh kế hoạch ngày (1 lần/ngày, trước 00:00 UTC), đánh giá cơ hội vào lệnh (mỗi khi một nến 15 phút đóng), quản lý vị thế đang mở (mỗi phút), và quét tin tức (mỗi 15 phút, chỉ xử lý khi có tin mới).
- **FR-049**: Chu kỳ đánh giá cơ hội và chu kỳ quản lý vị thế PHẢI KHÔNG gọi dịch vụ AI.
- **FR-050**: Lỗi ở một symbol trong một chu kỳ KHÔNG ĐƯỢC chặn các symbol còn lại.
- **FR-051**: Hai lần chạy chồng lấn của cùng một chu kỳ KHÔNG ĐƯỢC sinh hai lệnh cho cùng một cơ hội.

#### Kiểm thử lịch sử

- **FR-052**: Hệ thống PHẢI chạy lại được toàn bộ tầng kế hoạch ngày, chặn theo khung giờ, và chấm điểm trên dữ liệu lịch sử, hoàn toàn offline.
- **FR-053**: Kiểm thử lịch sử PHẢI dùng **đúng cùng một bộ logic** với chạy thật. Nhánh mã riêng cho kiểm thử là vi phạm.
- **FR-054**: Chuỗi quyết định của kiểm thử lịch sử và chuỗi quyết định của chế độ mô phỏng trên cùng dữ liệu PHẢI trùng khớp.
- **FR-055**: Báo cáo kiểm thử PHẢI gồm tối thiểu: số lệnh, tỷ lệ thắng, kỳ vọng theo đơn vị rủi ro, mức sụt giảm vốn lớn nhất, chuỗi thua dài nhất, và phân rã kết quả theo khung giờ và theo trạng thái ngày.
- **FR-056**: Kiểm thử PHẢI tính đến phí giao dịch và trượt giá; bỏ qua hai yếu tố này là vi phạm.

#### Chế độ so sánh song song

- **FR-057**: Đường sinh tín hiệu bằng AI hiện có PHẢI được chuyển sang chế độ chỉ-ghi-nhật-ký: vẫn chạy, vẫn lưu bản kiểm toán, nhưng KHÔNG BAO GIỜ tạo lệnh.
- **FR-058**: Hệ thống PHẢI ghi lại các điểm bất đồng giữa hai đường để đối chiếu về sau.
- **FR-059**: Chế độ so sánh PHẢI tắt được qua cấu hình; khi tắt, không còn lần gọi AI nào cho mục đích sinh tín hiệu.
- **FR-060**: Báo cáo so sánh PHẢI nêu: số đề xuất mỗi bên, số điểm bất đồng, và kết quả giả định của các đề xuất bên AI nếu chúng đã được thực thi.

#### Ràng buộc an toàn kế thừa

- **FR-061**: Giao dịch thật PHẢI vẫn mặc định TẮT.
- **FR-062**: Toàn bộ chuỗi lớp chặn trước khi gửi lệnh của hệ thống baseline PHẢI được giữ nguyên vẹn và đúng thứ tự. Feature này CHỈ được **thêm** lớp chặn, KHÔNG được bớt.
- **FR-063**: Kích thước lệnh sau khi bị nâng lên cho đạt mức tối thiểu của sàn PHẢI được chấm lại toàn bộ quy tắc trước khi gửi.
- **FR-064**: Không có bí mật nào (khoá sàn, khoá dịch vụ AI) được xuất hiện trong bản ghi chấm điểm, bản ghi kế hoạch ngày, hay báo cáo kiểm thử.

### Key Entities

- **Kế hoạch ngày**: bản nhận định cho một ngày UTC. Gồm trạng thái ngày, các chiều được phép, hệ số rủi ro, số lệnh tối đa, các mức giá tham chiếu, các cửa sổ chặn, danh sách thành phần dữ liệu bị thiếu, và thời điểm sinh. Một ngày có đúng một bản.
- **Sự kiện đã lên lịch**: một mục trong cuốn lịch nội bộ. Gồm loại, thời điểm UTC, mức tác động, nguồn gốc (nạp tay hay tính bằng công thức), và cửa sổ chặn suy ra.
- **Cửa sổ chặn**: một khoảng thời gian cấm vào lệnh mới. Gồm biên đầu, biên cuối, lý do, và sự kiện gốc.
- **Phiếu chấm điểm**: bản ghi một lần đánh giá cơ hội. Gồm symbol, thời điểm nến đóng, điểm từng tiêu chí của bốn nhóm, điểm tổng, veto và lý do, kích thước tính ra, và ảnh chụp toàn bộ đầu vào. Được lưu **kể cả khi không vào lệnh**.
- **Bối cảnh thị trường**: một mẩu thông tin do AI sinh ra. Gồm loại, mức độ, các symbol liên quan, thiên hướng, thời điểm ghi nhận, và thời hạn. Hết hạn thì coi như trung tính.
- **Chất lượng khung giờ**: điểm số cho một khung giờ UTC, lấy từ bảng phiên chuẩn hoặc từ thống kê thực tế của trader tuỳ theo số lệnh đã đóng.
- **Bản ghi nến lịch sử**: dữ liệu giá lưu trữ để chạy kiểm thử offline. Gồm symbol, khung thời gian, thời điểm mở và đóng, giá và khối lượng, và cờ đã đóng.
- **Phiên kiểm thử**: một lần chạy trên dữ liệu lịch sử. Gồm khoảng thời gian, cấu hình tham số, các quyết định sinh ra, và các chỉ số tổng kết.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Vòng quyết định vào lệnh chạy trọn vẹn khi dịch vụ AI bị ngắt hoàn toàn trong 24 giờ liên tục — không lệnh nào thất bại vì lý do đó.
- **SC-002**: Chạy chấm điểm hai lần trên cùng một trạng thái đầu vào cho ra kết quả giống hệt nhau ở 100% số lần thử.
- **SC-003**: Chuỗi quyết định từ kiểm thử lịch sử trùng khớp 100% với chuỗi quyết định từ chế độ mô phỏng trên cùng dữ liệu và cùng khoảng thời gian.
- **SC-004**: Chỉ báo tính tại thời điểm giữa chu kỳ nến và tính lại sau khi nến đóng cho ra giá trị giống nhau — không có dao động trong chu kỳ.
- **SC-005**: Trong một ngày giao dịch bình thường, tổng số lần gọi dịch vụ AI dưới 30.
- **SC-006**: 100% các cửa sổ chặn đã định nghĩa tại FR-010 đều có ít nhất một kiểm thử chứng minh chúng **thực sự chặn**, và một kiểm thử ở ngay ngoài biên chứng minh chúng **không chặn nhầm**.
- **SC-007**: 100% các gate kỷ luật tại FR-035 đều có kiểm thử cho cả trường hợp kích hoạt và trường hợp ngay dưới ngưỡng.
- **SC-008**: Không tồn tại đường dẫn mã nào cho phép đầu ra của AI làm tăng kích thước lệnh, nới ngưỡng, hay tạo lệnh — chứng minh bằng kiểm thử với phản hồi AI cố tình vi phạm.
- **SC-009**: Khi lịch sự kiện kinh tế rỗng, các cửa sổ chặn tính bằng công thức vẫn cưỡng chế đủ 100%.
- **SC-010**: Số lớp chặn trước khi gửi lệnh sau feature này **lớn hơn hoặc bằng** số lớp chặn của hệ thống baseline — chứng minh bằng kiểm thử đếm lớp.
- **SC-011**: Kiểm thử lịch sử chạy được trọn vẹn ở chế độ ngắt mạng hoàn toàn.
- **SC-012**: Mỗi lần đánh giá cơ hội đều để lại một phiếu chấm điểm truy vấn được, kể cả khi kết quả là không vào lệnh — tỷ lệ ghi vết đạt 100%.
- **SC-013**: Trader tra được lý do một cơ hội bị từ chối trong dưới 30 giây, không cần đọc log tệp.
- **SC-014**: Giao dịch thật vẫn ở trạng thái tắt sau khi triển khai feature — xác nhận bằng kiểm thử cấu hình mặc định.

---

## Assumptions

Các giả định dưới đây được chọn làm mặc định hợp lý khi mô tả feature không nêu rõ. Chúng đều là **quyết định có thể đảo ngược** bằng cấu hình hoặc bằng một feature tiếp theo.

1. **Phạm vi symbol**: chỉ hai symbol thanh khoản sâu nhất. Việc mở rộng danh sách là feature riêng, vì nó kéo theo bộ lọc thanh khoản và xử lý tương quan phức tạp hơn.
2. **Khung thời gian**: 15 phút cho vào lệnh, 4 giờ cho thiên hướng, 1 ngày cho kế hoạch ngày.
3. **Đơn vị rủi ro (1R)**: bằng phần trăm rủi ro tối đa mỗi lệnh đã cấu hình trên tài khoản. Không định nghĩa mới.
4. **Chiến lược v1 là thuận xu hướng**. Nhóm tiêu chí kỹ thuật được thiết kế quanh phá vỡ cấu trúc và kiểm định lại. **Hệ quả: ngày đi ngang sẽ hầu như không ra lệnh nào.** Bộ tiêu chí hồi quy về trung bình cho ngày đi ngang nằm ngoài phạm vi feature này — xem mục Ngoài phạm vi.
5. **Vùng thanh khoản** được xấp xỉ bằng các đỉnh/đáy dao động gần nhất và các mức giá tròn, vì dữ liệu bản đồ thanh lý không có sẵn công khai. Đây là xấp xỉ, không phải dữ liệu thật, và phải được ghi vết như vậy.
6. **Xử lý vị thế khi vào cửa sổ chặn**: mặc định kéo mức cắt lỗ về điểm hoà vốn nếu vị thế đang lãi từ 0.5 đơn vị rủi ro trở lên; ngược lại đóng một nửa vị thế. Hành vi này phải cấu hình được.
7. **Trần độ dài cửa sổ chặn do AI đề xuất**: 120 phút.
8. **Ngưỡng chuyển sang thống kê giờ cá nhân**: 50 lệnh đã đóng. Dưới mức đó, mẫu quá nhỏ để tin.
9. **Mốc ngày giao dịch**: 00:00 UTC, trùng thời điểm nến ngày đóng và một mốc thanh toán phí vốn.
10. **Nguồn chỉ số tâm lý thị trường** là một dịch vụ công khai miễn phí; khi không truy cập được, tiêu chí liên quan nhận 0 điểm theo FR-006.
11. **Phí và trượt giá trong kiểm thử** dùng mức phí công khai của sàn cho lệnh chủ động khớp, cộng một khoản trượt giá cố định theo cấu hình.
12. **Chế độ so sánh song song** dự kiến chạy 1–2 tháng rồi đánh giá lại; nó không phải thành phần vĩnh viễn.
13. Hạ tầng baseline (nhật ký lệnh, bộ quy tắc, phát hiện hành vi, chuỗi lớp chặn, thông báo, ghi vết) được **tái sử dụng nguyên trạng**, không viết lại.

---

## Ngoài phạm vi

Các mục sau **không** thuộc feature này và cần feature riêng nếu muốn làm:

- Bộ tiêu chí hồi quy về trung bình cho ngày đi ngang (xem Giả định 4).
- Mở rộng danh sách symbol ngoài hai symbol đã chốt.
- Vào lệnh trên khung thời gian khác 15 phút.
- Tối ưu tham số tự động trên dữ liệu lịch sử — rủi ro khớp quá mức cao, cần thiết kế riêng có phân tách dữ liệu trong/ngoài mẫu.
- Kết nối luồng dữ liệu thời gian thực thay cho truy vấn định kỳ.
- Dữ liệu bản đồ thanh lý thật từ nhà cung cấp bên thứ ba.
- Bật giao dịch thật. Feature này kết thúc ở trạng thái mô phỏng và mạng thử nghiệm.
- Bất kỳ thay đổi nào tới giao diện ngoài các trang tra cứu phiếu chấm điểm, kế hoạch ngày, và báo cáo kiểm thử.

---

## Đối chiếu hiến chương

| Nguyên tắc | Feature này tuân thủ ra sao |
|---|---|
| I — Kỷ luật hơn dự đoán | FR-035 nâng ba bộ phát hiện hành vi từ cảnh báo lên rào chắn. FR-038 cấm mọi cơ chế tự nới ngưỡng để tăng số lệnh. |
| II — Deterministic trước, AI sau | Toàn bộ FR-025 → FR-039 là tất định. FR-045 bảo đảm vòng quyết định chạy trọn vẹn khi AI chết. FR-041 cấm AI chạm vào quyết định. |
| III — An toàn mặc định cho tiền thật | FR-061 → FR-063 giữ nguyên chuỗi lớp chặn và trạng thái tắt mặc định. FR-032 thêm veto cứng, không bớt lớp nào. |
| IV — Ghi vết toàn bộ | FR-039 lưu phiếu chấm điểm kể cả khi không vào lệnh. FR-015 ghi vết mọi lần chặn. FR-064 cấm bí mật lọt vào bản ghi. |
| V — Kiến trúc phân tầng, quy tắc plug-in | Mỗi tiêu chí chấm điểm là một đơn vị độc lập; thêm tiêu chí mới không được sửa vòng lặp tổng hợp điểm. |
| VI — Test tương xứng rủi ro | SC-006, SC-007, SC-008, SC-010 đều là chỉ tiêu về độ phủ kiểm thử ở vùng chạm tiền thật. |
| VII — Bí mật không nằm trong mã | FR-004 chọn nguồn dữ liệu công khai không cần khoá. FR-064 chặn rò rỉ qua bản ghi mới. |
