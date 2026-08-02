<!--
SYNC IMPACT REPORT
==================
Version change: (chưa có) → 1.0.0
Bump rationale: Lần phê chuẩn đầu tiên. Toàn bộ placeholder của bản mẫu được thay
bằng nội dung cụ thể, 7 nguyên tắc cốt lõi được xác lập.

Modified principles: không có (bản đầu tiên)

Added sections:
  - Core Principles I–VII
  - Ràng Buộc Kỹ Thuật & Bảo Mật (thay [SECTION_2_NAME])
  - Quy Trình Phát Triển & Cổng Chất Lượng (thay [SECTION_3_NAME])
  - Governance

Removed sections: không có

Templates requiring updates:
  ✅ .specify/templates/plan-template.md — mục "Constitution Check" đã thay
     "[Gates determined based on constitution file]" bằng 7 gate cụ thể
  ✅ .specify/templates/tasks-template.md — sửa quy ước "Tests are OPTIONAL" thành
     bắt buộc cho vùng rủi ro tiền thật theo Nguyên tắc VI
  ✅ .specify/templates/spec-template.md — không cần đổi; cấu trúc đã tương thích
  ✅ .claude/skills/speckit-*/SKILL.md — đã rà, không còn tham chiếu agent lỗi thời
  ✅ specs/001-mmw-system-baseline/spec.md — 4 nguyên tắc nền trong spec là tập con
     của Nguyên tắc I–IV, không mâu thuẫn
  ⚠ SYSTEM_OVERVIEW.md — đã lạc hậu so với mã nguồn (thiếu notification, live order,
     macro event, audit). Không thuộc phạm vi lệnh này; xem Phụ lục B của baseline spec.

Deferred TODOs: không có
-->

# MMW Constitution

MMW (My Market Wisdom) là trợ lý kỷ luật giao dịch crypto futures cho một trader cá nhân,
có khả năng chạm tới **tiền thật**. Hiến chương này là luật tối cao của dự án: mọi spec,
plan, task và pull request đều phải tuân thủ.

## Core Principles

### I. Kỷ Luật Hơn Dự Đoán

Sản phẩm KHÔNG hứa hẹn dự đoán đúng thị trường. Sản phẩm hứa hẹn **chặn lệnh sai kỷ luật**.
Mọi tính năng mới PHẢI trả lời được câu hỏi: "Nó giúp trader giữ kỷ luật như thế nào?"

- Tính năng chỉ nhằm tăng tần suất vào lệnh, tăng đòn bẩy, hay hứa hẹn tỷ lệ thắng
  KHÔNG ĐƯỢC đưa vào sản phẩm.
- Mọi cảnh báo hướng tới người dùng PHẢI nêu **số liệu thực tế so với ngưỡng đã cấu hình**,
  không được nói chung chung ("rủi ro cao").
- Ngưỡng kỷ luật PHẢI đọc từ cấu hình theo tài khoản. Hardcode ngưỡng trong logic phát hiện
  là vi phạm hiến chương.

**Lý do**: Vấn đề của trader cá nhân là hành vi, không phải thiếu tín hiệu. Một sản phẩm
tối ưu cho "nhiều tín hiệu hơn" sẽ phá hỏng chính giá trị mà nó tồn tại để bảo vệ.

### II. Deterministic Trước, AI Sau

Mọi con số dùng để ra quyết định PHẢI tính được bằng công thức xác định, không phụ thuộc AI.
AI là lớp **làm giàu và lọc thêm**, không bao giờ là lớp duy nhất quyết định.

- Chỉ số rủi ro (số tiền rủi ro, % rủi ro, Reward:Risk, bội số R), thiên hướng thị trường,
  và kết quả chấm quy tắc PHẢI deterministic 100%.
- Mọi luồng có gọi AI PHẢI có nhánh dự phòng deterministic khi AI không cấu hình,
  hết hạn ngạch, trả sai định dạng, hoặc timeout.
- Đầu ra của AI PHẢI được **kiểm chứng bằng luật cứng** trước khi dùng: đúng phía giá theo
  hướng lệnh, đạt Reward:Risk tối thiểu, điểm và độ tin cậy nằm trong dải hợp lệ.
  Không bao giờ tin đầu ra AI vô điều kiện.
- Ngoại lệ có chủ đích: đặt lệnh THẬT tự động YÊU CẦU AI trả lời thật (xem Nguyên tắc III).
  Đây là ràng buộc theo hướng an toàn — thiếu AI thì **không đặt lệnh**, chứ không phải
  thiếu AI thì đặt lệnh mù.

**Lý do**: Mô hình ngôn ngữ không tất định và có thể hỏng bất cứ lúc nào. Kỷ luật thì không
được phép hỏng.

### III. An Toàn Mặc Định Cho Tiền Thật (KHÔNG THƯƠNG LƯỢNG)

Mọi đường dẫn tới tiền thật PHẢI mặc định TẮT và chỉ mở bằng cấu hình tường minh.

- Công tắc tổng giao dịch thật mặc định `false`; khi tắt, hệ thống KHÔNG ĐƯỢC phát bất kỳ
  yêu cầu đặt lệnh nào tới sàn.
- Chế độ testnet là mặc định. Chuyển sang tiền thật PHẢI là hành động cấu hình có chủ ý.
- Chuỗi kiểm tra trước khi gửi lệnh PHẢI được giữ nguyên vẹn và theo đúng thứ tự: chống gửi
  trùng → trạng thái lệnh → khoá API → AI đã cấu hình → chống trùng nội bộ → chống trùng vị
  thế trên sàn → giá vào hợp lệ → bắt buộc SL và TP đúng phía → cap đòn bẩy → khối lượng hợp
  lệ theo sàn → khoảng giá trị danh nghĩa → giới hạn lệnh live/ngày → không có vi phạm mức
  Nghiêm trọng. **Thêm lớp chặn được; bớt lớp chặn PHẢI qua sửa đổi hiến chương.**
- Cờ "cho phép bỏ qua rủi ro" CHỈ nới các rào **rủi ro** (cap đòn bẩy, cap notional, giới hạn
  lệnh/ngày, vi phạm Nghiêm trọng). Các rào **kỹ thuật** (mức tối thiểu của sàn, chống trùng
  vị thế, bắt buộc SL/TP) LUÔN được giữ, không có ngoại lệ.
- Nhật ký PHẢI khớp 1-1 với thực tế trên sàn: lệnh bị chặn hoặc lỗi khi vào sàn PHẢI chuyển
  sang Đã huỷ, không để lại vị thế "ma".
- Lỗi khi đặt SL/TP KHÔNG ĐƯỢC huỷ vị thế đã tồn tại thật. Thay vào đó: đánh dấu chờ xử lý,
  cảnh báo trader ngay, và tự thử lại định kỳ cho tới khi xong. **Không được im lặng.**
- Nếu khối lượng bị nâng lên cho đạt mức tối thiểu của sàn, PHẢI chấm lại toàn bộ quy tắc với
  khối lượng mới trước khi gửi, vì rủi ro thực tế đã thay đổi.

**Lý do**: Đây là nơi duy nhất trong hệ thống mà một lỗi phần mềm trực tiếp làm mất tiền.
Chi phí của một lớp chặn thừa là gần bằng không; chi phí của một lớp chặn thiếu là không giới hạn.

### IV. Ghi Vết Toàn Bộ

Mọi quyết định của hệ thống PHẢI truy ngược lại được sau nhiều tháng.

- Mỗi lần AI ra quyết định tín hiệu PHẢI lưu bản kiểm toán gồm: dữ liệu chỉ báo tại thời điểm
  quét, prompt hệ thống, dữ liệu gửi đi, phản hồi thô, trạng thái, và lý do từ chối —
  **kể cả khi không sinh đề xuất nào**.
- Mỗi lần chạm API sàn PHẢI được ghi vết.
- Cảnh báo hướng tới người dùng PHẢI lưu kèm số liệu chi tiết dạng cấu trúc, không chỉ câu chữ.
- Log ứng dụng PHẢI có cấu trúc và mang định danh nghiệp vụ (mã lệnh, symbol) để lần theo
  được một luồng xử lý xuyên nhiều job.
- Bí mật (khoá API, token AI, mật khẩu) KHÔNG ĐƯỢC xuất hiện trong log, bản kiểm toán,
  hay thông báo lỗi.

**Lý do**: Không truy vết được thì không học được từ lỗi — mà "học từ lỗi" chính là sản phẩm.

### V. Kiến Trúc Phân Tầng, Quy Tắc Dạng Plug-in

Chiều phụ thuộc là một chiều: `Web → Application → Infrastructure → Domain`, với `Shared`
chứa hợp đồng dùng chung.

- Tầng `Domain` KHÔNG ĐƯỢC phụ thuộc vào bất kỳ tầng nào khác.
- Tầng `Application` KHÔNG ĐƯỢC tham chiếu trực tiếp SDK của sàn, nhà cung cấp AI hay dịch vụ
  email. Mọi hệ thống ngoài PHẢI đi qua cổng (port) khai báo trong `Application` và được cài
  đặt (adapter) trong `Infrastructure`.
- Controller KHÔNG ĐƯỢC chứa logic nghiệp vụ. Chúng nhận đầu vào, gọi service, trả view.
- Thêm một quy tắc kỷ luật hoặc một bộ phát hiện hành vi mới PHẢI chỉ là thêm một lớp mới cài
  đặt hợp đồng tương ứng — KHÔNG ĐƯỢC sửa vòng lặp của engine. Nếu phải sửa engine để thêm
  quy tắc, thiết kế đó sai.
- Truy cập dữ liệu đi qua repository + đơn vị công việc (unit of work). Một thao tác nghiệp vụ
  = một lần commit nhất quán.
- Chấm lại một lệnh PHẢI bất biến (idempotent): cờ cũ bị thay thế, không tích luỹ trùng.

**Lý do**: Bộ quy tắc kỷ luật sẽ còn mở rộng dài hạn. Chi phí thêm quy tắc phải tiệm cận không,
nếu không trader sẽ ngừng bổ sung và hệ thống ngừng phản ánh kỷ luật thật của họ.

### VI. Test Tương Xứng Rủi Ro

Mức độ phủ test PHẢI tỷ lệ thuận với hậu quả tài chính khi mã sai.

- **Bắt buộc test trước khi merge** (viết test đỏ trước, rồi mới viết mã): công thức tính chỉ
  số rủi ro, mọi quy tắc kỷ luật, mọi bộ phát hiện hành vi, mọi lớp chặn của luồng đặt lệnh
  thật, và mọi logic đóng/đồng bộ lệnh làm thay đổi số dư.
- Mỗi lớp chặn ở Nguyên tắc III PHẢI có ít nhất một test chứng minh nó **thực sự chặn**.
- Mỗi quy tắc và bộ phát hiện PHẢI có test cho cả trường hợp kích hoạt và trường hợp
  ngay dưới ngưỡng (không kích hoạt).
- Bộ phân tích cú pháp phản hồi AI PHẢI có test với đầu vào lỗi định dạng: kèm markdown,
  kèm văn bản thừa, JSON lồng, thiếu trường, số ngoài dải.
- Sửa lỗi PHẢI kèm một test tái hiện lỗi đó và thất bại trước khi vá.
- Giao diện, view và văn bản hiển thị KHÔNG bắt buộc test tự động.
- Toàn bộ bộ test PHẢI xanh trước khi merge. Không có test bị bỏ qua (skip) mà không kèm
  ghi chú lý do và mốc xử lý.

**Lý do**: Test là bảo hiểm, và phí bảo hiểm nên tương ứng với giá trị tài sản được bảo vệ.

### VII. Bí Mật Không Nằm Trong Mã Hay Dữ Liệu Thường

Khoá API sàn, khoá dịch vụ AI, thông tin SMTP và mật khẩu là tài sản có giá trị bằng tiền thật.

- Bí mật KHÔNG ĐƯỢC commit vào kho mã, KHÔNG ĐƯỢC lưu dạng chuỗi thường trong cơ sở dữ liệu,
  và KHÔNG ĐƯỢC ghi ra log.
- Môi trường phát triển dùng User Secrets; môi trường chạy thật dùng kho bí mật của nền tảng
  hoặc biến môi trường. Giá trị bí mật lưu trong cơ sở dữ liệu PHẢI được mã hoá khi lưu.
- Khoá API sàn PHẢI được cấp quyền tối thiểu cần cho chức năng đang dùng. Quyền rút tiền
  KHÔNG BAO GIỜ được cấp.
- Mật khẩu người dùng lưu dưới dạng băm có muối, không thể đảo ngược.
- Mọi endpoint mặc định yêu cầu đăng nhập; mọi thao tác thay đổi dữ liệu PHẢI có bảo vệ chống
  giả mạo yêu cầu.

**Lý do**: Hệ thống đã có khả năng đặt lệnh bằng tiền thật. Rò rỉ khoá không phải sự cố dữ liệu
— nó là sự cố mất tiền.

## Ràng Buộc Kỹ Thuật & Bảo Mật

**Nền tảng cố định**: .NET 8 · ASP.NET Core MVC + Razor · EF Core code-first · SQL Server ·
Hangfire (tiến trình nền, dùng chung kho dữ liệu) · SignalR (thông báo thời gian thực) ·
Serilog (log có cấu trúc) · xUnit + InMemory DB (test). Đổi bất kỳ mục nào ở đây là thay đổi
MAJOR của hiến chương.

**Dữ liệu**:

- Mọi thay đổi lược đồ đi qua migration EF Core. KHÔNG ĐƯỢC sửa lược đồ bằng SQL thủ công.
- Giá và khối lượng dùng kiểu thập phân chính xác cao (18,8). KHÔNG ĐƯỢC dùng số thực dấu
  phẩy động cho tiền.
- Mọi mốc thời gian nghiệp vụ lưu theo UTC; quy đổi sang giờ Việt Nam chỉ ở lớp hiển thị.

**Khả năng chịu lỗi**:

- Lỗi ở một symbol trong một lượt quét KHÔNG ĐƯỢC chặn các symbol còn lại.
- Lỗi ở tầng thông báo KHÔNG ĐƯỢC làm thất bại nghiệp vụ đã hoàn tất thành công.
- Mọi lệnh gửi lên sàn PHẢI mang định danh do hệ thống sinh để bảo đảm không đặt trùng khi
  job chạy chồng lấn hoặc thử lại.
- Chống trùng lệnh dựa trên symbol + hướng + giá xấp xỉ + trạng thái đang mở, kiểm tra ở
  **cả** dữ liệu nội bộ **và** vị thế thật trên sàn. Đọc được sàn thì dùng cả hai; không đọc
  được thì vẫn phải giữ lớp nội bộ.

**Ngôn ngữ**: Mọi văn bản hướng tới người dùng — nhãn giao diện, thông điệp cảnh báo, nội dung
thông báo, lời khuyên từ AI — PHẢI bằng tiếng Việt. Mã nguồn, tên biến và commit dùng tiếng Anh;
chú thích giải thích nghiệp vụ dùng tiếng Việt.

## Quy Trình Phát Triển & Cổng Chất Lượng

**Quy trình**: Spec-driven và tuần tự. `/speckit-specify` → `/speckit-clarify` (khi có điểm mơ hồ)
→ `/speckit-plan` → `/speckit-tasks` → `/speckit-implement`. Không viết mã cho một feature trước
khi spec của nó tồn tại.

**Cổng chất lượng — mọi thay đổi PHẢI vượt qua trước khi merge**:

1. **Cổng hiến chương**: Plan nêu rõ thay đổi này chạm tới nguyên tắc nào và tuân thủ ra sao.
   Mọi vi phạm PHẢI được ghi trong bảng "Complexity Tracking" kèm lý do và lý do bác bỏ
   phương án đơn giản hơn.
2. **Cổng build**: Build không lỗi, không cảnh báo mới.
3. **Cổng test**: Toàn bộ test xanh. Vùng thuộc Nguyên tắc VI PHẢI có test mới đi kèm.
4. **Cổng an toàn tiền thật**: Nếu thay đổi chạm tới đường dẫn đặt lệnh, tính khối lượng,
   hoặc bất kỳ lớp chặn nào — PHẢI chứng minh bằng test rằng số lớp chặn không giảm.
5. **Cổng bí mật**: Không có bí mật nào lọt vào mã, migration, cấu hình đã commit, hoặc log.
6. **Cổng migration**: Thay đổi lược đồ có migration đi kèm và áp dụng được trên cơ sở dữ liệu
   sạch.
7. **Cổng đồng bộ spec**: Hành vi thay đổi thì spec tương ứng được cập nhật trong cùng thay đổi
   đó. Spec lạc hậu là nợ kỹ thuật, không phải chuyện nhỏ.

**Thay đổi ngưỡng mặc định** (% rủi ro, số lệnh/ngày, cap notional, cap đòn bẩy...) là thay đổi
**cấu hình**, không phải thay đổi hiến chương — miễn là cơ chế cưỡng chế vẫn nguyên vẹn.

## Governance

Hiến chương này đứng trên mọi quy ước, thói quen và tài liệu khác của dự án. Khi có xung đột,
hiến chương thắng.

**Sửa đổi**: Mọi sửa đổi PHẢI (a) ghi rõ nguyên tắc nào thay đổi và vì sao, (b) tăng phiên bản
theo quy tắc dưới đây, (c) cập nhật các artifact phụ thuộc trong cùng lần thay đổi
(`.specify/templates/*.md`, các skill `speckit-*`, và baseline spec nếu bị ảnh hưởng), và
(d) ghi Sync Impact Report ở đầu tệp này.

**Phiên bản** (semantic versioning):

- **MAJOR** — gỡ bỏ hoặc định nghĩa lại một nguyên tắc theo hướng không tương thích ngược;
  gỡ bỏ một lớp chặn an toàn; đổi nền tảng công nghệ cố định.
- **MINOR** — thêm nguyên tắc hoặc mục mới; mở rộng đáng kể hướng dẫn hiện có.
- **PATCH** — làm rõ câu chữ, sửa lỗi chính tả, tinh chỉnh không đổi ngữ nghĩa.

**Tuân thủ**: Mỗi plan PHẢI đi qua mục "Constitution Check" trước Phase 0 và kiểm tra lại sau
Phase 1. Mỗi lần review mã PHẢI xác nhận 7 cổng chất lượng ở trên. Độ phức tạp thêm vào PHẢI
được biện minh — mặc định là bác bỏ.

**Nguyên tắc bất khả xâm phạm**: Nguyên tắc III (An Toàn Mặc Định Cho Tiền Thật) chỉ được nới
lỏng bằng một sửa đổi MAJOR có chủ đích, ghi rõ rủi ro tài chính chấp nhận đánh đổi. Không có
ngoại lệ nào được cấp ở cấp pull request.

**Hướng dẫn vận hành**: Xem `specs/001-mmw-system-baseline/spec.md` để nắm hành vi hệ thống
hiện tại và danh sách khoảng trống đã biết.

**Version**: 1.0.0 | **Ratified**: 2026-07-28 | **Last Amended**: 2026-07-28
