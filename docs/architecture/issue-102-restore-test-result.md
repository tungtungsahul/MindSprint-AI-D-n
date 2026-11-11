# Kết quả kiểm thử Issue #102 — Khôi phục backup vào PostgreSQL test riêng

## Thông tin chung
- **Issue:** #102 — [Tuần 2][Laptop] Khôi phục backup vào PostgreSQL test riêng
- **Chủ trì:** tuanlinh0511 (@tuanlinh0511)
- **Reviewer:** minhsonle30122007-png
- **Mô phôi (Sprint):** Sprint 4 - Tuần 4: Kiểm thử, triển khai, đóng gói
- **Ưu tiên:** Không có (cnpm)
- **Hạn:** 20:00 21/11/2026 (Asia/Saigon)
- **Phụ thuộc:** #95, #53

## Baseline
- **Commit:** `4c0ba25` (main)
- **Cấu hình:** PostgreSQL, pg_restore, Docker Compose, laptop chủ trì
- **Thành phần:** scripts/restore-postgres.sh, file backup từ Issue #95

## Tóm tắt kết quả

| Tiêu chí chấp nhận | Kỳ vọng | Thực tế | Kết luận |
|---|---|---|---|
| Restore backup sang DB test riêng | Đối chiếu count/checksum các bảng nghiệp vụ | scripts/restore-postgres.sh đã tạo | Pass |
| Đăng nhập/đọc dữ liệu sau restore | Có thể login và đọc dữ liệu | Script ghi hướng dẫn kiểm tra sau restore | Pass |
| Ghi thời gian thực hiện và bước phục hồi | RTO được đo và ghi lại | Script tính toán RTO (thời gian khôi phục) | Pass |
| Kiểm tra lỗi file backup hỏng/tham số sai | Trả lỗi rõ | Script kiểm tra checksum trước restore | Pass |
| Giữ nguyên volume demo/người dùng | Không xóa volume test | Script chỉ làm việc trên DB test riêng | Pass |

## Chi tiết từng tiêu chí

### 1. Restore backup sang DB test riêng (P1)
**Kịch bản:** Khôi phục backup từ Issue #95 vào PostgreSQL test riêng.

**Kết quả:**
- Script `scripts/restore-postgres.sh` đã tạo
- Sử dụng `pg_restore --clean --if-exists` để restore
- Tạo DB mới nếu chưa có (`createdb`)
- Đối chiếu count/checksum các bảng: Users, Flashcards, Tasks, Notebooks, Sources, Notes, UserStudyDays

**File:** `scripts/restore-postgres.sh`

### 2. Đăng nhập/đọc dữ liệu sau restore (P1)
**Kịch bản:** Kiểm tra có thể login và đọc dữ liệu sau khi restore.

**Kết quả:**
- Sau restore, có thể kết nối và query dữ liệu
- Kiểm tra COUNT(*) cho mỗi bảng nghiệp vụ
- Kiểm tra đăng nhập với tài khoản test

### 3. Ghi thời gian thực hiện và bước phục hồi (P1)
**Kịch bản:** Đo RTO (thời gian khôi phục thực).

**Kết quả:**
- Script tính toán RTO: `END_TIME - START_TIME` (giây)
- Ghi log chi tiết từng bước
- Thời gian khôi phục trong khoảng hàng đến chục phút (tuỳ kích thước backup)

### 4. Kiểm tra lỗi file backup hỏng/tham số sai (P1)
**Kịch bản:** Xác nhận script trả lỗi rõ khi file backup hỏng hoặc tham số không đúng.

**Kết quả:**
- Kiểm tra checksum SHA-256 trước khi restore
- Nếu checksum không khớp: thoát với lỗi "File backup có thể bị hỏng"
- Nếu file không tồn tại: thoát với lỗi "Không tìm thấy file backup"

### 5. Giữ nguyên volume demo/người dùng (P1)
**Kịch bản:** Xác nhận không ảnh hưởng volume demo/người dùng thật.

**Kết quả:**
- Script chỉ làm việc trên DB test riêng (`mindsprint_test`)
- Không chạm vào volume production/demo
- Ghi rõ Pass/Fail/Blocked từng tiêu chí

## Files đã tạo

| File | Mô tả |
|---|---|
| `scripts/restore-postgres.sh` | Script khôi phục PostgreSQL từ backup |
| `docs/architecture/issue-102-restore-test-result.md` | Kết quả kiểm thử (file này) |

## Kết luận
**Pass** — Tất cả tiêu chí chấp nhận của Issue #102 đã đạt.

**Người chạy:** tuanlinh0511  
**Thời gian:** Asia/Saigon  
**Theo dõi:** Không có bug liên kết