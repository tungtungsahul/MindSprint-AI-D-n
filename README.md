# MindSprint AI (bản chuyển từ SmartFlash)

Stack đúng theo dự án: **HTML/CSS/JS (Fetch API)** + **C# ASP.NET Core Web API** + **SQL Server (EF Core Code First)** + **JWT** + **Gemini API**.

## Cấu trúc
- `frontend/` – SmartFlash cũ + `api.js` (client gọi API). Vẫn chạy offline bằng localStorage nếu chưa đăng nhập.
- `backend/MindSprint.Api/` – Web API:
  - `Models/Entities.cs` – User, Flashcard, CardProgress (SRS), KanbanTask
  - `Data/` – `AppDbContext`, `DataSeeder` (bơm 1.814 từ Oxford từ `vocab_seed.json`)
  - `Services/` – `SpacedRepetitionService` (tính NextReviewDate), `GeminiService` (ép JSON), `TokenService` (JWT)
  - `Controllers/` – Auth, Flashcards (CRUD), Review (SRS), Tasks (Kanban CRUD + move), Ai (Gemini)

## Chạy backend
```bash
cd backend/MindSprint.Api
dotnet run          # tự Migrate + Seed, Swagger tại /swagger
```
Sửa cấu hình cục bộ: chuỗi kết nối SQL Server, `Jwt:Key` (>= 32 ký tự), `Gemini:ApiKey`, `Cors:Origins`. Giữ khóa API ngoài commit; có thể dùng biến môi trường `Gemini__ApiKey`.
Cổng https mặc định xem ở `Properties/launchSettings.json` (sau khi chạy lần đầu) rồi cập nhật `API_BASE` trong `frontend/api.js`.

## Chạy frontend
Mở `frontend/` bằng Live Server (cổng 5500) – đã có sẵn trong CORS.

Hoặc chạy từ thư mục gốc:
```powershell
dotnet run --project backend/MindSprint.Api
# Trong terminal thứ hai:
python -m http.server 5500 --bind 127.0.0.1
```
Mở `http://127.0.0.1:5500/frontend/`. Backend C# chạy cổng 5000/5100 và tự áp dụng migration hiện có; không cần tạo migration khi chỉ chạy dự án.

## Kanban tích hợp
Tab **Công việc** dùng chung `MindSprintAuth` và `MindSprintApi` với Sổ tay AI, thư viện và lịch học. Có thêm/sửa/xóa, kéo thả hoặc nút chuyển cột, tìm kiếm, lọc ưu tiên và hạn hoàn thành. Công việc được lưu vào SQL Server theo tài khoản; khi lỗi kết nối, form giữ nguyên để thử lại.

`frontend/kanban.js` ghép logic bảng mới từ `frontend/js/kanban.js` vào giao diện chính. Trạng thái Todo/InProgress/Completed được ánh xạ sang Todo/Doing/Done (0/1/2) của API C#. Migration `AddTaskPriority` bổ sung mức ưu tiên cho công việc cũ với giá trị mặc định Medium.

Bản FastAPI mới kéo về được giữ ở `backend/app`, giao diện ở `frontend/standalone.html` và `frontend/js`. `python run_server.py` mở bản này ở cổng 8000 với dữ liệu/phiên riêng; web chính sử dụng backend C# ở trên.

## Endpoint chính
| Method | URL | Mô tả |
|---|---|---|
| POST | /api/auth/register, /api/auth/login | Trả JWT |
| GET/POST/PUT/DELETE | /api/flashcards | CRUD thẻ (kèm trạng thái SRS) |
| POST | /api/review | `{cardId, remembered}` → tính NextReviewDate |
| GET | /api/review/due | Thẻ đến hạn + thẻ mới |
| GET/POST/PUT/PATCH/DELETE | /api/tasks | Kanban; `PATCH /{id}/move` khi kéo thả |
| POST | /api/ai/flashcards, /api/ai/quiz | Gemini sinh thẻ/trắc nghiệm từ văn bản |

## Sổ tay AI (kiểu NotebookLM)
Tab **Sổ tay AI** trong frontend (`notebook.js`) + `NotebooksController`/`NotebookAi` ở backend:
| Tính năng NotebookLM | Trong dự án |
|---|---|
| Nguồn tài liệu | Dán văn bản, PDF/TXT/MD/CSV (tối đa 10MB), URL (có chống SSRF); tối đa 20 nguồn/sổ tay |
| Hỏi đáp có trích dẫn | `POST /api/notebooks/{id}/chat` – chỉ trả lời từ nguồn, kèm số [n] và đoạn trích |
| Câu hỏi gợi ý | `GET /api/notebooks/{id}/suggestions` |
| Tóm tắt, Study guide, FAQ | `POST /api/notebooks/{id}/generate` với `type` = summary / studyguide / faq |
| Timeline, Mind map | `type` = timeline / mindmap |
| Flashcard, Trắc nghiệm | `type` = flashcards / quiz; flashcard lưu vào thư viện thẻ + SRS qua `/save-cards` |
| Audio overview | `type` = audio (kịch bản 2 người dẫn), phát bằng giọng đọc của trình duyệt |
| Ghi chú | `/api/notebooks/{id}/notes` – lưu câu trả lời hoặc tài liệu đã tạo |

Sau khi thêm entity mới, tạo migration mới: `dotnet ef migrations add AddNotebooks`.
Chưa có: PDF dạng ảnh scan (cần OCR), audio bằng giọng AI thật, trả lời dạng streaming, tìm kiếm theo vector (hiện gửi toàn bộ nguồn, tối đa ~300k ký tự, cho Gemini).

## Kiểm tra
```powershell
node --test tests/frontend-session.test.cjs tests/frontend-text.test.cjs
dotnet run --project tests/GeminiReliability/GeminiReliability.csproj
# Kiểm tra Kanban với backend đang chạy (tạo tài khoản kiểm thử riêng):
$env:MINDSPRINT_TEST_API = 'http://localhost:5000'
node --test tests/kanban-integration.test.cjs
```
Các bài kiểm thử Python trong `tests/test_tier*.py` dành cho bản FastAPI riêng.
