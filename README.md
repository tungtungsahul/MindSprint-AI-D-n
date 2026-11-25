# MindSprint AI

Ứng dụng học tập của nhóm MindSprint: thư viện bộ học (thẻ ghi nhớ + quiz), luyện tập từ vựng, lớp học và giao bài, Sổ tay AI (hỏi đáp có trích dẫn, Studio, Gia sư), lịch học, công việc (Kanban), tổng quan và điểm yếu.

ASP.NET Core MVC (.NET 10) · Razor Views · EF Core + SQL Server · ASP.NET Core Identity · SignalR.

## 1. Cần cài

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- SQL Server (bản Developer hoặc Express) chạy trên máy, đăng nhập bằng tài khoản Windows
- Trình duyệt Chrome/Edge

## 2. Chạy lần đầu

Lấy nhánh `main` của kho nhóm rồi mở terminal ở thư mục chứa `MindSprintAI_ASP.NET_MVC.slnx`.

```bash
git clone --branch main https://github.com/tungtungsahul/MindSprint-AI-D-n.git
cd MindSprint-AI-D-n
```

```bash
dotnet tool restore                                   # cài dotnet-ef đúng phiên bản của project
dotnet ef database update --project MindSprintAI_ASP.NET_MVC   # tạo database "MindSprintAI" trên localhost
dotnet run --project MindSprintAI_ASP.NET_MVC          # mở địa chỉ in ra trong terminal
```

Chuỗi kết nối mặc định dùng `Server=localhost` trong `MindSprintAI_ASP.NET_MVC/appsettings.Development.json`. Nếu dùng SQL Express, đặt `Server=localhost\\SQLEXPRESS` trong tệp JSON và truyền cùng chuỗi kết nối cho lệnh cập nhật cơ sở dữ liệu.

```bash
dotnet ef database update --project MindSprintAI_ASP.NET_MVC --connection "Server=localhost\SQLEXPRESS;Database=MindSprintAI;Trusted_Connection=True;TrustServerCertificate=True"
```

Sau khi cập nhật mã, chạy lại bước `dotnet ef database update` trước khi mở ứng dụng để áp dụng các migration còn thiếu, gồm `DoiTenHienThi` cho trang Tài khoản.

## 3. Xem nhanh bằng tài khoản demo (một cú bấm)

Ở trang **Đăng nhập** bấm **Dùng thử tài khoản demo**. Lần đầu, tài khoản demo được tạo kèm dữ liệu mẫu:

- Bộ học "Demo — Sinh học tế bào": 6 thẻ ghi nhớ + 5 câu quiz
- Sổ tay AI "Demo — Sinh học" có một nguồn và một ghi chú
- 3 việc trên bảng Công việc, 2 buổi trong Lịch học

Nút demo **chỉ hiện khi chạy ở chế độ phát triển và mở từ chính máy đó** — bản triển khai thật không có.

Để trình diễn lớp học có sẵn bài giao và kết quả, quản trị viên mở **Quản trị → Tạo dữ liệu mẫu lớp học**. Tác vụ tạo 36 học viên mẫu, 2 giảng viên mẫu và 4 lớp. Tải lại trang để xem kết quả và mật khẩu của tài khoản mẫu.

## 4. Tài khoản thật và vai trò

- Đăng ký tài khoản mới ở trang Đăng ký. Khi chạy trên máy, thư xác nhận được lưu thành tệp trong `App_Data/emails` (trang Đăng ký có link mở thư thử).
- Trong **Tài khoản → Thông tin cá nhân**, đổi tên hiển thị bằng nút **Lưu tên**. Hai lần đổi tên cách nhau ít nhất 7 ngày.
- Tài khoản đã có mật khẩu có thể đổi tại **Tài khoản → Đổi mật khẩu**. Thiết bị đang dùng giữ phiên đăng nhập và các thiết bị khác bị đăng xuất.
- Đổi **Học viên ⇄ Giảng viên** ở menu tài khoản. Giảng viên tạo lớp, giao bài, xem báo cáo; học viên vào lớp bằng mã.
- Email trong `Admin:Emails` của `appsettings.Development.json` có thêm trang **Quản trị**.

## 5. Dùng tính năng AI

Các tính năng AI (tạo câu hỏi, Sổ tay AI, Gia sư) dùng **API key Gemini của chính người dùng**: vào trang **Tài khoản → API key Gemini**, đọc thông báo và dán key (lấy miễn phí ở Google AI Studio). Không có key thì mọi phần khác vẫn chạy bình thường.

## 6. Chạy test

```bash
bash MindSprintAI_ASP.NET_MVC/tools/run-tests.sh
```

Trên PowerShell, chạy `.\MindSprintAI_ASP.NET_MVC\tools\run-tests.ps1` từ thư mục gốc. Bộ test tạo lại cơ sở dữ liệu riêng `MindSprintAI_Tests`. Có thể trỏ `MINDSPRINT_TEST_DB` đến một SQL Server kiểm thử khác với tên cơ sở dữ liệu này.

## 7. Tài liệu cho nhóm

- [Kịch bản trình diễn 10 phút](docs/KICH_BAN_DEMO.md)
- [Bàn giao mã nguồn cho nhóm](docs/BAN_GIAO_NHOM.md)
- [Đối chiếu tính năng với bản nhóm cũ và QuizEdu](docs/DOI_CHIEU_CHUC_NANG.md)
- [Kiến trúc ASP.NET Core MVC](docs/cnpm/TAI_LIEU_KIEN_TRUC_MOI.md)
- [Yêu cầu chức năng trong Wiki](https://github.com/tungtungsahul/MindSprint-AI-D-n/wiki/Yeu-cau-chuc-nang)
- [Yêu cầu phi chức năng trong Wiki](https://github.com/tungtungsahul/MindSprint-AI-D-n/wiki/Yeu-cau-phi-chuc-nang)
- [Giao diện tham chiếu của nhóm](docs/GIAO_DIEN_NHOM.md)
- [Triển khai và cập nhật trên máy chủ](docs/TRIEN_KHAI_SERVER.md)
- [Bản frontend/API và hồ sơ đồ án trước khi chuyển sang MVC](https://github.com/tungtungsahul/MindSprint-AI-D-n/tree/9c86527291ae1e2ddf538fac804a371ba09e8a86)
