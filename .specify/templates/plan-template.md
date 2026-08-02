# Implementation Plan: [FEATURE]

**Branch**: `[###-feature-name]` | **Date**: [DATE] | **Spec**: [link]

**Input**: Feature specification from `/specs/[###-feature-name]/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

[Extract from feature spec: primary requirement + technical approach from research]

## Technical Context

<!--
  ACTION REQUIRED: Replace the content in this section with the technical details
  for the project. The structure here is presented in advisory capacity to guide
  the iteration process.
-->

**Language/Version**: [e.g., Python 3.11, Swift 5.9, Rust 1.75 or NEEDS CLARIFICATION]

**Primary Dependencies**: [e.g., FastAPI, UIKit, LLVM or NEEDS CLARIFICATION]

**Storage**: [if applicable, e.g., PostgreSQL, CoreData, files or N/A]

**Testing**: [e.g., pytest, XCTest, cargo test or NEEDS CLARIFICATION]

**Target Platform**: [e.g., Linux server, iOS 15+, WASM or NEEDS CLARIFICATION]

**Project Type**: [e.g., library/cli/web-service/mobile-app/compiler/desktop-app or NEEDS CLARIFICATION]

**Performance Goals**: [domain-specific, e.g., 1000 req/s, 10k lines/sec, 60 fps or NEEDS CLARIFICATION]

**Constraints**: [domain-specific, e.g., <200ms p95, <100MB memory, offline-capable or NEEDS CLARIFICATION]

**Scale/Scope**: [domain-specific, e.g., 10k users, 1M LOC, 50 screens or NEEDS CLARIFICATION]

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*Nguồn: `.specify/memory/constitution.md` v1.0.0. Trả lời từng cổng bằng PASS / N/A / VIOLATION.
Mọi VIOLATION phải được ghi trong "Complexity Tracking" kèm lý do.*

| # | Cổng | Câu hỏi kiểm tra | Kết quả |
|---|------|------------------|---------|
| 1 | I. Kỷ luật hơn dự đoán | Thay đổi này giúp trader giữ kỷ luật ra sao? Có ngưỡng nào bị hardcode thay vì đọc từ cấu hình tài khoản không? | |
| 2 | II. Deterministic trước, AI sau | Có con số quyết định nào phụ thuộc AI không? Mọi luồng gọi AI đã có nhánh dự phòng và kiểm chứng đầu ra bằng luật cứng chưa? | |
| 3 | III. An toàn mặc định (KHÔNG THƯƠNG LƯỢNG) | Có chạm tới đường dẫn đặt lệnh thật không? Số lớp chặn có giảm không? Nhật ký còn khớp 1-1 với sàn không? | |
| 4 | IV. Ghi vết toàn bộ | Quyết định mới có được ghi kiểm toán không? Có bí mật nào lọt vào log/audit không? | |
| 5 | V. Kiến trúc phân tầng | Chiều phụ thuộc có bị vi phạm không? Quy tắc/detector mới có thêm được mà không sửa engine không? | |
| 6 | VI. Test tương xứng rủi ro | Vùng bắt buộc test có test đỏ trước không? Mỗi lớp chặn mới có test chứng minh nó chặn thật không? | |
| 7 | VII. Bí mật an toàn | Có bí mật nào vào mã, migration, cấu hình đã commit, hay log không? | |

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)
<!--
  ACTION REQUIRED: Replace the placeholder tree below with the concrete layout
  for this feature. Delete unused options and expand the chosen structure with
  real paths (e.g., apps/admin, packages/something). The delivered plan must
  not include Option labels.
-->

```text
# [REMOVE IF UNUSED] Option 1: Single project (DEFAULT)
src/
├── models/
├── services/
├── cli/
└── lib/

tests/
├── contract/
├── integration/
└── unit/

# [REMOVE IF UNUSED] Option 2: Web application (when "frontend" + "backend" detected)
backend/
├── src/
│   ├── models/
│   ├── services/
│   └── api/
└── tests/

frontend/
├── src/
│   ├── components/
│   ├── pages/
│   └── services/
└── tests/

# [REMOVE IF UNUSED] Option 3: Mobile + API (when "iOS/Android" detected)
api/
└── [same as backend above]

ios/ or android/
└── [platform-specific structure: feature modules, UI flows, platform tests]
```

**Structure Decision**: [Document the selected structure and reference the real
directories captured above]

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |
