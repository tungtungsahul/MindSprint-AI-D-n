# Triển khai MindSprint AI lên server cá nhân (Linux + Docker + Cloudflare Tunnel)

Mô hình: **Trình duyệt → Cloudflare → Cloudflare Tunnel (cloudflared trên server) → `http://localhost:8080` → container `mindsprint` → container SQL Server.**

> **Cổng 80/443 là gì?** Là "cửa" mặc định của web (80 = http, 443 = https). Vì dùng **Cloudflare Tunnel**, server tự kết nối ra Cloudflare nên **không cần mở cửa nào** trên server; Cloudflare lo phần https. App chỉ mở `127.0.0.1:8080` cho chính server.

> **Server của nhóm (đã chạy Rekal / QuizEdu)** dùng cách riêng ở cuối tài liệu: mục "Server đã có Rekal". Phần Docker dưới đây dành cho server mới. Image đã được build thử ngày 17/11/2026 (có sẵn LibreOffice; đã đổi thử Word sang PDF trong container thành công); phần chạy container bằng `docker compose` chưa chạy thử trên server thật.

## 0. Chuẩn bị (làm trên trình duyệt, một lần)

| Cần | Ở đâu | Dùng cho |
|---|---|---|
| **Turnstile** Site key + Secret key | Cloudflare dashboard → **Turnstile** → *Add widget* → nhập tên miền → *Managed* | Hộp chống robot ở trang đăng ký/đăng nhập (bắt buộc khi chạy thật) |
| **Mật khẩu ứng dụng Gmail** | Bật xác minh 2 bước cho Gmail → https://myaccount.google.com/apppasswords → tạo mật khẩu 16 ký tự | Gửi thư xác nhận email (thiếu thì người đăng ký mới không đăng nhập được) |
| Google Client ID/Secret (không bắt buộc) | `docs/HUONG_DAN_DANG_NHAP_GOOGLE.md` | Nút đăng nhập Google |

## 1. Lấy code lên server

```bash
git clone <địa chỉ repo nhóm> mindsprint && cd mindsprint      # lần đầu
# hoặc: cd mindsprint && git pull                               # các lần sau
```

## 2. Tìm thông tin container SQL Server

```bash
docker ps --format '{{.Names}}\t{{.Image}}\t{{.Ports}}'          # tên container SQL Server (cột 1)
docker inspect <tên-container-sql> --format '{{json .NetworkSettings.Networks}}'   # tên mạng (khóa đầu tiên)
docker network inspect <tên-mạng> --format '{{(index .IPAM.Config 0).Gateway}}'   # gateway, ví dụ 172.18.0.1
```

Nếu tên mạng là `bridge` (mạng mặc định), tạo mạng riêng rồi nối SQL Server vào: `docker network create mindsprint-net && docker network connect mindsprint-net <tên-container-sql>`, sau đó dùng `mindsprint-net` cho các bước dưới.

## 3. Điền cấu hình

```bash
cd deploy
cp .env.example .env
chmod 600 .env          # chỉ chủ tài khoản đọc được (có mật khẩu)
nano .env
```

Điền: `SQL_NETWORK` (tên mạng), tên miền ở 3 dòng `App__BaseUrl` / `AllowedHosts` / `Passkeys__ServerDomain`, `ConnectionStrings__Default` (tên container SQL + mật khẩu), `ReverseProxy__KnownProxies__0` (gateway ở bước 2), Turnstile, SMTP, `Admin__Emails__0` (email của bạn để vào trang Quản trị). Mật khẩu có ký tự `$` thì viết `$$`.

## 4. Đóng gói và cập nhật database

```bash
docker compose build
docker compose run --rm --entrypoint sh mindsprint -c './efbundle --connection "$ConnectionStrings__Default"'
```

Lần đầu lệnh thứ hai tạo database `MindSprintAI` và mọi bảng; các lần sau chỉ thêm phần mới. Thấy `Done.` là được.

## 5. Chạy

```bash
docker compose up -d
curl -s -H "Host: <tên-miền>" http://127.0.0.1:8080/healthz   # "ok" = app đang chạy
curl -s -H "Host: <tên-miền>" http://127.0.0.1:8080/readyz    # "ready" = app nối được database ("not ready" = lỗi kết nối SQL)
docker logs mindsprint --tail 50       # xem lỗi nếu có
```

App tự khởi động lại khi server khởi động lại (`restart: unless-stopped`). Phải có `-H "Host: …"` vì app chỉ nhận đúng tên miền trong `AllowedHosts` (gọi thẳng `127.0.0.1` sẽ bị 400).

## 6. Nối tên miền qua Cloudflare Tunnel

Cloudflare dashboard → **Zero Trust → Networks → Tunnels** → chọn tunnel của server → **Public Hostname → Add**:
- *Subdomain/Domain*: tên miền của app
- *Service*: `HTTP` — `localhost:8080`

Mở `https://<tên-miền>` trên điện thoại và máy tính.

## 7. Thử sau khi lên mạng

1. Đăng ký tài khoản mới → nhận thư xác nhận → đăng nhập.
2. Dán API key Gemini ở trang Tài khoản, hỏi thử Sổ tay AI.
3. Trên điện thoại: **Cài đặt → Cài ứng dụng lên máy**; mở Ôn thẻ, tắt Wi-Fi giữa chừng, ôn tiếp rồi bật lại mạng (đánh giá được gửi bù).
4. Nút "Dùng thử tài khoản demo" **không** có trên server (chỉ hiện khi chạy trên máy dev).

## 8. Cập nhật phiên bản mới

```bash
cd mindsprint && git pull && cd deploy
docker compose build
docker compose run --rm --entrypoint sh mindsprint -c './efbundle --connection "$ConnectionStrings__Default"'
docker compose up -d
```

## 9. Sao lưu và khôi phục

**Database** (lệnh `sqlcmd` hỏi mật khẩu `sa`: tự gõ, không ghi vào lệnh):

```bash
SQL=<tên-container-sql>
docker exec $SQL mkdir -p /var/opt/mssql/backup
docker exec -it $SQL /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -Q "BACKUP DATABASE [MindSprintAI] TO DISK = N'/var/opt/mssql/backup/MindSprintAI.bak' WITH INIT"
docker cp $SQL:/var/opt/mssql/backup/MindSprintAI.bak ./MindSprintAI-$(date +%F).bak   # chép ra ngoài container
```

Khôi phục (ghi đè database hiện tại, nên dừng app trước):

```bash
docker cp ./MindSprintAI-2026-11-15.bak $SQL:/var/opt/mssql/backup/restore.bak
cd deploy && docker compose stop mindsprint
docker exec -it $SQL /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -Q "RESTORE DATABASE [MindSprintAI] FROM DISK = N'/var/opt/mssql/backup/restore.bak' WITH REPLACE"
docker compose start mindsprint
```

Bản `.bak` nên chép sang máy khác (một bản sao lưu chỉ nằm trên chính server thì hỏng server là mất cả hai). Thử khôi phục một lần trên máy dev để chắc bản sao lưu dùng được.

**Ổ dữ liệu `mindsprint-data`** (khóa mã hóa key Gemini và phiên đăng nhập, ảnh đại diện người dùng tải lên, nhật ký): `docker run --rm -v deploy_mindsprint-data:/d -v "$PWD":/b alpine tar czf /b/mindsprint-data.tgz -C /d .` (tên ổ xem bằng `docker volume ls`). Mất ổ này thì mọi người phải đăng nhập lại, dán lại key Gemini và tải lại ảnh đại diện.

## 10. Lỗi thường gặp

| Hiện tượng | Nguyên nhân | Cách sửa |
|---|---|---|
| Trình duyệt báo "chuyển hướng quá nhiều lần" | App không tin Cloudflare Tunnel nên tưởng đang là http | `ReverseProxy__KnownProxies__0` phải đúng gateway ở bước 2; `docker compose up -d` lại |
| Container dừng ngay, log có "App:BaseUrl bắt buộc…" hoặc "Turnstile đang trống…" | Thiếu cấu hình bắt buộc khi chạy thật | Điền đủ trong `.env` |
| Mở tên miền thấy lỗi 400 (Bad Request) | `AllowedHosts` khác tên miền đang dùng | Sửa `AllowedHosts` cho đúng tên miền |
| Log có "Login failed" / "server was not found" | Sai mật khẩu SQL hoặc app không cùng mạng với SQL | Kiểm `ConnectionStrings__Default`, `SQL_NETWORK` |
| Người đăng ký không nhận được thư | Thiếu/sai SMTP | Kiểm `Smtp__*`; log có dòng "Chưa cấu hình SMTP" |
| Google báo `redirect_uri_mismatch` | Chưa thêm địa chỉ server | Thêm `https://<tên-miền>/signin-google` ở Google Cloud |

## Server đã có Rekal (cách đang dùng cho mindsprint.rekal.id.vn)

Server đã có .NET 10, nginx, Cloudflare Tunnel và container SQL Server `rekal-sql` của Rekal. MindSprint chạy giống Rekal (dịch vụ systemd `mindsprint`, cổng 127.0.0.1:5002, nginx 127.0.0.1:8082), dùng chung SQL Server nhưng **database `MindSprintAI` + tài khoản SQL `mindsprint_app` riêng**; Turnstile, email Admin, thông tin pháp lý và Gmail chép từ cấu hình Rekal ngay trên server. Cấu hình bí mật nằm ở `/etc/mindsprint` (chỉ root đọc).

1. Trên máy dev (thư mục gốc repo), tạo bản build và công cụ tạo bảng:
   ```bash
   dotnet publish MindSprintAI_ASP.NET_MVC/MindSprintAI_ASP.NET_MVC.csproj -c Release -r linux-x64 --self-contained false -o out/app
   rm -rf out/app/App_Data out/app/appsettings.Development.json && tar -czf out/mindsprint-app.tar.gz -C out/app .
   dotnet ef migrations bundle --project MindSprintAI_ASP.NET_MVC/MindSprintAI_ASP.NET_MVC.csproj --target-runtime linux-x64 --output out/efbundle --force   # --target-runtime, KHÔNG phải --runtime (16/11: --runtime ra tệp Windows)
   scp out/mindsprint-app.tar.gz out/efbundle deploy/setup-mindsprint.sh james@<server>:mindsprint-deploy/
   ```
2. Trên server: `cd ~/mindsprint-deploy && sudo bash setup-mindsprint.sh` (chạy lại y hệt khi cập nhật bản mới; giữ nguyên dữ liệu và cấu hình).
3. Một lần trên trang Cloudflare: **Tunnel → Public hostname** `mindsprint.rekal.id.vn` → `HTTP` `127.0.0.1:8082`; **Turnstile** → widget của Rekal → thêm hostname `mindsprint.rekal.id.vn`.
4. Sửa cấu hình: `sudo nano /etc/mindsprint/mindsprint.env` (hoặc `secrets.env` cho Gmail/Google) rồi `sudo systemctl restart mindsprint`. Xem log: `sudo journalctl -u mindsprint -n 50`.
5. Bài làm theo đề: script tự cài LibreOffice (`libreoffice-writer-nogui` + phông Liberation/Carlito/Caladea) để đổi đề Word sang PDF, và đặt nginx `client_max_body_size 25m` (tệp tối đa 20 MB). Tệp đề và bài nộp nằm trong `/var/www/mindsprint/App_Data/assignment-files` (script giữ nguyên `App_Data` khi cập nhật).
6. Khi có lỗi cần nhờ người khác xem: chép `deploy/collect-diagnostics.sh` lên server rồi `bash collect-diagnostics.sh` (thêm `sudo` để xem cả container SQL Server). Tạo `~/mindsprint-diagnostics/<thời-gian>.tar.gz` gồm trạng thái dịch vụ, cổng, `/healthz` `/readyz`, log app/nginx/cloudflared đã **che mật khẩu, khóa, token, email** (không đọc tệp `.env`), kèm `summary.txt` gợi ý nguyên nhân. `--self-test` kiểm việc che bằng bí mật giả.
