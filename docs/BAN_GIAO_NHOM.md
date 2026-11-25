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
- Muốn xem nhanh: trang Đăng nhập → **Dùng thử tài khoản demo** (có sẵn bộ học, sổ tay, công việc, lịch học). Nút này chỉ hiện khi chạy trên máy.
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
| EF Core + Npgsql, 12 bảng, JWT + refresh token | `Data/AppDbContext.cs`, `Data/Entities/` (49 bảng), `Data/Migrations/`; ASP.NET Identity + `Services/LoginService.cs`, `Services/DeviceSessionService.cs` (phiên thiết bị) |

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
| Các controller `/api/...`, Dockerfile, Render | `Controllers/` (mỗi API cũ thành một action MVC, xem bảng 2 trong trang bằng chứng). Triển khai: **chưa làm**, sẽ lên server cá nhân |

Cần biết: lỗi nghiệp vụ ném `BusinessException`; `BusinessExceptionFilter` tự trả `{ "error": "…" }` cho lời gọi `fetch`, hoặc hiện thông báo đỏ cho form thường.

### Sơn · QA, tài liệu
| Bản cũ | Bản mới |
|---|---|
| Test case, báo cáo, UML | `tests/MindSprintAI_ASP.NET_MVC.Tests/` (81 tệp, 1811 test), `docs/KICH_BAN_DEMO.md`, `docs/cnpm/TAI_LIEU_KIEN_TRUC_MOI.md` (sơ đồ cho kiến trúc mới) |

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

## 4. Thêm một chức năng mới: ví dụ đi hết một vòng

Giả sử thêm "ghi chú cho công việc Kanban":

1. `Data/Entities/Kanban.cs`: thêm `public string? Note { get; set; }` (cho phép rỗng).
2. `dotnet ef migrations add ThemGhiChuCongViec --project MindSprintAI_ASP.NET_MVC` → mở tệp migration, chắc chắn chỉ có `AddColumn`.
3. `Services/KanbanService.cs`: kiểm độ dài, lưu.
4. `Controllers/KanbanController.cs`: nhận thêm trường.
5. `Views/Kanban/Index.cshtml`: thêm ô nhập.
6. `tests/.../KanbanTests.cs`: thêm một test cho quy tắc mới.
7. Chạy toàn bộ test, rồi thử trên trình duyệt (sáng, tối, khổ điện thoại).

## 5. Những gì còn lại
- Thử tiếp các tính năng AI khi key hồi lượt: Nghe tổng quan, Flashcard / Trắc nghiệm làm thử trong Studio, Gia sư 4 chế độ, đọc chữ từ ảnh, nguồn từ link, "Tạo thẻ bằng AI" ở Thư viện, trợ lý AI trong bộ học.
- Đăng nhập Google: cần mã ứng dụng (xem hướng dẫn riêng).
- Triển khai lên server cá nhân, sau đó thử trên điện thoại thật (ôn thẻ khi mất mạng, cài ứng dụng).
