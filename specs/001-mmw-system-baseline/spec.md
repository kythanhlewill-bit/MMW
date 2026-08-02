# Feature Specification: MMW — Trợ lý kỷ luật giao dịch crypto (baseline hệ thống)

**Feature Branch**: `main`

**Created**: 2026-07-28

**Status**: Baseline (reverse-engineered từ mã nguồn hiện có)

**Input**: User description: "Phân tích hệ thống MMW hiện tại ra file spec"

> **Phạm vi tài liệu**: Đây là **baseline spec** — đặc tả lại toàn bộ hành vi hệ thống MMW **đang tồn tại trong mã nguồn** tại thời điểm 2026-07-28, để làm gốc cho các vòng `/speckit-plan` → `/speckit-tasks` về sau. Tài liệu mô tả **CÁI GÌ** và **TẠI SAO**; chi tiết hiện trạng triển khai được tách riêng ở Phụ lục A.

---

## Bối cảnh & Vấn đề

Người dùng là trader crypto cá nhân (futures USDT-M). Vấn đề cốt lõi **không phải** thiếu khả năng dự đoán thị trường, mà là **mất kỷ luật**: vào lệnh trả thù sau khi cắt lỗ, tăng size khi đang tilt, bỏ stop loss, giao dịch quá số lệnh cho phép, giao dịch ngay trước tin vĩ mô mạnh.

MMW giải quyết bằng cách biến kỷ luật thành **cơ chế cưỡng chế được đo đếm**: mọi lệnh đều bị chấm theo bộ quy tắc rủi ro cá nhân hoá, mọi hành vi tâm lý tiêu cực đều được phát hiện và gắn cờ, và mọi lệnh thật gửi lên sàn đều phải vượt qua nhiều lớp chặn an toàn.

**Nguyên tắc nền (bất biến):**

1. **Kỷ luật hơn dự đoán** — hệ thống không hứa hẹn tín hiệu thắng; nó hứa hẹn chặn lệnh sai kỷ luật.
2. **Deterministic trước, AI sau** — quy tắc rủi ro và chỉ số rủi ro luôn tính bằng công thức xác định; AI chỉ là lớp tư vấn/lọc thêm, không bao giờ là lớp duy nhất quyết định.
3. **An toàn mặc định** — giao dịch thật mặc định TẮT; mọi lớp chặn mặc định BẬT; muốn nới phải bật cờ rõ ràng.
4. **Ghi nhật ký toàn bộ** — mọi lần quét, mọi lần gọi AI, mọi lần chạm API sàn đều lưu vết để review lại được.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Ghi nhật ký lệnh và bị chấm kỷ luật ngay (Priority: P1)

Trader ghi nhận một lệnh (thêm tay hoặc từ đề xuất): symbol, hướng, giá vào, stop loss, take profit, khối lượng, đòn bẩy, tâm lý trước khi vào lệnh. Ngay khi lưu, hệ thống tính các chỉ số rủi ro và chấm lệnh theo bộ quy tắc của tài khoản, sinh cảnh báo nếu vi phạm. Khi đóng lệnh, hệ thống tính lãi/lỗ thực tế, cập nhật số dư và kết quả theo bội số R.

**Why this priority**: Đây là MVP tối thiểu có giá trị. Không có journal + chấm rule thì mọi tính năng còn lại đều vô nghĩa.

**Independent Test**: Tạo tài khoản giao dịch với số dư và ngưỡng rủi ro → thêm một lệnh vi phạm (không có SL, hoặc rủi ro 5% khi ngưỡng 1%) → xác nhận hệ thống hiển thị cảnh báo đúng mức độ → đóng lệnh với giá thoát → xác nhận PnL, số dư và R-multiple đúng.

**Acceptance Scenarios**:

1. **Given** tài khoản có ngưỡng rủi ro tối đa 1%/lệnh, **When** trader lưu lệnh có mức rủi ro 2.5% vốn, **Then** hệ thống gắn cờ "vượt ngưỡng rủi ro" mức Cảnh báo và hiển thị số liệu thực tế so với ngưỡng.
2. **Given** tài khoản bắt buộc có Stop Loss, **When** trader lưu lệnh không nhập Stop Loss, **Then** hệ thống gắn cờ mức Nghiêm trọng "rủi ro không giới hạn".
3. **Given** một lệnh đang mở, **When** trader đóng lệnh tại giá thoát, **Then** hệ thống tính lãi/lỗ theo hướng lệnh, cộng/trừ vào số dư tài khoản, xác định kết quả Thắng/Thua/Hoà và tính bội số R.
4. **Given** một lệnh đã đóng, **When** trader xoá lệnh đó, **Then** hệ thống hoàn lãi/lỗ về số dư và tính lại tổng hợp ngày.
5. **Given** trader đã có 5 lệnh trong ngày và ngưỡng là 5 lệnh/ngày, **When** ghi lệnh thứ 6, **Then** hệ thống cảnh báo vượt giới hạn số lệnh/ngày.

---

### User Story 2 — Phát hiện hành vi tâm lý tiêu cực (Priority: P1)

Ngoài quy tắc cứng về số liệu, hệ thống đối chiếu lệnh mới với **lịch sử giao dịch gần đây** để phát hiện mẫu hành vi nguy hiểm: vào lệnh ngay sau khi cắt lỗ (revenge), đang trong chuỗi thua liên tiếp, tăng vọt kích thước lệnh ngay sau một lệnh thua (tilt).

**Why this priority**: Đây là khác biệt cốt lõi của sản phẩm so với một journal thông thường. Nó nói cho trader biết điều mà bảng số liệu không nói.

**Independent Test**: Nạp lịch sử 3 lệnh thua liên tiếp → tạo lệnh mới cách lệnh thua cuối 10 phút với size gấp 3 lần trung bình → xác nhận hệ thống sinh đồng thời cả 3 cảnh báo hành vi với mức độ đúng.

**Acceptance Scenarios**:

1. **Given** lệnh thua gần nhất đóng lúc T và ngưỡng cửa sổ revenge là 30 phút, **When** trader mở lệnh mới tại T+10 phút, **Then** hệ thống gắn cờ "revenge trade"; vào càng sớm (dưới 1/3 cửa sổ) mức độ càng nghiêm trọng.
2. **Given** ngưỡng chuỗi thua là 3, **When** trader vào lệnh sau 3 lệnh thua liên tiếp, **Then** hệ thống cảnh báo chuỗi thua; từ 6 lệnh thua liên tiếp trở lên thì mức Nghiêm trọng.
3. **Given** lệnh liền trước là lệnh thua và ngưỡng tăng size là 50%, **When** lệnh mới có giá trị danh nghĩa lớn hơn trung bình 10 lệnh gần nhất quá 50%, **Then** hệ thống cảnh báo dấu hiệu tilt; vượt gấp đôi trung bình thì mức Nghiêm trọng.
4. **Given** lệnh liền trước KHÔNG phải lệnh thua, **When** lệnh mới có size lớn bất thường, **Then** hệ thống KHÔNG gắn cờ tilt (chỉ xét tilt ngay sau lệnh thua).

---

### User Story 3 — Quét thị trường và sinh đề xuất lệnh có kiểm duyệt (Priority: P2)

Hệ thống tự động quét định kỳ danh sách theo dõi của trader: lấy dữ liệu giá, tính chỉ báo kỹ thuật, suy ra thiên hướng thị trường, rồi hỏi AI xem có nên vào lệnh không. Đề xuất chỉ được lưu khi vượt điểm tối thiểu, đúng chiều giá (SL/Entry/TP hợp lệ) và đạt tỷ lệ Reward:Risk tối thiểu của tài khoản. Mọi đề xuất còn phải đi qua một vòng **preflight** thứ hai để được chấm điểm và (nếu cần) đề xuất lại SL/TP tối ưu hơn.

**Why this priority**: Có giá trị lớn nhưng phụ thuộc P1; trader vẫn dùng được hệ thống mà không có tính năng này.

**Independent Test**: Thêm một symbol vào watchlist → chạy quét thủ công → xác nhận có bản ghi chỉ báo, ảnh chụp thị trường và (nếu AI đồng ý) một đề xuất kèm lý do + nhãn quyết định của AI; nếu AI trả WAIT thì không có đề xuất nào được lưu nhưng vẫn có bản ghi kiểm toán giải thích lý do.

**Acceptance Scenarios**:

1. **Given** watchlist có symbol đang bật, **When** job quét chạy, **Then** hệ thống lưu một bản ghi lịch sử chỉ báo và cập nhật ảnh chụp thị trường mới nhất cho symbol đó.
2. **Given** AI trả về quyết định WAIT hoặc điểm thấp hơn ngưỡng tối thiểu, **When** quét xong, **Then** KHÔNG sinh đề xuất, nhưng lưu bản ghi kiểm toán ghi rõ lý do từ chối.
3. **Given** AI trả LONG nhưng Stop Loss lại nằm trên giá vào, **When** hệ thống kiểm tra, **Then** đề xuất bị loại vì sai phía giá.
4. **Given** AI trả đề xuất có Reward:Risk thấp hơn ngưỡng tối thiểu của tài khoản, **When** hệ thống kiểm tra, **Then** đề xuất bị loại.
5. **Given** một symbol lỗi mạng khi quét, **When** job đang chạy, **Then** các symbol còn lại vẫn được quét bình thường và lỗi được ghi log.
6. **Given** một đề xuất đã lưu, **When** trader bấm "Ghi nhận", **Then** biểu mẫu tạo lệnh được điền sẵn symbol/entry/SL/TP và khối lượng tự tính theo % rủi ro tài khoản.

---

### User Story 4 — Nhiều lớp chặn trước khi lệnh chạm sàn thật (Priority: P2)

Khi hệ thống (hoặc trader) yêu cầu gửi một lệnh **thật** lên sàn, lệnh phải vượt qua chuỗi kiểm tra: công tắc tổng phải bật, tài khoản phải có khoá API, AI phải được cấu hình, không được trùng lệnh/vị thế đang mở, phải có SL và TP đúng phía, không vượt cap đòn bẩy, cap giá trị danh nghĩa, giới hạn số lệnh live/ngày, và không có vi phạm quy tắc mức Nghiêm trọng. Bất kỳ lớp nào chặn thì lệnh bị đánh dấu Bị chặn kèm lý do và **không** để lại vị thế "ma" trong nhật ký.

**Why this priority**: Đây là nơi rủi ro tiền thật phát sinh. Sai ở đây tốn tiền, nên phải đặc tả chặt.

**Independent Test**: Bật công tắc live ở chế độ testnet → gửi lần lượt các lệnh vi phạm từng lớp một → xác nhận mỗi lệnh bị chặn với đúng lý do và không có lệnh nào chạm sàn.

**Acceptance Scenarios**:

1. **Given** công tắc giao dịch thật đang TẮT, **When** hệ thống yêu cầu gửi lệnh, **Then** không có yêu cầu nào được gửi tới sàn.
2. **Given** một lệnh thiếu Take Profit, **When** yêu cầu gửi lệnh thật, **Then** lệnh bị chặn với lý do "thiếu Take Profit" và trạng thái chuyển sang Đã huỷ.
3. **Given** đã tồn tại vị thế cùng symbol + cùng hướng trên sàn, **When** yêu cầu gửi lệnh mới tương tự, **Then** lệnh bị chặn vì trùng.
4. **Given** lệnh có cờ vi phạm mức Nghiêm trọng, **When** cờ "cho phép bỏ qua rủi ro" đang TẮT, **Then** lệnh bị chặn; **When** cờ đó được bật, **Then** lệnh được gửi nhưng ghi cảnh báo vào log.
5. **Given** khối lượng theo % rủi ro nhỏ hơn mức tối thiểu của sàn, **When** gửi lệnh, **Then** hệ thống nâng khối lượng lên mức tối thiểu hợp lệ và **chấm lại quy tắc** với khối lượng mới trước khi gửi.
6. **Given** lệnh vào sàn thành công nhưng đặt SL/TP thất bại sau 3 lần thử, **When** kết thúc quy trình, **Then** vị thế KHÔNG bị huỷ, lệnh được đánh dấu "chờ đặt lại SL/TP", trader được cảnh báo, và một tiến trình định kỳ tự thử lại.
7. **Given** yêu cầu gửi lệnh thất bại ngay ở bước vào lệnh, **When** xử lý lỗi, **Then** lệnh trong nhật ký được huỷ để nhật ký luôn khớp 1-1 với thực tế trên sàn.

---

### User Story 5 — Theo dõi lệnh đang mở và đồng bộ kết quả từ sàn (Priority: P2)

Với các lệnh đang mở, hệ thống định kỳ lấy giá và chỉ báo hiện tại để tính lãi/lỗ tạm tính, khoảng cách tới SL/TP, mức độ rủi ro, và đưa ra lời khuyên hành động cụ thể (giữ / dời SL / chốt một phần / cắt lỗ). Song song, hệ thống đối chiếu với lịch sử khớp lệnh trên sàn để tự đóng những lệnh đã thực sự đóng ngoài sàn.

**Why this priority**: Giữ cho nhật ký phản ánh đúng thực tế mà không cần trader nhập tay.

**Independent Test**: Tạo một lệnh đang mở → chạy job phân tích → xác nhận có bản phân tích với lãi/lỗ tạm tính, khoảng cách SL/TP và lời khuyên; đóng vị thế trên sàn → chạy job đồng bộ → xác nhận lệnh chuyển sang Đã đóng với lãi/lỗ thực tế.

**Acceptance Scenarios**:

1. **Given** một lệnh đang mở, **When** job phân tích chạy, **Then** hệ thống tạo/cập nhật bản phân tích gồm giá hiện tại, lãi/lỗ tạm tính (số tiền và %), khoảng cách tới SL/TP theo %, mức rủi ro và lời khuyên.
2. **Given** dịch vụ AI không khả dụng, **When** job phân tích chạy, **Then** vẫn có lời khuyên xác định (deterministic) — AI chỉ làm giàu thêm chứ không phải điều kiện bắt buộc.
3. **Given** vị thế trên sàn đã đóng, **When** job đồng bộ chạy, **Then** lệnh tương ứng được cập nhật lãi/lỗ thực tế, chuyển trạng thái Đã đóng và chạy lại toàn bộ luồng chấm rule + hành vi + tổng hợp ngày.
4. **Given** không đọc được vị thế trên sàn, **When** job đồng bộ chạy, **Then** hệ thống không tự đóng nhầm lệnh mà bỏ qua và thử lại lần sau.

---

### User Story 6 — Cảnh báo tin vĩ mô và trung tâm thông báo (Priority: P3)

Hệ thống quét lịch sự kiện/tin vĩ mô có tác động mạnh, cảnh báo trader trước vùng tin và đưa bối cảnh đó vào quyết định của AI. Mọi cảnh báo (vi phạm rule, tín hiệu mới, lệnh bị chặn, tin vĩ mô) đều đổ về một trung tâm thông báo có mức độ, có đánh dấu đã đọc, đẩy thời gian thực và tuỳ chọn gửi email theo cấu hình của người dùng.

**Why this priority**: Nâng cao trải nghiệm và độ an toàn, nhưng hệ thống vẫn vận hành được nếu thiếu.

**Independent Test**: Cấu hình một sự kiện vĩ mô tác động cao trong 30 phút tới → chạy quét → xác nhận có thông báo cảnh báo và bối cảnh tin được đưa vào phần đánh giá đề xuất.

**Acceptance Scenarios**:

1. **Given** có sự kiện tác động cao trong khoảng 45 phút trước đến 30 phút sau thời điểm hiện tại, **When** hệ thống đánh giá một setup, **Then** cảnh báo "đang gần khung giờ tin mạnh" được thêm vào phần rủi ro.
2. **Given** người dùng tắt kênh email cho một loại thông báo, **When** thông báo loại đó phát sinh, **Then** chỉ tạo thông báo trong ứng dụng, không xếp hàng gửi email.
3. **Given** một thông báo giống hệt vừa được tạo, **When** nguồn phát lại cùng nội dung, **Then** hệ thống bỏ qua để tránh spam trùng lặp.
4. **Given** người dùng đang mở ứng dụng, **When** một thông báo mới phát sinh, **Then** thông báo và số lượng chưa đọc được đẩy tới giao diện theo thời gian thực.
5. **Given** nguồn dữ liệu tin vĩ mô chưa được cấu hình, **When** hệ thống đánh giá setup, **Then** không chặn quy trình, chỉ ghi nhận rằng bối cảnh tin không khả dụng.

---

### User Story 7 — Bảng điều khiển, cấu hình và truy vết (Priority: P3)

Trader xem tổng quan hiệu suất (số lệnh, tỷ lệ thắng, PnL, chuỗi thua, cờ vi phạm) theo tài khoản; cấu hình ngưỡng rủi ro riêng cho từng tài khoản và các cài đặt toàn cục; và tra cứu lại toàn bộ vết gọi AI cũng như vết gọi API sàn để hiểu vì sao hệ thống ra quyết định như vậy.

**Why this priority**: Cần cho vận hành lâu dài và gỡ lỗi, nhưng không chặn luồng chính.

**Independent Test**: Đổi ngưỡng rủi ro của một tài khoản → tạo lệnh ở mức rủi ro nằm giữa ngưỡng cũ và mới → xác nhận kết quả chấm rule thay đổi theo; mở trang truy vết → xác nhận thấy được yêu cầu/phản hồi của lần gọi AI tương ứng.

**Acceptance Scenarios**:

1. **Given** trader đổi ngưỡng rủi ro tối đa mỗi lệnh, **When** lệnh tiếp theo được chấm, **Then** hệ thống dùng ngưỡng mới, không dùng giá trị mặc định.
2. **Given** tài khoản chưa từng cấu hình ngưỡng riêng, **When** một lệnh được chấm, **Then** hệ thống dùng bộ ngưỡng mặc định an toàn.
3. **Given** một lần quét đã gọi AI, **When** trader mở trang truy vết, **Then** thấy được prompt, dữ liệu gửi đi, phản hồi thô, trạng thái và lý do từ chối (nếu có).
4. **Given** trader chọn một tài khoản trên bảng điều khiển, **When** trang tải, **Then** mọi số liệu hiển thị chỉ thuộc tài khoản đó.

---

### Edge Cases

- **Stop Loss bằng đúng giá vào** → khoảng cách rủi ro bằng 0: hệ thống không tính được % rủi ro và Reward:Risk, phải để trống thay vì chia cho 0.
- **Số dư tài khoản bằng 0 hoặc âm**: không tính được % rủi ro; các quy tắc dựa trên % vốn phải bỏ qua thay vì cảnh báo sai.
- **Lệnh chưa có ngày vào lệnh**: tổng hợp ngày lấy theo ngày tạo bản ghi.
- **Chấm lại một lệnh nhiều lần**: cờ vi phạm cũ phải bị xoá trước khi ghi cờ mới (kết quả phải bất biến theo số lần chạy).
- **AI trả về văn bản kèm markdown/giải thích thay vì JSON thuần**: hệ thống phải bóc tách được JSON; nếu vẫn hỏng thì gọi lại một lần với prompt sửa lỗi; hỏng tiếp thì bỏ qua đề xuất và ghi vết.
- **AI trả điểm/độ tin cậy ngoài dải cho phép**: phải kẹp về dải hợp lệ thay vì tin tuyệt đối.
- **Sàn trả lỗi khi đọc vị thế**: quay về cơ chế chống trùng dựa trên dữ liệu nội bộ, không được để lỡ mất lớp chặn trùng.
- **Job chạy chồng lấn** (lần quét trước chưa xong đã tới lần sau): không được tạo lệnh trùng — chống trùng dựa trên symbol + hướng + giá xấp xỉ + trạng thái đang mở.
- **Gửi thông báo lỗi**: không bao giờ được làm hỏng luồng nghiệp vụ chính (chấm rule, quét, đặt lệnh).
- **Khối lượng theo % rủi ro nhỏ hơn mức tối thiểu sàn**: phải nâng lên và **chấm lại rule**, vì rủi ro thực tế đã tăng.

---

## Requirements *(mandatory)*

### Functional Requirements

#### Nhật ký giao dịch

- **FR-001**: Hệ thống PHẢI cho phép tạo, sửa, xoá và đóng lệnh giao dịch với các thuộc tính: symbol, hướng (Long/Short), loại lệnh, giá vào, stop loss, take profit, khối lượng, đòn bẩy, phí, ghi chú, tâm lý trước và sau lệnh.
- **FR-002**: Hệ thống PHẢI tính tự động và lưu sẵn các chỉ số: số tiền rủi ro = |giá vào − stop loss| × khối lượng; % rủi ro = số tiền rủi ro / vốn × 100; Reward:Risk dự kiến = |take profit − giá vào| / |giá vào − stop loss|; bội số R = lãi/lỗ thực tế / số tiền rủi ro.
- **FR-003**: Khi đóng lệnh, hệ thống PHẢI tính lãi/lỗ theo hướng lệnh, cập nhật số dư tài khoản, xác định kết quả Thắng/Thua/Hoà và tính bội số R.
- **FR-004**: Khi xoá một lệnh đã đóng, hệ thống PHẢI hoàn lại lãi/lỗ vào số dư và xoá các cờ liên quan.
- **FR-005**: Hệ thống PHẢI hỗ trợ nhiều tài khoản giao dịch độc lập, mỗi tài khoản có số dư và bộ ngưỡng rủi ro riêng.
- **FR-006**: Hệ thống PHẢI cho phép nhập lệnh từ lịch sử khớp lệnh của sàn và chống trùng bằng mã lệnh ngoài.

#### Bộ quy tắc kỷ luật

- **FR-007**: Hệ thống PHẢI chấm mỗi lệnh theo các quy tắc: bắt buộc có Stop Loss; % rủi ro mỗi lệnh không vượt ngưỡng; Reward:Risk không thấp hơn ngưỡng; số lệnh/ngày không vượt ngưỡng; tổng lỗ trong ngày không vượt % vốn cho phép.
- **FR-008**: Mỗi vi phạm PHẢI sinh một cờ có phân loại, mức độ (Thông tin/Cảnh báo/Nghiêm trọng), thông điệp tiếng Việt dễ hiểu và dữ liệu chi tiết kèm số liệu thực tế so với ngưỡng.
- **FR-009**: Mức độ PHẢI leo thang theo mức vi phạm — ví dụ rủi ro vượt gấp đôi ngưỡng thì lên mức Nghiêm trọng.
- **FR-010**: Việc chấm lại một lệnh PHẢI bất biến (idempotent): cờ vi phạm cũ bị thay thế, không tích luỹ trùng.
- **FR-011**: Mọi ngưỡng PHẢI đọc từ cấu hình theo tài khoản, KHÔNG được hardcode trong logic phát hiện.
- **FR-012**: Quy tắc và hành vi PHẢI được chấm **trước** khi cập nhật tổng hợp ngày, để "số lệnh trong ngày" phản ánh trạng thái *trước* lệnh đang xét.

#### Phát hiện hành vi

- **FR-013**: Hệ thống PHẢI phát hiện revenge trade: vào lệnh trong vòng N phút sau khi cắt lỗ, mức độ nặng hơn nếu vào sớm hơn 1/3 cửa sổ.
- **FR-014**: Hệ thống PHẢI phát hiện chuỗi thua liên tiếp đạt/vượt ngưỡng, chỉ tính lệnh đã đóng, chuỗi đứt khi gặp lệnh không thua.
- **FR-015**: Hệ thống PHẢI phát hiện tăng kích thước bất thường **ngay sau lệnh thua**, so với trung bình giá trị danh nghĩa của các lệnh đã đóng gần nhất.
- **FR-016**: Cờ hành vi PHẢI tách biệt khỏi cờ vi phạm quy tắc để review được riêng.

#### Quét thị trường & đề xuất

- **FR-017**: Hệ thống PHẢI cho phép quản lý danh sách theo dõi (symbol + khung thời gian, bật/tắt).
- **FR-018**: Hệ thống PHẢI quét định kỳ và lưu lịch sử chỉ báo cùng ảnh chụp trạng thái mới nhất cho từng mục theo dõi.
- **FR-019**: Hệ thống PHẢI suy ra thiên hướng thị trường bằng quy tắc xác định từ tương quan giá/đường trung bình và động lượng, độc lập với AI.
- **FR-020**: Đề xuất lệnh CHỈ được lưu khi: AI trả quyết định vào lệnh (không phải chờ), điểm ≥ ngưỡng tối thiểu, giá SL/Entry/TP đúng phía theo hướng lệnh, và Reward:Risk ≥ ngưỡng của tài khoản.
- **FR-021**: Mỗi lần quét PHẢI ghi bản kiểm toán gồm trạng thái, dữ liệu chỉ báo, prompt, dữ liệu gửi, phản hồi thô và lý do từ chối — kể cả khi không sinh đề xuất.
- **FR-022**: Mỗi đề xuất PHẢI đi qua vòng đánh giá preflight thứ hai; nếu vòng này đề xuất SL/TP tốt hơn thì cập nhật vào đề xuất và tính lại Reward:Risk.
- **FR-023**: Lỗi ở một symbol KHÔNG được chặn việc quét các symbol còn lại.
- **FR-024**: Hệ thống PHẢI cho phép tạo lệnh journal từ một đề xuất, tự tính khối lượng theo % rủi ro tối đa của tài khoản.
- **FR-025**: Tự động tạo lệnh từ đề xuất CHỈ được xảy ra khi cấu hình bật rõ ràng VÀ vòng preflight có AI thật trả lời với quyết định chấp nhận.

#### Giao dịch thật

- **FR-026**: Giao dịch thật PHẢI mặc định TẮT và chỉ bật qua cấu hình tường minh; khi tắt, hệ thống KHÔNG BAO GIỜ gửi yêu cầu tới sàn.
- **FR-027**: Trước khi gửi lệnh, hệ thống PHẢI kiểm tra tuần tự: đã gửi rồi chưa (chống gửi trùng), trạng thái lệnh phải là đang mở, tài khoản có khoá API, dịch vụ AI đã cấu hình, không trùng lệnh nội bộ, không trùng vị thế trên sàn, giá vào hợp lệ, có SL và TP đúng phía, đòn bẩy trong cap, khối lượng hợp lệ theo quy tắc sàn, giá trị danh nghĩa trong khoảng cho phép, chưa vượt giới hạn số lệnh live/ngày, và không có vi phạm mức Nghiêm trọng.
- **FR-028**: Nếu bất kỳ lớp nào chặn, hệ thống PHẢI đánh dấu lệnh là Bị chặn kèm lý do và chuyển trạng thái sang Đã huỷ để nhật ký không giữ vị thế không tồn tại.
- **FR-029**: Cờ "cho phép bỏ qua rủi ro" CHỈ được nới các rào **rủi ro** (cap đòn bẩy, cap giá trị danh nghĩa, giới hạn lệnh/ngày, vi phạm Nghiêm trọng); các rào **kỹ thuật** (mức tối thiểu của sàn, chống trùng vị thế, bắt buộc SL/TP) LUÔN được giữ.
- **FR-030**: Nếu khối lượng bị nâng lên cho đạt mức tối thiểu của sàn, hệ thống PHẢI chấm lại toàn bộ quy tắc với khối lượng mới trước khi gửi.
- **FR-031**: Lỗi khi vào lệnh PHẢI dẫn tới huỷ lệnh trong nhật ký; lỗi khi đặt SL/TP KHÔNG được huỷ vị thế đã tồn tại — thay vào đó đánh dấu chờ xử lý, cảnh báo trader, và tự thử lại định kỳ.
- **FR-032**: Mỗi lệnh gửi lên sàn PHẢI mang một mã định danh do hệ thống sinh để đảm bảo không đặt trùng.
- **FR-033**: Hệ thống PHẢI cho phép đồng bộ lại SL/TP lên sàn khi trader sửa mức giá, và cho phép đóng vị thế trên sàn từ giao diện.

#### Theo dõi & đồng bộ

- **FR-034**: Hệ thống PHẢI định kỳ phân tích các lệnh đang mở: giá hiện tại, lãi/lỗ tạm tính (tiền và %), khoảng cách tới SL/TP theo %, chỉ báo hiện tại, mức rủi ro và lời khuyên hành động.
- **FR-035**: Lời khuyên PHẢI có phiên bản xác định không phụ thuộc AI; AI chỉ làm giàu thêm khi khả dụng.
- **FR-036**: Hệ thống PHẢI định kỳ đối chiếu lệnh đang mở với lịch sử khớp trên sàn để tự đóng lệnh đã đóng ngoài sàn, và không được tự đóng khi vị thế vẫn còn tồn tại trên sàn.
- **FR-037**: Sau khi đồng bộ đóng lệnh, hệ thống PHẢI chạy lại toàn bộ luồng chấm rule + hành vi + tổng hợp ngày.

#### Tin vĩ mô & thông báo

- **FR-038**: Hệ thống PHẢI quét định kỳ các sự kiện vĩ mô tác động cao và cảnh báo khi thời điểm hiện tại nằm trong vùng nguy hiểm quanh sự kiện.
- **FR-039**: Bối cảnh tin vĩ mô PHẢI được đưa vào dữ liệu gửi cho AI khi sinh đề xuất và khi preflight.
- **FR-040**: Hệ thống PHẢI có trung tâm thông báo với loại, mức độ, nguồn, symbol liên quan, đường dẫn liên quan, trạng thái đã đọc và thời điểm hết hạn.
- **FR-041**: Người dùng PHẢI cấu hình được bật/tắt từng kênh (trong ứng dụng / email) theo loại thông báo và ngưỡng mức độ.
- **FR-042**: Hệ thống PHẢI chống trùng thông báo theo nguồn + khoá nguồn.
- **FR-043**: Thông báo mới PHẢI được đẩy tới giao diện theo thời gian thực kèm số lượng chưa đọc.
- **FR-044**: Lỗi ở khâu thông báo KHÔNG được làm thất bại nghiệp vụ đã thực hiện thành công.

#### Bảng điều khiển, cấu hình, truy vết

- **FR-045**: Hệ thống PHẢI hiển thị tổng quan theo tài khoản: số lệnh, tỷ lệ thắng, PnL ròng, chuỗi thua tối đa, tổng % rủi ro và danh sách cờ gần đây.
- **FR-046**: Hệ thống PHẢI tổng hợp số liệu theo ngày cho từng tài khoản (số lệnh, thắng/thua, lãi gộp, lỗ gộp, PnL ròng, chuỗi thua dài nhất, tổng % rủi ro, vốn đầu ngày).
- **FR-047**: Hệ thống PHẢI cho phép cấu hình ngưỡng rủi ro theo từng tài khoản và cấu hình toàn cục (tài khoản mặc định, xác nhận trước khi tạo lệnh, tự tạo lệnh từ đề xuất, điểm tín hiệu tối thiểu, cho phép bỏ qua rủi ro).
- **FR-048**: Hệ thống PHẢI lưu vết mọi lần gọi API sàn và mọi lần gọi AI, và cho phép tra cứu qua giao diện.
- **FR-049**: Hệ thống PHẢI yêu cầu đăng nhập cho mọi chức năng trừ trang đăng nhập, và bảo vệ mọi thao tác thay đổi dữ liệu khỏi tấn công giả mạo yêu cầu.
- **FR-050**: Danh sách dài PHẢI được phân trang.

### Key Entities

- **Tài khoản giao dịch**: gốc của mọi dữ liệu — tên, sàn, đơn vị tiền, vốn ban đầu, số dư hiện tại, trạng thái hoạt động, khoá API. 1:N với lệnh, 1:1 với cấu hình rủi ro.
- **Lệnh giao dịch**: bản ghi trung tâm — symbol, hướng, trạng thái (Kế hoạch/Đang mở/Đã đóng/Đã huỷ), nguồn, loại lệnh, giá vào/ra, SL/TP, khối lượng, đòn bẩy, phí, lãi/lỗ, các chỉ số rủi ro tính sẵn, kết quả, tâm lý trước/sau, thời điểm mở/đóng, và nhóm thuộc tính giao dịch thật (đã gửi sàn chưa, trạng thái live, mã lệnh sàn, ghi chú live).
- **Cấu hình rủi ro**: ngưỡng cho từng tài khoản — % rủi ro tối đa/lệnh, Reward:Risk tối thiểu, số lệnh tối đa/ngày, % lỗ tối đa/ngày, bắt buộc SL, cửa sổ revenge, ngưỡng chuỗi thua, ngưỡng tăng size.
- **Cấu hình toàn cục**: tài khoản mặc định, xác nhận trước khi tạo lệnh, tự tạo lệnh từ đề xuất, điểm tín hiệu tối thiểu, cho phép bỏ qua rủi ro.
- **Cờ**: cảnh báo gắn với lệnh — phân loại (vi phạm quy tắc / hành vi), loại, mức độ, thông điệp, chi tiết, thời điểm phát hiện.
- **Tổng hợp ngày**: số liệu gộp theo tài khoản + ngày, làm đầu vào cho quy tắc số lệnh/ngày và giới hạn lỗ ngày.
- **Đề xuất lệnh**: gợi ý sinh từ quét — symbol, khung thời gian, hướng, thiên hướng, điểm, entry/SL/TP, Reward:Risk, lý do (kèm nhãn quyết định AI). **Không** phải lệnh thật.
- **Phân tích lệnh**: ảnh chụp trạng thái một lệnh đang mở — giá hiện tại, lãi/lỗ tạm tính, khoảng cách SL/TP, chỉ báo, mức rủi ro, lời khuyên, phần làm giàu bởi AI. 1:1 với lệnh.
- **Mục theo dõi**: symbol + khung thời gian + trạng thái bật/tắt.
- **Bản ghi chỉ báo / Ảnh chụp thị trường**: lịch sử và trạng thái mới nhất của chỉ báo theo symbol.
- **Chiến lược**: setup giao dịch gắn tuỳ chọn vào lệnh, để thống kê hiệu quả theo chiến lược.
- **Nhãn lệnh**: nhãn lỗi/điều kiện/setup gắn vào lệnh để review.
- **Thông báo / Lượt gửi / Tuỳ chọn thông báo**: nội dung cảnh báo, trạng thái gửi theo kênh, và cấu hình kênh theo loại + ngưỡng mức độ của người dùng.
- **Bản ghi kiểm toán quét AI**: vết đầy đủ một lần AI ra quyết định tín hiệu.
- **Bản ghi kiểm toán API sàn**: vết mỗi lần chạm API sàn.
- **Người dùng**: tài khoản đăng nhập, email nhận thông báo.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% lệnh được ghi nhận đều có kết quả chấm quy tắc và phát hiện hành vi trong cùng thao tác lưu — không có lệnh nào "chưa được chấm".
- **SC-002**: 100% vi phạm ở mức Cảnh báo trở lên sinh thông báo tới trader trong vòng 1 phút kể từ khi lệnh được lưu.
- **SC-003**: Không có lệnh thật nào chạm sàn khi công tắc giao dịch thật đang tắt (0 ngoại lệ).
- **SC-004**: Không có lệnh thật nào chạm sàn khi thiếu Stop Loss hoặc Take Profit hợp lệ (0 ngoại lệ).
- **SC-005**: Không phát sinh vị thế trùng (cùng symbol + cùng hướng) do hệ thống tự tạo (0 ngoại lệ).
- **SC-006**: Mỗi lần AI ra quyết định tín hiệu đều truy vết được đầy đủ đầu vào và đầu ra — tỷ lệ bản ghi kiểm toán / lần quét = 100%.
- **SC-007**: Chạm lỗi ở một symbol khi quét không làm giảm số symbol quét thành công còn lại quá 1 symbol.
- **SC-008**: Trader ghi nhận một lệnh hoàn chỉnh (từ mở biểu mẫu tới lưu) trong dưới 60 giây.
- **SC-009**: Nhật ký khớp với thực tế trên sàn: sau mỗi chu kỳ đồng bộ, không tồn tại lệnh ở trạng thái Đang mở mà vị thế tương ứng đã đóng trên sàn quá 1 chu kỳ.
- **SC-010**: Lệnh vào sàn nhưng chưa đặt được SL/TP luôn được cảnh báo tới trader ngay và được thử lại tự động cho tới khi thành công hoặc trader can thiệp — 0 trường hợp im lặng.
- **SC-011**: Chấm lại cùng một lệnh nhiều lần cho ra cùng một tập cờ (không tích luỹ trùng lặp).
- **SC-012**: Lỗi ở tầng thông báo hoặc AI không gây thất bại cho bất kỳ thao tác nghiệp vụ nào đã hoàn tất.

---

## Assumptions

- Hệ thống phục vụ **một trader cá nhân tự vận hành** (single-tenant); phân quyền nhiều người dùng nằm ngoài phạm vi baseline này.
- Thị trường mục tiêu là **crypto futures USDT-M**, sàn chính là Binance; các sàn khác đã có chỗ trong mô hình dữ liệu nhưng chưa triển khai.
- Toàn bộ mốc thời gian nghiệp vụ lưu theo UTC; hiển thị quy đổi sang giờ Việt Nam.
- Số dư dùng để tính % rủi ro ưu tiên số dư thật đọc từ sàn, fallback về số dư ghi trong hệ thống.
- Dịch vụ AI là **tuỳ chọn** cho luồng nhật ký và phân tích, nhưng **bắt buộc** cho luồng đặt lệnh thật tự động.
- Nguồn dữ liệu tin vĩ mô là tuỳ chọn cấu hình; khi không cấu hình, hệ thống vẫn chạy đầy đủ, chỉ mất lớp cảnh báo tin.
- Khoá API sàn được cấp quyền tối thiểu cần thiết; việc lưu khoá an toàn (User Secrets / mã hoá) là yêu cầu vận hành **chưa hoàn tất** — xem Phụ lục B.
- Giao dịch thật mặc định chạy ở chế độ testnet cho tới khi trader chủ động chuyển sang tiền thật.
- Ngôn ngữ giao diện và mọi thông điệp hướng tới người dùng là tiếng Việt.

---

## Phụ lục A — Hiện trạng triển khai (tham chiếu, không phải yêu cầu)

> Phần này ghi lại **cách hệ thống đang được xây dựng** để người đọc spec định vị được mã nguồn. Các con số ở đây là giá trị mặc định hiện tại, không phải ràng buộc nghiệp vụ bất biến.

### A.1 Kiến trúc

Clean layered, 5 project: `MMW.Web` (ASP.NET Core MVC + Razor + SignalR) → `MMW.Application` (nghiệp vụ: services, rule engine, behavior, indicators, market data ports) → `MMW.Infrastructure` (EF Core, repositories, adapter Binance, adapter LLM, email, macro events) → `MMW.Domain` (entities, enums, DbContext) + `MMW.Shared` (interfaces dùng chung, Result, PaginatedResult).

Mẫu thiết kế: Repository + UnitOfWork, Port/Adapter cho sàn và LLM, rule/detector dạng plug-in (thêm lớp mới là engine tự nhặt), DI scoped.

### A.2 Tiến trình nền (Hangfire)

| Job | Chu kỳ | Nhiệm vụ |
|---|---|---|
| `market-scan` | mỗi 5 phút (+ chạy ngay khi khởi động) | Quét watchlist → chỉ báo → AI signal → preflight → lưu đề xuất → (tuỳ chọn) tự tạo lệnh + gửi sàn |
| `trade-result-sync` | mỗi 2 phút | Đối chiếu fills trên sàn để đóng lệnh |
| `trade-advisor` | mỗi 1 phút | Phân tích lệnh đang mở + lời khuyên |
| `macro-event-scan` | mỗi 15 phút (+ chạy ngay khi khởi động) | Quét tin vĩ mô, cảnh báo trước vùng tin |
| `sltp-retry` | mỗi 2 phút | Đặt lại SL/TP cho lệnh ở trạng thái chờ |

### A.3 Ngưỡng mặc định

**Cấu hình rủi ro theo tài khoản**: rủi ro tối đa 1%/lệnh · Reward:Risk tối thiểu 1.5 · tối đa 5 lệnh/ngày · lỗ tối đa 3%/ngày · bắt buộc Stop Loss · cửa sổ revenge 30 phút · ngưỡng chuỗi thua 3 · ngưỡng tăng size 50% (so với trung bình 10 lệnh đã đóng gần nhất).

**Cấu hình giao dịch thật**: `Enabled=false` (kill-switch) · `UseTestnet=true` · cap đòn bẩy 20x · đòn bẩy mặc định 20x · notional tối thiểu 20 USDT · notional tối đa 50 USDT/lệnh · tối đa 10 lệnh live/ngày.

**Cấu hình toàn cục**: xác nhận trước khi tạo lệnh = bật · tự tạo lệnh từ đề xuất = tắt · điểm tín hiệu tối thiểu = 2 · cho phép bỏ qua rủi ro = tắt.

**Vùng tránh tin vĩ mô**: 45 phút trước → 30 phút sau sự kiện tác động cao; nhìn trước 24 giờ, nhìn lại 12 giờ.

### A.4 Bộ quy tắc và bộ phát hiện hiện có

Quy tắc (mã cờ 1xx): `RequireStopLoss` (Nghiêm trọng) · `MaxRiskPerTrade` (Cảnh báo, ≥2× ngưỡng → Nghiêm trọng) · `MinRiskReward` (Cảnh báo) · `MaxTradesPerDay` (Cảnh báo) · `DailyLossLimit` (Nghiêm trọng).

Bộ phát hiện hành vi (mã cờ 2xx): `RevengeTrade` · `LossStreak` · `OversizedAfterLoss`.

### A.5 Chỉ báo và thiên hướng

Chỉ báo: SMA, EMA(20/50), RSI(14), MACD, ATR(14). Thiên hướng deterministic: +1 nếu giá > EMA20 > EMA50, −1 nếu ngược lại; +1 nếu MACD histogram dương, −1 nếu âm; RSI >70 / <30 chỉ ghi chú. Tổng > 0 → Tăng, < 0 → Giảm, = 0 → Trung tính.

Lưu ý: đề xuất hiện được sinh bởi **AI** (`MarketScanService`), còn `SignalGenerator` thuần quy tắc (SL = 1.5×ATR, RR = 2) vẫn tồn tại và có test nhưng không nằm trên luồng quét chính.

### A.6 Hạ tầng ngoài

Sàn: Binance (market data công khai, account read-only, futures order có ký HMAC). LLM: `ILlmService` với các adapter MiniMax / DeepSeek / Gemini. Email: SMTP. Tin vĩ mô: provider theo cấu hình, mặc định là bản rỗng (noop).

Nền tảng: .NET 8 · EF Core code-first (14 migration) · SQL Server (kèm Hangfire storage) · xác thực cookie + bcrypt · Serilog (console + file xoay vòng) · SignalR cho thông báo realtime · Tabler + CSS glassmorphism · xUnit + InMemory DB cho test.

---

## Phụ lục B — Khoảng trống đã biết (đầu vào cho `/speckit-plan`)

| # | Khoảng trống | Rủi ro | Ưu tiên |
|---|---|---|---|
| 1 | Khoá API sàn lưu dạng chuỗi thường trong DB, chưa mã hoá / chưa dùng User Secrets | Lộ khoá = mất tiền thật | 🔴 Cao |
| 2 | ~~Hiến chương dự án chưa điền~~ — **đã xong 2026-07-28**: `.specify/memory/constitution.md` v1.0.0 với 7 nguyên tắc + 7 cổng chất lượng | — | ✅ Đã xử lý |
| 3 | Vốn đầu ngày trong tổng hợp ngày đang **ước lượng** (số dư hiện tại − PnL trong ngày) | Quy tắc giới hạn lỗ ngày có thể lệch | 🟡 Trung bình |
| 4 | Chưa có trang chuyên biệt để xem/review toàn bộ cờ vi phạm và hành vi theo thời gian | Mất một phần giá trị "học từ lỗi" | 🟡 Trung bình |
| 5 | Chưa có unit test cho luồng preflight, advisor AI và các lớp chặn của live order | Vùng rủi ro cao nhất lại phủ test thấp nhất | 🟡 Trung bình |
| 6 | Nhập lệnh từ sàn chưa ghép fill theo FIFO | Lệnh nhập có thể sai khối lượng/giá trung bình | 🟡 Trung bình |
| 7 | Tài liệu `SYSTEM_OVERVIEW.md` đã lạc hậu so với mã nguồn (thiếu notification, live order, macro event, audit) | Nhầm lẫn khi onboard | 🟢 Thấp |
| 8 | `SignalGenerator` thuần quy tắc không còn nằm trên luồng chính nhưng vẫn được duy trì | Mã chết hoặc thiếu đường dự phòng khi AI hỏng — cần quyết định rõ | 🟢 Thấp |
