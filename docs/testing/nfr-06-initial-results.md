# NFR-06 — kết quả kiểm thử và retest

**Ngày chạy:** 2026-11-06 (Asia/Saigon)
**Issue:** [#47 — NFR-06: Khả năng phục hồi khi AI và mạng lỗi](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/47)
**Baseline ban đầu:** commit `ebe0b55` trên `codex/sync-mchihihaha-main`; nhánh xử lý hiện tại là `codex/nfr-06-idempotency`.
**Môi trường:** Windows; .NET 8.0.31; Node.js 24.21.0; Google Chrome 156.0.8078.80.

Đây là vòng kiểm thử đầu với provider/API giả lập và SQLite in-memory. Không gọi Gemini thật, không kết nối staging/PostgreSQL, không dùng dữ liệu người dùng. Kết quả này không phải nghiệm thu live.

## Lệnh và kết quả

| Bộ kiểm tra | Kết quả | Ghi chú |
| --- | --- | --- |
| `dotnet run --project tests/GeminiReliability/GeminiReliability.csproj --configuration Release` | **15/15 pass** | Có cảnh báo `NU1900`: máy không lấy được dữ liệu vulnerability từ NuGet; harness vẫn chạy xong. |
| `node --test --test-concurrency=1 --test-reporter=tap tests/frontend-session.test.cjs tests/fallback-cards.test.cjs backend/TutorChecks/frontend-tutor.test.cjs` | **23/23 pass** | Session, quota metadata, giữ đề bài khi lỗi AI và fallback thẻ. |
| `dotnet run --no-restore --project backend/TutorChecks/TutorChecks.csproj --configuration Release` | **33 + 44 + 59 checks pass** | Mock provider và SQLite in-memory; không phải PostgreSQL. Dùng `--no-restore` vì NuGet vulnerability endpoint không truy cập được. |
| `TUTOR_BROWSER='C:\Program Files\Google\Chrome\Application\chrome.exe'; node --test --test-concurrency=1 --test-reporter=tap tests/ai-cards-browser.test.cjs backend/TutorChecks/programming-browser.test.cjs` | **2/2 pass** | Chrome thật, API/provider giả lập; gồm quota, lỗi lưu từng phần, retry, giữ code và notebook state. |
| `TUTOR_BROWSER='C:\Program Files\Google\Chrome\Application\chrome.exe'; node --test --test-concurrency=1 --test-reporter=tap tests/notebook-mobile-voice.test.cjs` | **1/1 pass** | Lỗi chat 503 giả lập; giữ câu hỏi và hiện nút retry. Speech được giả lập. |
| Probe tạm: commit trong mock rồi làm rơi response, sau đó retry trong UI | **FAIL — 4 bản ghi thay vì 3** | Câu hỏi `Alpha` bị lưu hai lần; xem chi tiết bên dưới. Probe đã xóa sau khi chạy. |

Các bài browser cần quyền localhost để mở trang kiểm thử. Lần đầu thiếu Chromium riêng của Playwright; đã chọn Chrome có sẵn bằng `TUTOR_BROWSER`.

## Failure phát hiện ở vòng đầu (đã sửa)

Probe tạo ba thẻ. Mock lưu thẻ đầu tiên, rồi làm rơi response. Retry tự động với cùng `externalId` bị từ chối như khi gặp unique constraint. Khi người dùng bấm thử lại, `frontend/script.js` tạo `id` mới cho cùng thẻ; mock nhận bản ghi mới. Kết quả: **4 bản ghi cho 3 thẻ cần lưu, trong đó `Alpha` xuất hiện hai lần**, dù UI báo đã lưu ba thẻ ở lần retry.

Căn cứ trong mã: `frontend/script.js` dựng ID ngẫu nhiên bên trong `saveAiGeneratedCards()` mỗi lần thử; `backend/MindSprint.Api/Controllers/FlashcardsController.cs` tạo bản ghi mới mỗi POST; `backend/MindSprint.Api/Data/AppDbContext.cs` đặt unique index trên `ExternalId`. Vì probe dùng API/DB giả lập, cần xác nhận lại với PostgreSQL test cô lập trước khi ghi là failure live. Tiêu chí “retry không tạo bản ghi trùng” hiện **không đạt trong probe giả lập**.

## Chưa xác minh sau retest

- Đầy đủ GET/POST/PUT/DELETE hoạt động khi Gemini đang lỗi; retest mới kiểm tra POST tạo flashcard.
- Lỗi DB lúc lưu không báo thành công qua ma trận đầy đủ các form.
- Retry sau khi mất response trên PostgreSQL thật; các đường tạo bản ghi ngoài thẻ AI.
- Offline thật trên browser/mạng và tổng thời gian timeout mặc định 150 giây. Các ca timeout/cancel hiện chạy bằng giá trị ngắn trong harness.

## Trạng thái vòng này

Vòng đầu phát hiện failure giả lập ở retry; sau đó đã sửa và thêm regression test lâu dài. Chi tiết retest bên dưới. Chưa push hoặc cập nhật issue GitHub.

## Retest sau khi sửa

**Thay đổi:**

- `frontend/script.js`: mỗi thẻ AI giữ cùng `clientId` qua các lần lưu lại.
- `FlashcardsController.Create`: POST dùng lại cùng `ExternalId` và cùng nội dung/tài khoản trả lại kết quả đã tạo; nội dung khác hoặc tài khoản khác nhận 409. Nếu hai POST cùng ID đua nhau, controller đọc lại hàng đã ghi sau lỗi unique constraint.
- `backend/ConcurrencyChecks/Program.cs`: thêm kiểm tra replay tuần tự/đồng thời, payload khác, chủ sở hữu khác, và CRUD flashcard sau lỗi Gemini 503.
- `tests/ai-cards-browser.test.cjs`: thêm tình huống DB mock đã ghi nhưng response của POST đầu và retry tự động đều bị rơi; sau khi người dùng bấm lưu lại, chỉ có đúng ba hàng và `Alpha` xuất hiện một lần.

| Kiểm tra | Kết quả sau sửa |
| --- | --- |
| Gemini reliability harness | **15/15 pass** |
| TutorChecks | **33 + 44 + 59 checks pass** (mock provider, SQLite in-memory) |
| ConcurrencyChecks | **31 checks pass** (SQLite cô lập), gồm idempotency và tạo flashcard khi Gemini trả 503 |
| Toàn bộ frontend/browser CI command | **54 tổng: 53 pass, 0 fail, 1 skip**; Kanban integration skip vì không đặt `MINDSPRINT_TEST_API` |
| Browser retry-after-response-loss regression | **Pass**: đúng 3 bản ghi sau retry, không lặp `Alpha` |
| `npm run build` trong `frontend` | **Pass** |
| Backend API Release build `--no-restore` | **Pass, 0 warning, 0 error** |
| `git diff --check`, JavaScript syntax | **Pass** |

Probe ban đầu ghi 4 hàng cho 3 thẻ; regression sau sửa ghi đúng 3 hàng. Các bài dùng Chrome/Playwright và dữ liệu giả. NuGet có thể in `NU1900` do máy không truy cập được vulnerability endpoint; build và harness vẫn hoàn thành.

**Trạng thái hiện tại:** các ca NFR-06 đã chạy ở mức mock/controller và SQLite cô lập đều pass sau sửa. Chưa đủ căn cứ để tuyên bố nghiệm thu live hoặc đóng #47: chưa chạy Kestrel + PostgreSQL staging, chưa mô phỏng rớt response trên DB staging, chưa chờ đủ timeout mặc định 150 giây, và chưa kiểm tra idempotency cho các endpoint tạo bản ghi ngoài thẻ AI. Cần giữ các điểm này là blocked/chưa xác minh, không suy rộng kết quả mock thành production.

Thay đổi nằm ở nhánh local `codex/nfr-06-idempotency`; chưa commit, push hoặc đăng kết quả lên GitHub.
