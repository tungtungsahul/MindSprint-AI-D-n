# Backup & Restore Runbook — MindSprint AI

## Overview
This runbook documents the backup and restore procedures for the PostgreSQL database used by MindSprint AI.

## RPO/RTO Targets
- **RPO (Recovery Point Objective):** ≤ 24 hours
- **RTO (Recovery Time Objective):** ≤ 60 minutes
- **Retention:** Minimum 7 daily backups

## Scripts
All scripts are located in `scripts/` directory and must be run on the laptop chủ trì (http://127.0.0.1:8080).

| Script | Mục đích | Issue |
|---|---|---|
| `scripts/backup-postgres.sh` | Sao lưu PostgreSQL với pg_dump, timestamp và checksum | #95 |
| `scripts/restore-postgres.sh` | Khôi phục backup vào PostgreSQL test riêng | #102 |
| `scripts/verify-migration.sh` | Kiểm chứng migration nâng cấp và dữ liệu sau restart | #99 |
| `scripts/verify-volume-persistence.sh` | Kiểm tra giữ dữ liệu khi dừng/tạo lại container | #111 |

## Backup Procedure

### Automated Daily Backup (PostgreSQL)
```bash
# Chạy script backup (tạo timestamp và checksum tự động)
./scripts/backup-postgres.sh [backup_dir] [db_name] [db_user] [db_host] [db_port]

# Ví dụ:
./scripts/backup-postgres.sh ./backups mindsprint postgres localhost 5432
```

Script sẽ:
- Tạo file backup: `mindsprint_db_YYYYMMDD_HHMMSS.sql`
- Tạo file checksum: `mindsprint_db_YYYYMMDD_HHMMSS.sql.sha256`
- Ghi log chi tiết vào `./backups/backup_YYYYMMDD_HHMMSS.log`
- Báo lỗi nếu dump thất bại (không in thông tin xác thực)

### Backup Schedule
- **Frequency:** Daily at 02:00 Asia/Saigon (UTC+7)
- **Retention:** 7 daily backups, 4 weekly backups, 3 monthly backups
- **Storage:** Thư mục `./backups/` (ngoài image và Git, không commit vào repo)
- **Access:** Giới hạn quyền truy cập, không lưu secret trong script/log

### Backup Verification Checklist
- [ ] Backup file tồn tại và kích thước > 0
- [ ] Checksum SHA-256 khớp (`sha256sum -c backup_file.sql.sha256`)
- [ ] `pg_restore --list` thành công không lỗi
- [ ] Backup chứa tất cả bảng cần thiết: Users, Flashcards, CardProgresses, Tasks, Notebooks, NotebookSources, NotebookNotes, UserStudyDays, StudySchedules, RefreshTokens, WeakAreas
- [ ] Số hàng khớp với expected baseline

## Restore Procedure

### Full Restore
```bash
# Chạy script restore (kiểm tra checksum tự động trước khi restore)
./scripts/restore-postgres.sh <backup_file> [db_name] [db_user] [db_host] [db_port]

# Ví dụ:
./scripts/restore-postgres.sh ./backups/mindsprint_db_20261110_143000.sql mindsprint_test postgres localhost 5432
```

Script sẽ:
1. Kiểm tra file backup tồn tại
2. Kiểm tra checksum SHA-256 (nếu có file .sha256)
3. Tạo database mới nếu chưa có
4. Chạy `pg_restore --clean --if-exists`
5. Tính toán và ghi thời gian khôi phục (RTO)

### Smoke Test After Restore
1. Login with test account
2. Verify flashcards load correctly
3. Verify Kanban tasks display properly
4. Verify notebook sources and notes are intact
5. Verify study stats match pre-backup state

## Migration Verification (Issue #99)
```bash
# Kiểm chứng migration trên PostgreSQL test riêng
./scripts/verify-migration.sh [db_name] [db_user] [db_host] [db_port]
```

Script sẽ hướng dẫn:
1. Tạo database rỗng
2. Áp dụng migrations (`dotnet ef database update`)
3. Đối chiếu schema và số hàng
4. Kiểm tra seed không nhân đôi khi chạy lại
5. Backup trước nâng cấp

## Volume Persistence (Issue #111)
```bash
# Kiểm tra giữ dữ liệu khi dừng/tạo lại container
./scripts/verify-volume-persistence.sh [compose_file] [db_service] [volume_name]
```

Script sẽ hướng dẫn:
1. Định danh volume PostgreSQL
2. Ghi số hàng trước khi dừng container
3. Dừng/khởi động lại container (không dùng `down -v`)
4. Kiểm tra số hàng sau khi khởi động lại
5. Phân biệt container thay (dữ liệu giữ) vs volume xóa (dữ liệu mất)

**CẢNH BÁO:** Không dùng `docker-compose down -v` vì sẽ xóa volume và mất dữ liệu.

## Data Scope
### Included in Backup
- All server-side data: Users, Flashcards (system + personal), CardProgresses, KanbanTasks, Notebooks, NotebookSources, NotebookNotes, NotebookGenerationCaches, UserStudyDays, StudySchedules, RefreshTokens, WeakAreas

### NOT Included in Backup (Client-Side Only)
- localStorage data (flashcard progress not yet synced)
- Service worker cache
- Browser session state
- Any data created offline that has not been synced to server

**Important:** Data stored only in localStorage is NOT recoverable from server backup. Users must sync data before relying on backup recovery.

## RPO/RTO Measurement
- **RPO:** Time between last successful backup and failure event (≤ 24h)
- **RTO:** Time from failure detection to full service restoration (≤ 60min)
- Measure from backup creation timestamp to restore completion timestamp

## Limitations
- Cloud demo instances may not have automated backup enabled by default
- Backup storage costs and retention policies depend on hosting provider
- Point-in-time recovery requires WAL archiving configuration
- Volume persistence chỉ áp dụng khi không dùng `docker-compose down -v`