# Kiến trúc và yêu cầu phi chức năng MindSprintAI

Bộ tài liệu phục vụ yêu cầu kiến trúc [#40](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/40) và kiểm thử Sprint 4, cập nhật **14/10/2026** theo mã nguồn `main` tại [`b890dcc`](https://github.com/tungtungsahul/MindSprint-AI-D-n/tree/b890dccbd502f5158a5a9fe46188b091e5211857).

| Tài liệu | Nội dung |
| --- | --- |
| [ERD](erd.md) | 12 bảng, 91 cột; khóa ngoại, quan hệ nghiệp vụ, ảnh PNG/SVG và Mermaid |
| [Kiến trúc phần mềm](software-architecture.md) | Bối cảnh, thành phần, dữ liệu, luồng xử lý, triển khai và rủi ro |
| [Yêu cầu phi chức năng](non-functional-requirements.md) | 14 yêu cầu NFR-01…NFR-14, ngưỡng và cách đo |
| [Kế hoạch kiểm thử Sprint 4](sprint-4-test-plan.md) | Issue tương ứng, thứ tự thực hiện và điều kiện nghiệm thu |
| [ADR-001](../decisions/0001-layered-monolith.md) | Backend theo tầng trong một ứng dụng |
| [ADR-002](../decisions/0002-postgresql-and-deployment.md) | PostgreSQL và triển khai frontend/API riêng |
| [ADR-003](../decisions/0003-identity-and-account-isolation.md) | JWT, refresh token và ranh giới tài khoản |
| [ADR-004](../decisions/0004-ai-and-source-boundaries.md) | Cô lập Gemini và nguồn tài liệu ngoài |

## Mốc kiến trúc và trạng thái

Mốc đối chiếu của đợt cập nhật này là [main tại commit `b890dcc`](https://github.com/tungtungsahul/MindSprint-AI-D-n/tree/b890dccbd502f5158a5a9fe46188b091e5211857): **HTML/CSS/JS + ASP.NET Core 8 + PostgreSQL + EF Core + JWT + Gemini**. Bản FastAPI/SQLite vẫn là ứng dụng độc lập, không dùng chung database/token với API ASP.NET Core.

## Đối chiếu main

| Nội dung | Tài liệu tại mốc 9ba7acd | Code/migrations tại b890dcc | Cập nhật |
| --- | --- | --- | --- |
| Schema PostgreSQL | 10 bảng, 69 cột, 5 FK cascade | 12 bảng, 91 cột, 7 FK cascade; thêm `NotebookGenerationCaches`, `WeakAreas` và hai cột file nullable cho `NotebookSources` | ERD Mermaid/Markdown; PNG/SVG đồng bộ |
| Sinh nội dung Notebook | Chưa mô tả bảng cache | Có cache bền vững theo notebook/type/hash nguồn và request | Ghi nhận cơ chế, không khẳng định hiệu năng đã đạt |
| Phân tích điểm yếu | Chưa có entity/service trong ERD | `WeakArea`, unique theo UserId/Category/Topic, service đăng ký trong DI | Ghi nhận model và ranh giới dữ liệu; chất lượng AI vẫn cần kiểm thử |
| NFR/Sprint 4 | Tiêu chí đề xuất, chưa nghiệm thu | Code có thêm cơ chế kể trên; chưa có kết quả runtime chứng minh các NFR | Giữ trạng thái đề xuất/chưa kiểm chứng |

Các số liệu schema được đếm từ EF entities và đối chiếu migrations trong repository ở commit đã ghi; không xác nhận database production đã migrate. Thuật ngữ và phạm vi cần được rà cùng tài liệu UML [#20](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/20) và yêu cầu [#58](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/58).

“Hiện có” nghĩa là đã quan sát trong mã/cấu hình; không có nghĩa đã xác minh môi trường production. “Đề xuất” là mục tiêu chất lượng cần chốt tại sprint planning. Các ADR ghi nhận kiến trúc hiện tại và phân tích trade-off của tài liệu này; không giả định lịch sử phê duyệt của nhóm.

Chưa thực thi các bài tải, bảo mật, khôi phục hay chấm chất lượng AI của Sprint 4. Các issues được mở để thực hiện các bài đó. Cấu hình cloud chưa được xác minh đang chạy thực tế.

## Cập nhật

Khi thay đổi provider, hợp đồng API, phiên đăng nhập, giới hạn AI hoặc triển khai: cập nhật tài liệu này, ADR liên quan và issue kiểm thử có ID NFR; ghi lại commit và bằng chứng. Quyết định thay thế tạo ADR mới liên kết ADR cũ. Bản docs này là nơi tra cứu chính, có thể liên kết từ wiki khi wiki được khởi tạo.
