# Hướng dẫn chạy MindSprint AI Self-Host trên Laptop

## Mục đích

Tài liệu hướng dẫn dựng và chạy gói self-host (frontend + API + PostgreSQL) từ **checkout sạch** trên laptop chủ trì, mặc định truy cập qua `http://127.0.0.1:8080`. Tài liệu phục vụ Issue #53 (NFR-12 - Khả năng bảo trì và triển khai tái lập).

## Điều kiện tiên quyết

- Docker Desktop đã cài đặt và đang chạy
- .NET SDK 8.0 (nếu build thủ công không dùng Docker)
- Node.js 20 (nếu build frontend thủ công)
- Git đã clone repository về máy

## Cấu trúc tệp liên quan

| Tệp | Mục đích |
|-----|----------|
| `Dockerfile.selfhost` | Multi-stage Dockerfile build API + phục vụ frontend tĩnh trên cổng 8080 |
| `docker-compose.selfhost.yml` | Docker Compose chạy PostgreSQL + web self-host |
| `.env.selfhost.example` | Mẫu cấu hình môi trường (sao chép thành `.env.selfhost` trước khi chạy) |
| `scripts/selfhost-up.ps1` | PowerShell: khởi động self-host (build compose, chờ readyz) |
| `scripts/selfhost-up.sh` | Bash: khởi động self-host (build compose, chờ readyz) |
| `scripts/selfhost-down.ps1` | PowerShell: dừng container (giữ nguyên volume PostgreSQL) |
| `scripts/selfhost-down.sh` | Bash: dừng container (giữ nguyên volume PostgreSQL) |
| `scripts/smoke-selfhost.ps1` | PowerShell: kiểm thử khói (healthz, readyz, frontend, API auth) |
| `scripts/smoke-selfhost.sh` | Bash: kiểm thử khói (cùng nội dung PS1) |
| `scripts/backup-postgres.sh` | Bash: sao lưu PostgreSQL với timestamp + checksum (Issue #95) |
| `scripts/restore-postgres.sh` | Bash: khôi phục backup vào DB test riêng (Issue #102) |
| `scripts/verify-migration.sh` | Bash: kiểm chứng migration PostgreSQL (Issue #99) |
| `scripts/verify-volume-persistence.sh` | Bash: kiểm tra giữ dữ liệu khi dừng/tạo lại container (Issue #111) |
| `backend/MindSprint.Api/Program.cs` | Entry point ASP.NET Core: migration, seed, healthz, readyz, bind PORT |
| `frontend/build.mjs` | Build frontend, nhúng API_BASE vào runtime-config.js |
| `backend/MindSprint.Api/Dockerfile` | Dockerfile cho Render (cổng 10000) |

## Bước 1: Chuẩn bị tệp cấu hình

Sao chép tệp mẫu thành tệp thực (không commit `.env.selfhost` lên Git):

```powershell
# PowerShell
Copy-Item .env.selfhost.example .env.selfhost
```

```bash
# Bash
cp .env.selfhost.example .env.selfhost
```

Chỉnh sửa `.env.selfhost`:
- `POSTGRES_PASSWORD`: đặt mật khẩu PostgreSQL thật (ít nhất 12 ký tự)
- `JWT_KEY`: đặt khóa bí mật JWT (ít nhất 32 ký tự, không dùng chuỗi placeholder)
- `GEMINI_API_KEY`: để trống nếu không dùng tính năng AI live

> **QUAN TRỌNG:** `.env.selfhost` chứa mật khẩu/khoá thật, đã được đưa vào `.gitignore` và `.dockerignore`. Tuyệt đối không commit lên Git.

## Bước 2: Dựng và chạy từ checkout sạch

### Sử dụng PowerShell (khuyên dùng trên Windows)

```powershell
.\scripts\selfhost-up.ps1
```

Script sẽ:
1. Kiểm tra Docker đã cài đặt
2. Sao chép `.env.selfhost` từ mẫu nếu chưa có
3. Build Docker image (`docker compose --build`)
4. Khởi động PostgreSQL và web self-host
5. Chờ `/readyz` trả về `"ready"` (tối đa 60 giây)

### Sử dụng Bash (Git Bash, WSL, Linux)

```bash
./scripts/selfhost-up.sh
```

## Bước 3: Kiểm thử khói (smoke test)

Sau khi self-host sẵn sàng, chạy smoke test để xác minh các endpoint chính:

```powershell
.\scripts\smoke-selfhost.ps1
```

```bash
./scripts/smoke-selfhost.sh
```

Smoke test kiểm tra:
- `/healthz` trả về 200 (liveness)
- `/readyz` trả về 200 và database connected (readiness)
- Trang chủ `/` trả về 200 và chứa nội dung MindSprint
- Tài nguyên tĩnh `/api.js`, `/style.css` trả về 200
- Route API không tồn tại trả về 404 (không bị đọa thành HTML index)
- API `/api/flashcards` yêu cầu Auth trả về 401 (không để lộ dữ liệu)

## Bước 4: Truy cập ứng dụng

Mở trình duyệt và truy cập:

```
http://127.0.0.1:8080
```

- API và frontend chạy cùng origin (cùng cổng 8080)
- Liveness: `http://127.0.0.1:8080/healthz`
- Readiness: `http://127.0.0.1:8080/readyz`

## Bước 5: Dừng self-host

Dừng container nhưng **giữ nguyên volume PostgreSQL** để dữ liệu không mất:

```powershell
.\scripts\selfhost-down.ps1
```

```bash
./scripts/selfhost-down.sh
```

> **CẢNH BÁO:** Không dùng `docker compose down -v` vì sẽ xóa volume PostgreSQL và mất tất cả dữ liệu. Chỉ dùng `docker compose down` (không có cờ `-v`).

## Bước 6: Khởi động lại (dữ liệu giữ nguyên)

Volume PostgreSQL (`mindsprint_selfhost_pgdata`) được giữ nguyên khi chỉ dừng/khoi động lại container. Chạy lại `selfhost-up.ps1` hoặc `selfhost-up.sh`, dữ liệu test trước đó vẫn còn.

## Xác minh các tiêu chí NFR-12

| Tiêu chí | Cách xác minh |
|----------|--------------|
| dotnet build Release từ checkout sạch | `dotnet build backend/MindSprint.Api/MindSprint.Api.csproj -c Release` |
| npm run build từ checkout sạch | `npm run build` trong thư mục `frontend/` |
| API_BASE được nhúng đúng, không chứa secret | Kiểm tra `frontend/dist/runtime-config.js` có `apiBase`, không có API key/JWT |
| Migration chạy trên DB rỗng | `docker compose exec db psql -U postgres -d mindsprint -c "\dt"` sau khi up |
| Restart không seed trùng | Chạy `selfhost-up` hai lần, số hàng thẻ hệ thống không thay đổi |
| Image bind PORT và /healthz trả 200 | `curl -sf http://127.0.0.1:8080/healthz` |
| Hồi quy API/session/Kanban/URL/tutor | Chạy `scripts/smoke-selfhost.ps1` và các test harness trong `backend/*Checks/` |

## Xử lý sự cố

| Lỗi | Nguyên nhân có thể | Cách khắc phục |
|-----|-------------------|----------------|
| `/readyz` không sẵn sàng sau 60s | PostgreSQL chưa khởi động xong | Chờ thêm, hoặc chạy `docker compose -f docker-compose.selfhost.yml logs db` |
| `/healthz` trả 503 | Database không kết nối được | Kiểm tra `POSTGRES_PASSWORD` trong `.env.selfhost` khớp với compose |
| Frontend không tải | `SERVE_FRONTEND` không đúng | Kiểm tra `.env.selfhost` có `SERVE_FRONTEND=true` |
| Build Docker thất bại | Không có Docker hoặc lỗi mạng | Cài Docker Desktop, kiểm tra kết nối internet để pull image |

## Liên kết issue

- [Issue #53 — Dựng gói self-host tái lập từ checkout sạch](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/53)
- [Issue #99 — Kiểm chứng migration PostgreSQL](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/99)
- [Issue #95 — Sao lưu PostgreSQL](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/95)
- [Issue #102 — Khôi phục backup](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/102)
- [Issue #111 — Giữ dữ liệu khi dừng container](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/111)

## Người chịu trách nhiệm

Chủ trì: tuanlinh0511 (@tuanlinh0511) — reassigned từ minhsonle30122007-png theo yêu cầu ngày 09/11/2026

Reviewer: mchihihaha

Hạn bàn giao: 20:00 19/11/2026 (Asia/Saigon)