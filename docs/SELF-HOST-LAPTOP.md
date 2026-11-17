# Self-host trên laptop

Gói này chạy frontend, ASP.NET API và PostgreSQL bằng Docker Compose. Chỉ frontend được publish ra host, tại `http://127.0.0.1:8080`; Nginx proxy `/api` và `/healthz` tới API cùng origin. PostgreSQL chỉ nằm trong network nội bộ của Compose và dữ liệu được giữ trong named volume `postgres_data`.

## Yêu cầu và cấu hình

- Docker Desktop có hỗ trợ Docker Compose v2.
- Đủ dung lượng đĩa cho image và database; lần build đầu cần Internet để tải image/package.
- Không cần cài .NET, Node.js hoặc PostgreSQL trên host.

Từ PowerShell tại thư mục gốc repo:

```powershell
Copy-Item .env.example .env
$bytes = New-Object byte[] 48
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($bytes)
$rng.Dispose()
$jwt = [Convert]::ToBase64String($bytes)
$password = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
(Get-Content .env) -replace '^JWT_KEY=.*', "JWT_KEY=$jwt" -replace '^POSTGRES_PASSWORD=.*', "POSTGRES_PASSWORD=$password" | Set-Content .env
```

Giữ `.env` riêng tư, không gửi kèm log/artifact. Gemini và Google Sign-In là tùy chọn; để trống thì các tích hợp đó không hoạt động.

## Build, chạy và smoke test

Ghi nhận revision source nếu checkout có Git metadata, sau đó chọn tag image ổn định:

```powershell
$revision = git rev-parse --short=12 HEAD 2>$null
if ($LASTEXITCODE -eq 0) {
	git rev-parse HEAD
	$env:IMAGE_TAG = $revision
} else {
	$env:IMAGE_TAG = 'local'
	Write-Warning 'No Git metadata found; using IMAGE_TAG=local.'
}
docker compose build
docker compose up -d
docker compose ps
./scripts/self-host-smoke.ps1
```

Compose đợi PostgreSQL healthy rồi mới start API. API chạy EF Core migrations và seed khi khởi động; lần đầu có thể cần thêm thời gian để hoàn tất. `/healthz` kiểm tra API process, trong khi health check `pg_isready` xác nhận database; cả hai phải healthy trước smoke test. Đăng ký tài khoản test riêng qua giao diện để kiểm tra luồng đăng nhập và đọc/ghi dữ liệu. Không dùng dữ liệu cá nhân trong nghiệm thu.

Ghi ID thật của image và checksum cấu hình để đối chiếu lần chạy:

```powershell
docker image inspect --format '{{.Id}}' "mindsprint-api:$env:IMAGE_TAG"
docker image inspect --format '{{.Id}}' "mindsprint-frontend:$env:IMAGE_TAG"
Get-FileHash compose.yaml, frontend/Dockerfile, frontend/nginx.conf, backend/MindSprint.Api/Dockerfile -Algorithm SHA256
```

`IMAGE_TAG=local` có thể dùng khi chưa chọn revision. Tag theo commit giúp lưu image cũ để rollback; tag không thay cho image ID/digest, vì vậy ghi cả hai vào biên bản chạy.

## Backup và khôi phục

Backup trước mỗi lần cập nhật có migration. Các lệnh sau chạy trong PowerShell, dùng file custom-format để có thể xác minh/restore bằng `pg_restore`:

```powershell
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
New-Item -ItemType Directory -Force .\backups | Out-Null
docker compose exec -T db sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc -f /tmp/mindsprint.dump'
docker compose cp db:/tmp/mindsprint.dump ".\backups\mindsprint-$stamp.dump"
Get-FileHash ".\backups\mindsprint-$stamp.dump" -Algorithm SHA256
```

Lưu backup ngoài Docker volume (và tốt nhất ngoài laptop) với quyền hạn phù hợp. Backup thủ công này không tạo lịch tự động hay bảo đảm RPO; chủ máy phải định kỳ chạy và xác minh checksum.

Khôi phục là thao tác phá hủy dữ liệu hiện tại. Dừng API, đưa dump vào container DB rồi restore vào database hiện tại; chỉ thực hiện sau khi đã xác nhận đúng file/database và có bản backup trước thao tác:

```powershell
docker compose stop api
docker compose cp .\backups\mindsprint-YYYYMMDD-HHMMSS.dump db:/tmp/restore.dump
docker compose exec -T db sh -c 'pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --clean --if-exists --no-owner /tmp/restore.dump'
docker compose start api
docker compose ps
./scripts/self-host-smoke.ps1
```

Để xác minh restore mà không đụng dữ liệu đang chạy, hãy tạo project/volume Compose riêng và restore vào database test cô lập trước. Ghi checksum dump, image IDs, migration hiện hành và kết quả smoke; không đưa nội dung dữ liệu hay secret vào báo cáo.

## Cập nhật và rollback

Build tag mới sau khi đã backup:

```powershell
$env:IMAGE_TAG = '<new-commit-short-sha>'
docker compose build
docker compose up -d
docker compose ps
./scripts/self-host-smoke.ps1
```

Rollback code bằng cách đặt `IMAGE_TAG` về tag image đã ghi trước đó rồi chạy `docker compose up -d api frontend`. Trước khi rollback, xác minh migration mới tương thích với phiên bản API cũ; EF Core không tự hạ schema khi quay code. Nếu không tương thích, dừng API và restore backup vào database test hoặc môi trường đã cô lập trước khi quyết định khôi phục dữ liệu. Không chạy rollback trên DB production chỉ dựa vào việc image cũ còn tồn tại.

## Giới hạn/bằng chứng

- Frontend/API/PostgreSQL chỉ bind trên loopback, không cấu hình TLS cho truy cập LAN/Internet. Không dùng gói này để public service.
- Same-origin deployment không bật CORS. Production API vẫn từ chối origin CORS HTTP; cấu hình HTTPS allowlist chỉ cần khi frontend được host ở origin khác.
- `healthz` xác nhận API process, không thay thế kiểm tra migration/data; Compose còn kiểm tra PostgreSQL readiness.
- URL importer có giới hạn runtime JavaScript theo `NotebookPageRenderer`; gói demo này không cam kết cài Chromium trong image.
- Tại thời điểm tạo hướng dẫn, môi trường làm việc chưa có Docker executable và checkout không có Git metadata. Vì vậy Docker build/start, migration trên PostgreSQL, volume persistence, backup/restore, image IDs và checksum của artifact runtime chưa được thực thi tại đây; cần ghi Pass/Fail từ lần chạy laptop, không xem các lệnh trên là bằng chứng đã chạy.