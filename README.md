# MindSprint AI

Ứng dụng học tập của nhóm MindSprint: thư viện bộ học (thẻ ghi nhớ + quiz), luyện tập từ vựng, lớp học và giao bài, Sổ tay AI (hỏi đáp có trích dẫn, Studio, Gia sư), lịch học, công việc (Kanban), tổng quan và điểm yếu.

ASP.NET Core MVC (.NET 10) · Razor Views · EF Core + SQL Server · ASP.NET Core Identity · SignalR.

## 1. Cần cài

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- SQL Server (bản Developer hoặc Express) chạy trên máy, đăng nhập bằng tài khoản Windows
- Trình duyệt Chrome/Edge

## 2. Chạy lần đầu

Mở terminal ở thư mục chứa `MindSprintAI_ASP.NET_MVC.slnx`:

```bash
dotnet tool restore                                   # cài dotnet-ef đúng phiên bản của project
dotnet ef database update --project MindSprintAI_ASP.NET_MVC   # tạo database "MindSprintAI" trên localhost
dotnet run --project MindSprintAI_ASP.NET_MVC          # mở địa chỉ in ra trong terminal
```

Chuỗi kết nối nằm ở `MindSprintAI_ASP.NET_MVC/appsettings.Development.json` (`Server=localhost`). Máy dùng SQL Express thì đổi thành `Server=localhost\\SQLEXPRESS`.

## 3. Xem nhanh bằng tài khoản demo (một cú bấm)

Ở trang **Đăng nhập** bấm **Dùng thử tài khoản demo**. Lần đầu, tài khoản demo được tạo kèm dữ liệu mẫu:

- Bộ học "Demo — Sinh học tế bào": 6 thẻ ghi nhớ + 5 câu quiz
- Sổ tay AI "Demo — Sinh học" có một nguồn và một ghi chú
- 3 việc trên bảng Công việc, 2 buổi trong Lịch học

Nút demo **chỉ hiện khi chạy ở chế độ phát triển và mở từ chính máy đó** — bản triển khai thật không có.

## 4. Tài khoản thật và vai trò

- Đăng ký tài khoản mới ở trang Đăng ký. Khi chạy trên máy, thư xác nhận được lưu thành tệp trong `App_Data/emails` (trang Đăng ký có link mở thư thử).
- Đổi **Học viên ⇄ Giảng viên** ở menu tài khoản. Giảng viên tạo lớp, giao bài, xem báo cáo; học viên vào lớp bằng mã.
- Email trong `Admin:Emails` của `appsettings.Development.json` có thêm trang **Quản trị**.

## 5. Dùng tính năng AI

Các tính năng AI (tạo câu hỏi, Sổ tay AI, Gia sư) dùng **API key Gemini của chính người dùng**: vào trang **Tài khoản → API key Gemini**, đọc thông báo và dán key (lấy miễn phí ở Google AI Studio). Không có key thì mọi phần khác vẫn chạy bình thường.

## 6. Chạy test

```bash
bash MindSprintAI_ASP.NET_MVC/tools/run-tests.sh            # hoặc tools/run-tests.ps1 trên PowerShell
```

Test dùng database riêng `MindSprintAI_Tests` (tự tạo). Máy ít RAM: tắt app đang chạy trước khi chạy test.

## 7. Tài liệu cho nhóm

- `docs/KICH_BAN_DEMO.md` — kịch bản trình diễn 10 phút
- `docs/DOI_CHIEU_CHUC_NANG.md` — đối chiếu tính năng với bản nhóm cũ và QuizEdu
- `docs/GIAO_DIEN_NHOM.md` + `docs/giao-dien-nhom/` — giao diện chuẩn của nhóm
