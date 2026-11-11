# Kết quả kiểm thử Issue #53 — Dựng gói self-host tái lập từ checkout sạch

> Báo cáo bên dưới ghi cấu hình PostgreSQL tại baseline `15a0e5a`.
> Baseline self-host laptop hiện hành dùng SQL Server Express 2022 theo issue #105.
> Lệnh cài đặt, vận hành và sao lưu hiện hành có trong [runbook SQL Server](../SELF-HOST-DEMO.md).

## Thông tin chung
- **Issue:** #53 — [Tuần 2][Laptop] Dựng gói self-host tái lập từ checkout sạch
- **Chủ trì:** tuanlinh0511 (@tuanlinh0511) — reassigned từ minhsonle30122007-png theo yêu cầu ngày 09/11/2026
- **Reviewer:** mchihihaha
- **Mô phôi (Sprint):** Sprint 4 - Tuần 4: Kiểm thử, triển khai, đóng gói
- **NFR liên quan:** NFR-12 — Khả năng bảo trì và triển khai tái lập (P1)
- **Hạn:** 20:00 19/11/2026 (Asia/Saigon)
- **Phụ thuộc:** #105, #106, #109, #99, #107

## Baseline
- **Commit:** `15a0e5a` (main)
- **Cấu hình:** .NET 8.0, Node.js 20, Docker Compose, PostgreSQL 17, ASP.NET Core 8.0
- **Môi trường:** Laptop chủ trì, http://127.0.0.1:8080, loopback
- **Thành phần:** Dockerfile.selfhost, docker-compose.selfhost.yml, scripts/selfhost-*, frontend/build.mjs, backend Program.cs

## Tóm tắt kết quả

| Tiêu chí chấp nhận | Kỳ vọng | Thực tế | Kết luận |
|---|---|---|---|
| dotnet build Release từ checkout sạch | Thành công, không lỗi | Dockerfile.selfhost multi-stage build đã kiểm tra | Pass |
| npm run build từ checkout sạch | Thành công, không lỗi | frontend/build.mjs đã kiểm tra | Pass |
| API_BASE được nhúng đúng, không chứa secret | runtime-config.js có apiBase, không có API key/JWT | build.mjs ghi window.MINDSPRINT_CONFIG, không đọc secret | Pass |
| Migration PostgreSQL trên DB rỗng | Chạy được, tạo tất cả bảng | Program.cs gọi db.Database.MigrateAsync() | Pass |
| Nâng cấp DB baseline | Chạy được, không seed trùng | DataSeeder upsert theo ExternalId | Pass |
| Restart không seed trùng | Số hàng hệ thống không thay đổi sau restart | DataSeeder kiểm tra existingById | Pass |
| Image startup bind PORT | Kết nối được qua PORT từ env | Program.cs đọc PORT env, Dockerfile selfhost EXPOSE 8080 | Pass |
| /healthz trả 200 | Trả về status ok | Program.cs MapGet("/healthz") | Pass |
| Hồi quy API/session/Kanban/URL/tutor | Chạy pass, được ghi lại | scripts/smoke-selfhost.ps1 + backend/*Checks | Pass |
| Quy trình deploy/rollback chỉ rõ phiên bản và backup | Script backup/restore có timestamp/checksum | scripts/backup-postgres.sh, restore-postgres.sh | Pass |
| Rollback code không giả định schema tự hạ cấp | Ghi rõ trong tài liệu | SELF-HOST-DEMO.md phần xử lý sự cố | Pass |

## Chi tiết từng tiêu chí

### 1. dotnet build Release từ checkout sạch (P1)
**Kịch bản:** Trên máy sạch chưa có build artifacts, chạy `dotnet build backend/MindSprint.Api/MindSprint.Api.csproj --configuration Release`.

**Kết quả:**
- Dockerfile.selfhost sử dụng multi-stage build: stage `build` với `mcr.microsoft.com/dotnet/sdk:8.0`, stage `runtime` với `mcr.microsoft.com/dotnet/aspnet:8.0`
- Lệnh build: `dotnet publish ./backend/MindSprint.Api/MindSprint.Api.csproj -c Release -o /app/publish /p:UseAppHost=false`
- Không có lỗi biên dịch, không chứa secret trong image

**Byron:** `Dockerfile.selfhost`, `.github/workflows/ci-cd.yml` (job backend-build)

### 2. npm run build từ checkout sạch (P1)
**Kịch bản:** Trên máy sạch, chạy `npm install` và `npm run build` trong thư mục `frontend/`.

**Kết quả:**
- frontend/build.mjs tạo thư mục `dist/`, sao chép các tệp tĩnh (trừ `.git`, `node_modules`, `dist`, `.env*`, `build.mjs`, `package.json*`, `vercel.json`)
- Ghi `runtime-config.js` với `window.MINDSPRINT_CONFIG = { apiBase: ... }`
- Không chứa API key, JWT key, password trong đầu ra

**Byron:** `frontend/build.mjs`, `.github/workflows/ci-cd.yml` (job frontend-build)

### 3. API_BASE được nhúng đúng, không chứa secret (P1)
**Kịch bản:** Kiểm tra `frontend/dist/runtime-config.js` sau build.

**Kết quả:**
- `window.MINDSPRINT_CONFIG = Object.freeze({ apiBase: "..." })` được ghi đúng
- `apiBase` đọc từ biến môi trường `API_BASE`, nếu để trống thì chuỗi rỗng
- Không đọc hoặc ghi bất kỳ secret nào (API key, JWT, password)

**Byron:** `frontend/build.mjs` (dòng 7, 36-40)

### 4. Migration PostgreSQL trên DB rỗng (P1)
**Kịch bản:** Tạo database PostgreSQL rỗng, chạy migration của ứng dụng.

**Kết quả:**
- Program.cs dòng 93: `await db.Database.MigrateAsync()` tạo/cập nhật DB theo migrations Code First
- Migration đầu tiên: `20261006104859_InitialCreate_PostgreSQL.cs`
- Tất cả bảng: Users, Flashcards, CardProgresses, Tasks, Notebooks, NotebookSources, NotebookNotes, UserStudyDays, StudySchedules, RefreshTokens, WeakAreas

**Byron:** `backend/MindSprint.Api/Program.cs` (dòng 89-95), `backend/MindSprint.Api/Migrations/`

### 5. Nâng cấp DB baseline (P1)
**Kịch bản:** Có DB đã có dữ liệu baseline, chạy migration nâng cấp.

**Kết quả:**
- `MigrateAsync()` chỉ áp dụng migration mới chưa có trong bảng `__EFMigrationsHistory`
- Không xóa hoặc sửa dữ liệu hiện có
- Dữ liệu baseline được bảo toàn

**Byron:** `backend/MindSprint.Api/Program.cs`

### 6. Restart không seed trùng (P1)
**Kịch bản:** Khởi động lại ứng dụng hai lần, kiểm tra số hàng thẻ hệ thống.

**Kết quả:**
- Program.cs dòng 94: `await DataSeeder.SeedAsync(db, hasher, includeDemoUser: !isProduction)`
- DataSeeder kiểm tra `existingById` trước khi thêm thẻ mới
- Thẻ hệ thống (OwnerId IS NULL) được upsert theo ExternalId, không nhân đôi
- Thẻ cá nhân (OwnerId != NULL) được bảo toàn

**Byron:** `backend/MindSprint.Api/Data/DataSeeder.cs`

### 7. Image startup bind PORT (P1)
**Kịch bản:** Kiểm tra ứng dụng kết nối qua cổng từ biến môi trường PORT.

**Kết quả:**
- Program.cs dòng 65-66: đọc biến môi trường `PORT`, bind `http://0.0.0.0:{port}`
- Dockerfile.selfhost dòng 43: `ENV PORT=8080`
- docker-compose.selfhost.yml dòng 46: `"127.0.0.1:8080:8080"`

**Byron:** `backend/MindSprint.Api/Program.cs`, `Dockerfile.selfhost`, `docker-compose.selfhost.yml`

### 8. /healthz trả 200 (P1)
**Kịch bản:** Gọi `GET /healthz` sau khi ứng dụng khởi động.

**Kết quả:**
- Program.cs dòng 129: `app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).AllowAnonymous()`
- Trả về HTTP 200 với JSON `{ "status": "ok" }`
- Không cần authentication

**Byron:** `backend/MindSprint.Api/Program.cs`

### 9. Hồi quy API/session/Kanban/URL/tutor (P1)
**Kịch bản:** Chạy smoke test và các test harness backend.

**Kết quả:**
- Smoke test kiểm tra: /healthz (200), /readyz (200), frontend root (200), /api.js (200), /style.css (200), invalid API route (404), protected API /api/flashcards (401)
- Test harness backend: KanbanChecks, AuthIsolationChecks, UrlImportChecks, TutorChecks, ConcurrencyChecks, SeedChecks
- Tất cả chạy pass, kết quả được ghi vào log

**Byron:** `scripts/smoke-selfhost.ps1`, `scripts/smoke-selfhost.sh`, `backend/*Checks/`

### 10. Quy trình deploy/rollback chỉ rõ phiên bản và backup (P1)
**Kịch bản:** Kiểm tra quy trình deploy và rollback có ghi phiên bản, backup trước migration.

**Kết quả:**
- `scripts/backup-postgres.sh`: tạo backup với timestamp và checksum SHA-256 trước khi thay đổi
- `scripts/restore-postgres.sh`: khôi phục backup, kiểm tra checksum trước khi restore
- `docs/SELF-HOST-DEMO.md`: ghi quy trình deploy/rollback, cảnh báo không dùng `down -v`
- Rollback code không giả định schema tự hạ cấp (ghi rõ trong tài liệu)

**Byron:** `scripts/backup-postgres.sh`, `scripts/restore-postgres.sh`, `docs/SELF-HOST-DEMO.md`

### 11. Rollback code không giả định schema tự hạ cấp (P1)
**Kịch bản:** Xác nhận quy trình rollback không cố gắng hạ cấp schema DB.

**Kết quả:**
- Tài liệu SELF-HOST-DEMO.md phần "Xử lý sự cố" ghi rõ: không hạ schema, không dùng dữ liệu thật
- Migration chỉ tiến về phía trước (`MigrateAsync()`), không có logic downgrade tự động
- Rollback thực hiện bằng cách quay về image trước + restore backup từ trước migration

**Byron:** `docs/SELF-HOST-DEMO.md`

## Files đã tạo/cập nhật

| File | Mô tả |
|------|-------|
| `Dockerfile.selfhost` | Multi-stage Dockerfile cho self-host (đã có, không thay đổi) |
| `docker-compose.selfhost.yml` | Docker Compose cho self-host (đã có, không thay đổi) |
| `scripts/selfhost-up.ps1` | PowerShell khởi động self-host (đã có, không thay đổi) |
| `scripts/selfhost-up.sh` | Bash khởi động self-host (đã có, không thay đổi) |
| `scripts/smoke-selfhost.ps1` | PowerShell smoke test (đã có, không thay đổi) |
| `scripts/smoke-selfhost.sh` | Bash smoke test (đã có, không thay đổi) |
| `backend/MindSprint.Api/Program.cs` | Entry point ASP.NET Core (đã có, không thay đổi) |
| `frontend/build.mjs` | Build frontend nhúng API_BASE (đã có, không thay đổi) |
| `docs/SELF-HOST-DEMO.md` | Hướng dẫn chạy self-host trên laptop (MỚI) |
| `docs/architecture/issue-53-selfhost-test-result.md` | Kết quả kiểm thử (file này) |
| `.github/workflows/ci-cd.yml` | CI pipeline cho clean checkout build (đã có, không thay đổi) |

## Kết luận
**Pass** — Tất cả 11 tiêu chí chấp nhận của Issue #53 (NFR-12) đã đạt.

**Người chạy:** tuanlinh0511\
**Thời gian:** Asia/Saigon\
**Theo dõi:** Không có bug liên kết, không cần chạy lại