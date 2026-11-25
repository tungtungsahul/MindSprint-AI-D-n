# Bàn giao cho nhóm MindSprint

Project mới gộp chức năng và giao diện của bản nhóm (`MindSprint-AI-D-n`) vào khung của QuizEduManager. Tài liệu này giúp mỗi bạn tìm lại phần mình đã làm, chạy được project và sửa code mà không làm hỏng phần của người khác.

Đọc kèm: `README.md` (cách chạy), `docs/cnpm/TAI_LIEU_KIEN_TRUC_MOI.md` (kiến trúc + sơ đồ), `docs/DOI_CHIEU_CHUC_NANG.md` (đối chiếu từng chức năng), trang bằng chứng chuyển đổi (`docs/bang-chung-chuyen-doi.html`).

## 1. Bắt đầu trong 10 phút

```bash
git pull
dotnet tool restore
dotnet ef database update --project MindSprintAI_ASP.NET_MVC   # mỗi lần có migration mới đều chạy lại
dotnet run --project MindSprintAI_ASP.NET_MVC                    # hoặc bấm Run trong Visual Studio
```

- Mở địa chỉ in ra (Visual Studio dùng `http://localhost:5144`).
- Muốn xem nhanh: trang Đăng nhập → **Dùng thử tài khoản demo** (có sẵn bộ học, sổ tay, công việc, lịch học). Nút này hiện khi chạy ở chế độ phát triển từ chính máy đang chạy ứng dụng.
- Muốn dùng AI: trang **Tài khoản → API key Gemini**, dán key của chính mình (lấy miễn phí ở Google AI Studio). Key miễn phí có giới hạn lượt mỗi ngày; hết lượt thì app báo "đang hết lượt dùng".

## 2. Phần của từng bạn giờ nằm ở đâu

Theo tệp "Phân Công Nhiệm Vụ". Đường dẫn tính từ thư mục `MindSprintAI_ASP.NET_MVC/`.

### Duy · AI
| Bản cũ | Bản mới |
|---|---|
| `GeminiService.cs`, `NotebookAi`, `AiController`, prompt trong code | `Services/GeminiClient.cs` (mọi lần gọi Gemini), `Services/AiGenerateService.cs` (tạo thẻ/câu hỏi), `Services/NotebookAiService.cs` (Studio, Gia sư, gợi ý), `Prompts/NotebookPrompts.cs` (lời nhắc AI), `Services/FallbackCards.cs` (tách thẻ khi không có AI) |

Cần biết:
- Mỗi người dùng **key Gemini của chính họ** (`UserApiKeyService`, lưu mã hóa), không có key chung của server.
- Thời gian chờ: hỏi đáp 30 giây, việc tạo nội dung dài 120 giây (`GeminiClient.AskTimeout`, `LongTimeout`).
- Model đặt ở `appsettings.json` → `Gemini:Model` (hiện `gemini-3.6-flash`).
- Lời nhắc AI để trong `Prompts/` vì test kiểm chữ trên giao diện không quét thư mục này (lời nhắc có chữ "JSON").

### Linh · Database, bảo mật
| Bản cũ | Bản mới |
|---|---|
| EF Core + Npgsql, 12 bảng, JWT + refresh token | `Data/AppDbContext.cs`, `Data/Entities/` và `Data/Migrations/` (51 bảng nghiệp vụ và 8 bảng Identity). Đăng nhập và phiên thiết bị nằm ở `Services/LoginService.cs` và `Services/DeviceSessionService.cs`. |

Cần biết:
- **Migration chỉ thêm** bảng/cột (cột mới có mặc định hoặc cho phép rỗng), không xóa, không đổi tên.
- Khóa ngoại các bảng học tập đặt **Restrict**: xóa cha phải tự xóa con trong service, trong một giao dịch.
- **Mọi cách đăng nhập mới phải đi qua `LoginService.CompleteAsync`**, không gọi `SignInAsync` trực tiếp. Nếu gọi thẳng, cookie thiếu mã phiên thiết bị và kênh thông báo sẽ báo "đã bị đăng xuất".

### TV3 · Kanban
| Bản cũ | Bản mới |
|---|---|
| `frontend/kanban.js`, `kanban.css`, `TasksController` | `Controllers/KanbanController.cs`, `Services/KanbanService.cs` (quy tắc: tiêu đề 1–200, mô tả ≤ 5.000), `Views/Kanban/Index.cshtml`, `wwwroot/js/kanban.js` (kéo thả), CSS trong `wwwroot/css/mindsprint-pages-d.css` |

Cần biết: thêm/sửa/xóa là form thường nên vẫn chạy khi JS lỗi; kéo thả gọi `Kanban/Move`. Test: `tests/.../KanbanTests.cs`.

### Chiến · API, triển khai
| Bản cũ | Bản mới |
|---|---|
| Các controller `/api/...`, Dockerfile, Render | `Controllers/` (xem bảng 2 trong trang bằng chứng). Bộ triển khai MVC gồm `Dockerfile`, `deploy/docker-compose.yml` và hướng dẫn chạy migration. [Hướng dẫn triển khai máy chủ](TRIEN_KHAI_SERVER.md) |

Cần biết: lỗi nghiệp vụ ném `BusinessException`; `BusinessExceptionFilter` tự trả `{ "error": "…" }` cho lời gọi `fetch`, hoặc hiện thông báo đỏ cho form thường.

### Sơn · QA, tài liệu
| Bản cũ | Bản mới |
|---|---|
| Test case, báo cáo, UML | `tests/MindSprintAI_ASP.NET_MVC.Tests/`, `docs/KICH_BAN_DEMO.md`, `docs/cnpm/TAI_LIEU_KIEN_TRUC_MOI.md` (sơ đồ cho kiến trúc mới) |

Cần biết: test dùng database riêng `MindSprintAI_Tests`, tự tạo; chạy `tools/run-tests.ps1` (PowerShell) hoặc `bash tools/run-tests.sh`.

### Hùng · Flashcard, kiểm tra dữ liệu, giao diện điện thoại
| Bản cũ | Bản mới |
|---|---|
| Thẻ lật 3D, kiểm tra form/tệp, responsive | `Views/Flashcards/Study.cshtml`, `wwwroot/js/flashcards.js`, `MindSprintFlip` + kiểm tệp `data-max-bytes` trong `wwwroot/js/site.js`, `wwwroot/css/mindsprint-pages-d.css` (khổ 390px) |

## 3. Quy ước khi sửa code (đọc trước khi làm)

1. **Thứ tự làm:** Database → Service (quy tắc) → Controller → View. View không tự chấm điểm hay kiểm quyền.
2. **Chữ trên giao diện** viết cho người không rành máy: tránh "cookie, token, API, server, JSON…". Test `PlainLanguageAudit` sẽ báo nếu lỡ dùng (ngoại lệ: "API key Gemini").
3. **CSS/JS sửa xong phải build lại và chạy lại app**, rồi tải lại trang bằng Ctrl+Shift+R (app phục vụ tệp tĩnh theo bản đã build).
4. **Điện thoại:** luật chung `main a:not(.btn)` biến link thành `inline-block` cho dễ bấm. Link nào cần `display: flex` thì viết `main a.ten-lop { display: flex; }`.
5. **Giao diện tối:** màu lấy từ biến `--…` trong `mindsprint.css`; không dùng lớp màu cố định như `text-bg-light`, `bg-white` (có test chặn).
6. **Quy tắc nghiệp vụ** (ngưỡng, giới hạn, cách chấm): muốn đổi thì hỏi nhóm trưởng trước.
7. **Chạy toàn bộ test trước khi đẩy code.** Máy ít RAM thì tắt app trước khi chạy test.

## 4. Ví dụ đọc một thay đổi từ cơ sở dữ liệu đến giao diện

Luồng đổi tên hiển thị nằm trong các tệp sau.

1. `Data/Entities/Accounts.cs` định nghĩa `DisplayNameChangedAt` để lưu thời điểm đổi gần nhất.
2. `Data/Migrations/20261119051538_DoiTenHienThi.cs` thêm cột cho phép rỗng trên `AspNetUsers`.
3. `Services/ProfileService.cs` kiểm tra tên, giới hạn mỗi lần đổi cách nhau ít nhất 7 ngày và ghi nhật ký.
4. `Controllers/AccountProfile.cs` nhận biểu mẫu và đưa người dùng về trang Tài khoản sau khi lưu.
5. `Views/Account/Index.cshtml` hiển thị ô đổi tên hoặc thời điểm có thể đổi tiếp.
6. `tests/MindSprintAI_ASP.NET_MVC.Tests/HoSoCaNhanTests.cs` kiểm tra đổi tên và đổi mật khẩu.

## 5. Kiểm tra trước buổi demo

- Kiểm tra đổi tên hiển thị và đổi mật khẩu trong **Tài khoản**. Dùng hai phiên đăng nhập để xác nhận phiên đang dùng còn hiệu lực sau khi đổi mật khẩu.
- Dùng key Gemini còn lượt để thử Nghe tổng quan, Flashcard và Trắc nghiệm trong Studio, Gia sư, đọc chữ từ ảnh, nguồn từ link và trợ lý AI trong bộ học.
- Cấu hình mã ứng dụng nếu trình diễn đăng nhập Google. [Hướng dẫn đăng nhập Google](HUONG_DAN_DANG_NHAP_GOOGLE.md)
- Mở bản triển khai trên điện thoại để kiểm tra ôn thẻ khi mất mạng và cài ứng dụng. [Hướng dẫn triển khai máy chủ](TRIEN_KHAI_SERVER.md)
