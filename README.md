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
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate
dotnet run          # tự Migrate + Seed, Swagger tại /swagger
```
Sửa `appsettings.json`: chuỗi kết nối SQL Server, `Jwt:Key` (>= 32 ký tự), `Gemini:ApiKey`, `Cors:Origins`.
Cổng https mặc định xem ở `Properties/launchSettings.json` (sau khi chạy lần đầu) rồi cập nhật `API_BASE` trong `frontend/api.js`.

## Chạy frontend
Mở `frontend/` bằng Live Server (cổng 5500) – đã có sẵn trong CORS.

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

## Việc còn lại (chưa làm trong bản này)
- Đăng nhập hiện chỉ có trong tab Sổ tay AI; chưa có màn hình đăng nhập chung cho cả app.
- Giao diện bảng Kanban + Drag & Drop (đã có API `/api/tasks`).
- Nút "Tạo thẻ bằng AI" gọi `MindSprintApi.aiFlashcards`.
- Đồng bộ thêm/sửa/xóa thẻ tự tạo lên server (đã có `createCard/updateCard/deleteCard`).
