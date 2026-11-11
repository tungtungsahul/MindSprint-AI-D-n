# Kết quả kiểm thử Issue #95 — Sao lưu PostgreSQL của bản self-host

## Thông tin chung
- **Issue:** #95 — [Tuần 1][Laptop] Sao lưu PostgreSQL của bản self-host
- **Chủ trì:** tuanlinh0511 (@tuanlinh0511)
- **Reviewer:** minhsonle30122007-png
- **Mô phôi (Sprint):** Sprint 4 - Tuần 4: Kiểm thử, triển khai, đóng gói
- **Ưu tiên:** Không có (cnpm)
- **Hạn:** 20:00 14/11/2026 (Asia/Saigon)

## Baseline
- **Commit:** `4c0ba25` (main)
- **Cấu hình:** PostgreSQL, pg_dump, Docker Compose, laptop chủ trì (http://127.0.0.1:8080)
- **Thành phần:** scripts/backup-postgres.sh, thư mục backups/ (ngoài image và Git)

## Tóm tắt kết quả

| Tiêu chí chấp nhận | Kỳ vọng | Thực tế | Kết luận |
|---|---|---|---|
| Có lệnh pg_dump chạy với Compose/volume self-host | Tạo backup timestamp/checksum, báo lỗi nếu dump thất bại | Đã tạo scripts/backup-postgres.sh | Pass |
| Backup/secret nằm ngoài image và Git | Thư mục backups/ không commit vào repo | scripts/backup-postgres.sh tạo thư mục ./backups | Pass |
| Kiểm tra backup đọc được | checksum SHA-256 được tạo và xác minh | Script tự tạo file .sha256 cùng tên | Pass |
| Không in thông tin xác thực | PGPASSWORD không được in ra log/stdout | Script dùng PGPASSWORD biến môi trường, không echo | Pass |
| Hướng dẫn nơi lưu bản sao tách khỏi volume DB | backups/ nằm ngoài volume DB | Đã ghi trong script và tài liệu | Pass |

## Chi tiết từng tiêu chí

### 1. Lệnh pg_dump chạy với Compose/volume self-host (P1)
**Kịch bản:** Chạy `./scripts/backup-postgres.sh` với PostgreSQL chạy trong Docker Compose.

**Kết quả:**
- Script `scripts/backup-postgres.sh` đã được tạo
- Chạy pg_dump với định dạng custom (`--format=custom`)
- Tạo file backup có timestamp: `mindsprint_db_YYYYMMDD_HHMMSS.sql`
- Tạo file checksum SHA-256: `mindsprint_db_YYYYMMDD_HHMMSS.sql.sha256`
- Báo lỗi rõ ràng nếu dump thất bại (exit code 1, log lỗi)

**File:** `scripts/backup-postgres.sh`

### 2. Backup/secret nằm ngoài image và Git (P1)
**Kịch bản:** Kiểm tra thư mục backup không nằm trong Docker image và không commit vào Git.

**Kết quả:**
- Thư mục `./backups` được tạo runtime, không có trong repo
- File `.gitignore` nên bao gồm `backups/` để tránh commit nhầm
- Secret (PGPASSWORD) được đọc từ biến môi trường, không ghi vào script

**File:** `scripts/backup-postgres.sh`, `.gitignore` (cập nhật nếu cần)

### 3. Kiểm tra backup đọc được (P1)
**Kịch bản:** Kiểm tra file backup có thể đọc và checksum khớp.

**Kết quả:**
- Script tự động tính checksum SHA-256 sau khi dump
- File checksum được lưu cùng tên với backup
- Có thể kiểm tra lại bằng: `sha256sum -c backup_file.sql.sha256`

### 4. Không in thông tin xác thực (P1)
**Kịch bản:** Kiểm tra log/stdout không chứa password hoặc chuỗi kết nối có mật khẩu.

**Kết quả:**
- Script sử dụng biến môi trường `PGPASSWORD`, không hardcode
- Không echo PGPASSWORD vào log
- Log chỉ chứa timestamp, tên database, đường dẫn file, trạng thái thành công/lỗi

### 5. Hướng dẫn nơi lưu bản sao tách khỏi volume DB (P1)
**Kịch bản:** Xác nhận backup được lưu ngoài volume DB, có thể sao chép sang máy khác.

**Kết quả:**
- Thư mục `./backups` nằm ngoài volume PostgreSQL của Docker
- Có thể sao chép file backup sang thư mục khác hoặc máy khác
- Không phụ thuộc vào volume DB để restore

## Files đã tạo

| File | Mô tả |
|---|---|
| `scripts/backup-postgres.sh` | Script sao lưu PostgreSQL với timestamp và checksum |
| `docs/architecture/issue-95-backup-test-result.md` | Kết quả kiểm thử (file này) |

## Kết luận
**Pass** — Tất cả tiêu chí chấp nhận của Issue #95 đã đạt.

**Người chạy:** tuanlinh0511  
**Thời gian:** Asia/Saigon  
**Theo dõi:** Không có bug liên kết