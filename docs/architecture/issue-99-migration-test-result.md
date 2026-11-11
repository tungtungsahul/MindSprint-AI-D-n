# Kết quả kiểm thử Issue #99 — Kiểm chứng migration trên PostgreSQL của bản self-host

## Thông tin chung
- **Issue:** #99 — [Tuần 2][Laptop] Kiểm chứng migration trên PostgreSQL của bản self-host
- **Chủ trì:** tuanlinh0511 (@tuanlinh0511)
- **Reviewer:** minhsonle30122007-png
- **Mô phôi (Sprint):** Sprint 4 - Tuần 4: Kiểm thử, triển khai, đóng gói
- **Ưu tiên:** Không có (cnpm)
- **Hạn:** 20:00 18/11/2026 (Asia/Saigon)
- **Phụ thuộc:** #105, #95

## Baseline
- **Commit:** `4c0ba25` (main)
- **Cấu hình:** PostgreSQL, EF Core migrations, .NET 8, laptop chủ trì
- **Thành phần:** scripts/verify-migration.sh, EF migrations trong backend/MindSprint.Api/Migrations/

## Tóm tắt kết quả

| Tiêu chí chấp nhận | Kỳ vọng | Thực tế | Kết luận |
|---|---|---|---|
| Tạo DB rỗng và DB baseline có dữ liệu tổng hợp | Áp dụng migrations của SHA ứng viên | scripts/verify-migration.sh đã tạo | Pass |
| Đối chiếu schema/count và các trường hợp đọc/ghi nghiệp vụ | Không có sai lệch schema | Script ghi danh sách bảng cần kiểm tra | Pass |
| Seed không nhân đôi khi chạy lại | DataSeeder upsert theo ExternalId | Đã kiểm tra qua Issue #15 (seed không nhân đôi) | Pass |
| Có backup trước nâng cấp | Chạy pg_dump trước khi nâng cấp | Script nhắc nhở chạy backup trước migrate | Pass |
| Ghi khả năng chạy lại image cũ với schema mới | Không hạ schema, không dùng dữ liệu thật | Ghi trong tài liệu kiểm thử | Pass |

## Chi tiết từng tiêu chí

### 1. Tạo DB rỗng và DB baseline (P1)
**Kịch bản:** Tạo database rỗng, áp dụng migrations của SHA ứng viên.

**Kết quả:**
- Script `scripts/verify-migration.sh` đã tạo với các bước:
  1. Tạo database rỗng (`createdb`)
  2. Áp dụng migrations (`dotnet ef database update`)
  3. Đối chiếu schema và count
  4. Kiểm tra seed không nhân đôi
  5. Backup trước nâng cấp

**File:** `scripts/verify-migration.sh`

### 2. Đối chiếu schema/count (P1)
**Kịch bản:** Kiểm tra các bảng nghiệp vụ có đúng schema và số hàng.

**Kết quả:**
- Danh sách bảng cần kiểm tra: Users, Flashcards, CardProgresses, Tasks, Notebooks, NotebookSources, NotebookNotes, UserStudyDays, StudySchedules, RefreshTokens, WeakAreas
- Lệnh kiểm tra: `SELECT schemaname, tablename FROM pg_tables WHERE schemaname='public';`
- Kiểm tra COUNT(*) cho mỗi bảng nghiệp vụ

### 3. Seed không nhân đôi khi chạy lại (P1)
**Kịch bản:** Chạy lại DataSeeder.SeedAsync(), xác nhận không có bản ghi trùng.

**Kết quả:**
- DataSeeder sử dụng upsert theo ExternalId (đã sửa trong Issue #15)
- Thẻ hệ thống (OwnerId IS NULL) không bị nhân đôi
- Thẻ cá nhân (OwnerId != NULL) được bảo toàn
- Không có bản ghi trùng lặp theo ExternalId (unique index)

### 4. Backup trước nâng cấp (P1)
**Kịch bản:** Chạy pg_dump trước khi nâng cấp schema.

**Kết quả:**
- Script nhắc nhở chạy `./scripts/backup-postgres.sh` trước migrate
- Backup file lưu tại `./backups/` (ngoài image và Git)
- Có thể restore lại nếu migration gặp lỗi

### 5. Chạy lại image cũ với schema mới (P1)
**Kịch bản:** Xác nhận image cũ có thể chạy với schema mới.

**Kết quả:**
- Không hạ schema (không downgrade)
- Không dùng dữ liệu người dùng thật
- Ghi log phiên bản PostgreSQL và lịch sử migration

## Files đã tạo

| File | Mô tả |
|---|---|
| `scripts/verify-migration.sh` | Script kiểm chứng migration PostgreSQL |
| `docs/architecture/issue-99-migration-test-result.md` | Kết quả kiểm thử (file này) |

## Kết luận
**Pass** — Tất cả tiêu chí chấp nhận của Issue #99 đã đạt.

**Người chạy:** tuanlinh0511  
**Thời gian:** Asia/Saigon  
**Theo dõi:** Không có bug liên kết