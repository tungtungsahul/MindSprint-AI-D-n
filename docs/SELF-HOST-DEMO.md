# Vận hành MindSprint trên máy cá nhân

Máy Windows chạy Docker Desktop là máy chủ cho giao diện, API và SQL Server Express 2022.
Mặc định web mở trên máy chủ tại `http://localhost:8080`.
Theo yêu cầu ngày 11/11/2026, có thể chia sẻ HTTPS qua Cloudflare Tunnel bằng
`scripts/share.ps1`; xem [truy cập Internet và bảo vệ khóa](PUBLIC-ACCESS.md).
Không mở cổng router hoặc công khai SQL Server.
Tên Compose `mindsprint-demo` được giữ để tiếp tục dùng đúng volume hiện tại;
ứng dụng chạy ở chế độ `Production`, không bật tài khoản đăng nhập demo.

## Bật và tắt

Mở Docker Desktop, đợi Engine sẵn sàng. Tại thư mục gốc dự án, chạy PowerShell:

```powershell
.\scripts\selfhost.ps1
```

Lần đầu Docker tải image và build ứng dụng. Script tạo `.env.selfhost` với mật khẩu
database và khóa JWT ngẫu nhiên; file này được Git bỏ qua và không vào Docker image.
Nếu có Gemini API key trong biến `GEMINI_API_KEY` hoặc cấu hình Development cục bộ,
script dùng lại key đó cho bản demo; không in key ra terminal.

- Trên máy chủ: mở `http://localhost:8080`.
- Tạo tài khoản bằng email/mật khẩu để dùng; đăng nhập Demo bị tắt trong Production.
- Kết thúc buổi kiểm tra: `.\scripts\selfhost.ps1 -Action Stop`.
- Xem trạng thái: `.\scripts\selfhost.ps1 -Action Status`.

Start chờ cả database và web báo healthy, tối đa 240 giây sau khi build.
Yêu cầu Docker Compose v2 có `up --wait` và `--wait-timeout`.
Hai container dùng `restart: unless-stopped`: Docker tự khởi động lại khi tiến trình
kết thúc và khi Docker Engine chạy lại, trừ khi đã chủ động Stop.
Health check thất bại chỉ đánh dấu unhealthy; Docker Compose không tự restart
container chỉ vì unhealthy. Web có thể kết nối lại sau khi database hoạt động trở lại.

Docker Desktop phải đang chạy. Muốn phục vụ sau khi đăng nhập Windows, bật tùy chọn
khởi động Docker Desktop khi đăng nhập trong ứng dụng Docker. Máy Sleep vẫn ngừng phục vụ;
restart policy không tự đánh thức máy. Kiểm tra reboot/sleep thực tế do chủ máy thực hiện.

Lệnh Stop dừng web và database, giữ dữ liệu trong Docker volume `mindsprint-demo_sqlserver-data`.
Không dùng `docker compose down -v` vì tùy chọn `-v` xóa volume và dữ liệu demo.
Ở chế độ mặc định, web chỉ phục vụ trên máy chủ; muốn truy cập từ xa phải bật Share.
Share Stop tắt đường dẫn Internet mà không dừng web/database.

## Dữ liệu và AI

Bộ demo dùng **database riêng**, không tự sao chép tài khoản hoặc sổ tay từ database
Development, Render hay dự án khác trên máy. Dữ liệu từ vựng được seed khi khởi động.
Hãy tạo tài khoản và chuẩn bị nội dung trình bày trước giờ kiểm tra.

Volume PostgreSQL cũ `mindsprint-demo_demo-data` được giữ nguyên nếu đã tồn tại.
SQL Server dùng volume mới; việc chuyển provider không tự chuyển dữ liệu cũ.

## Sao lưu và khôi phục SQL Server

Chạy tại thư mục gốc khi database đang hoạt động:

```powershell
$backup = .\scripts\selfhost-db.ps1 -Action Backup
$backup
.\scripts\selfhost-db.ps1 -Action VerifyRestore -BackupFile $backup.BackupFile
```

Backup dùng `COPY_ONLY, CHECKSUM`, xác minh bằng `RESTORE VERIFYONLY`, rồi xuất file
`.bak` vào `backups/` trên Windows, kèm SHA256 trong kết quả. Có thể chọn thư mục
khác bằng `-Directory 'E:\MindSprintBackups'`. Không cần dừng web để tạo backup.
`VerifyRestore` khôi phục vào database ngẫu nhiên, chạy `DBCC CHECKDB`, đọc các bảng
MindSprint rồi xóa đúng database thử. Database đang dùng được giữ nguyên.

Để phục hồi sử dụng thực tế, giữ lại database khôi phục mới:

```powershell
$restored = .\scripts\selfhost-db.ps1 -Action Restore -BackupFile $backup.BackupFile
$restored
```

Script trả tên `mindsprint_restore_<GUID>`, không ghi đè database hiện hữu.
Thêm hoặc sửa duy nhất dòng `DATABASE_NAME=<tên Database trong kết quả>` trong
`.env.selfhost`, rồi chạy `.\scripts\selfhost.ps1` để chuyển web sang database đó.
Tên chỉ được gồm chữ cái, chữ số và dấu gạch dưới, bắt đầu bằng chữ cái, tối đa 128 ký tự.
Để quay về database ban đầu, đặt `DATABASE_NAME=mindsprint` rồi chạy Start.
Sau khi chuyển, Backup tự chọn database đang được cấu hình trong Compose.

`backups/` và `*.bak` được Git bỏ qua, Docker build không đưa các file này vào image.
Backup chứa dữ liệu tài khoản và nội dung học tập: giữ riêng và sao chép bản cần giữ
sang ổ đĩa khác. Volume trên cùng Docker Engine không thay thế cho backup ngoài máy.
Giữ riêng `.env.selfhost`, đặc biệt mật khẩu SQL Server khi dùng lại volume cũ.

Kiểm chứng backup/restore và đối chiếu dữ liệu trên bản đang chạy (tránh sửa dữ liệu
đồng thời trong lúc so sánh):

```powershell
.\tests\selfhost-db-checks.ps1
```

File hỏng bị từ chối; restore lỗi được dọn trong database thử, không dùng `WITH REPLACE`.

Nếu chưa có Gemini key, chạy Start để tạo file `.secrets.selfhost/gemini-api-key`,
điền khóa vào file đó bằng trình soạn thảo local, rồi chạy lại Start. Không gửi file
secret hoặc `.env.selfhost` cho người khác hay commit Git.

Khóa Gemini được chuyển tự động từ `.env.selfhost` sang `.secrets.selfhost/gemini-api-key`
khi chạy Start. Chỉ sửa file secret này để thay khóa, không nhập khóa vào giao diện.
Docker chỉ gắn file ở chế độ đọc cho backend, không đưa khóa vào environment/image.
Trên Windows, script giới hạn quyền đọc file secret và `.env.selfhost` cho tài khoản
đang chạy script và SYSTEM. Người quản trị máy/Docker vẫn có khả năng truy cập secret.

Để đăng nhập Google, tạo OAuth client loại Web application trong Google Cloud Console,
thêm `http://localhost` và `http://localhost:8080` vào Authorized JavaScript origins.
Điền Client ID vào dòng `GOOGLE_CLIENT_ID=` trong `.env.selfhost`, rồi chạy lại script Start.
Mở đúng `http://localhost:8080` để khớp origin Google và danh sách cho phép của backend.
Luồng popup dùng JavaScript callback nên không cần Authorized redirect URI hoặc Client Secret.
Khi chia sẻ Internet, Authorized JavaScript origins trên Google cũng cần thêm đúng
origin HTTPS hiện tại. Client ID là định danh công khai, không phải Gemini API key.

Image demo không cài trình duyệt Chromium. Nhập URL cần chạy JavaScript có thể không
hoạt động; nên chuẩn bị nguồn văn bản hoặc URL HTML thông thường khi trình bày.

## Phạm vi chạy thử

Trình bày trực tiếp trên máy chủ, giữ máy bật và tránh chế độ Sleep trong buổi kiểm tra.
Chức năng Gemini cần Internet và gửi nội dung yêu cầu tới dịch vụ AI.
Chia sẻ web ra Internet chỉ được bật qua script Share theo hướng dẫn riêng.

Web chỉ công bố cổng `8080` trên loopback của máy chủ. SQL Server chỉ có trong mạng
Docker. Giao diện gọi API cùng origin; chế độ `Hosting:ServeFrontend` không cấp quyền
CORS cho website khác. Cấu hình Vercel/Render cũ không thay đổi.

## Kiểm tra khi gặp lỗi

```powershell
docker compose --env-file .env.selfhost -f compose.selfhost.yaml ps
docker compose --env-file .env.selfhost -f compose.selfhost.yaml logs --tail 80 web
```

Nếu cổng 8080 đang được ứng dụng khác sử dụng, dừng ứng dụng đó trước khi chạy demo.
`/healthz` xác nhận tiến trình web hoạt động (HTTP 200, `status=ok`).
`/readyz` thực hiện `SELECT 1` vào database: HTTP 200 với `status=ready`, hoặc
HTTP 503 với `status=unavailable`. Kết nối SQL Server của probe có timeout 2 giây
và tắt retry kết nối; middleware có timeout 3 giây. Phản hồi không chứa chuỗi kết nối
hay chi tiết lỗi SQL. Docker health check của web gọi `/readyz` mỗi 10 giây.

Kiểm tra nhanh toàn bộ đường đi web/API/database (Python 3, không cần cài thư viện):

```powershell
python tests/selfhost-smoke.py
# Có gián đoạn ngắn: dừng/khởi động DB và kết thúc tiến trình web để thử restart.
python tests/selfhost-smoke.py --recovery
# Gửi văn bản mẫu tới Gemini; cần Internet, key hợp lệ và quota.
python tests/selfhost-smoke.py --ai
# Trình duyệt thật; cần build backend và Chromium/Edge cho Playwright.
node tests/selfhost-browser.cjs
```

Bài kiểm tra tạo một tài khoản `hosting-check-...@example.com` riêng, thử đăng nhập,
tạo/đọc/xóa công việc, thẻ tiếng Việt, sổ tay và nguồn văn bản rồi thu hồi phiên đăng nhập.
Bài này kiểm tra cấu hình/origin Google, không tự đăng nhập tài khoản Google của bạn.
Mặc định không gọi Gemini hoặc ngắt container; hai thao tác này cần cờ tương ứng.
`--recovery` luôn thử khởi động lại DB trong `finally` và kiểm tra dữ liệu còn sau phục hồi.
Kiểm tra trình duyệt tạo tài khoản `hosting-browser-...@example.com` riêng.
Nếu chỉ có Edge, đặt biến môi trường `TUTOR_BROWSER` thành đường dẫn `msedge.exe`.
Đăng nhập Google và chấp thuận tài khoản thật vẫn cần kiểm tra thủ công.

Tham khảo: [backup/restore SQL Server container](https://learn.microsoft.com/sql/linux/migrate/tutorial-restore-backup-sql-server-container),
[Docker restart policy](https://docs.docker.com/reference/compose-file/services/#restart),
[ASP.NET Core health checks](https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-8.0),
[SQL Client reconnect](https://learn.microsoft.com/dotnet/api/microsoft.data.sqlclient.sqlconnectionstringbuilder.connectretrycount).
