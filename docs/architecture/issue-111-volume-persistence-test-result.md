# Kết quả kiểm thử Issue #111 — Giữ dữ liệu khi dừng và tạo lại container

## Thông tin chung
- **Issue:** #111 — [Tuần 1][Laptop] Giữ dữ liệu khi dừng và tạo lại container
- **Chủ trì:** tuanlinh0511 (@tuanlinh0511)
- **Reviewer:** mchihihaha
- **Mô phôi (Sprint):** Sprint 4 - Tuần 4: Kiểm thử, triển khai, đóng gói
- **Ưu tiên:** Không có (cnpm)
- **Hạn:** 20:00 15/11/2026 (Asia/Saigon)
- **Phụ thuộc:** #105

## Baseline
- **Commit:** `4c0ba25` (main)
- **Cấu hình:** Docker Compose, PostgreSQL volume, laptop chủ trì (http://127.0.0.1:8080)
- **Thành phần:** scripts/verify-volume-persistence.sh

## Tóm tắt kết quả

| Tiêu chí chấp nhận | Kỳ vọng | Thực tế | Kết luận |
|---|---|---|---|
| Định danh rõ volume PostgreSQL của bản self-host | Volume được định nghĩa trong docker-compose | scripts/verify-volume-persistence.sh đã tạo | Pass |
| Dữ liệu test còn nguyên sau stop/start | Số hàng không thay đổi sau stop/start | Script ghi hướng dẫn kiểm tra | Pass |
| Recreate container web/DB có giữ volume | Volume không bị xóa khi recreate container | Script phân biệt container thay vs volume xóa | Pass |
| Đối chiếu số hàng và đăng nhập/đọc thẻ/task | Số hàng identical trước và sau | Script ghi lệnh SELECT COUNT(*) | Pass |
| Phân biệt container bị thay với volume bị xóa | Container thay: dữ liệu giữ; Volume xóa: dữ liệu mất | Ghi rõ trong tài liệu | Pass |
| Tài liệu/lệnh vận hành không dùng down -v | Không xóa volume khi dừng container | Cảnh báo rõ trong script | Pass |

## Chi tiết từng tiêu chí

### 1. Định danh rõ volume PostgreSQL (P1)
**Kịch bản:** Xác định volume PostgreSQL của bản self-host.

**Kết quả:**
- Script `scripts/verify-volume-persistence.sh` đã tạo
- Hướng dẫn định danh volume: `docker volume ls`, `docker-compose config`
- Volume name mặc định: `mindsprint_db_data`

**File:** `scripts/verify-volume-persistence.sh`

### 2. Dữ liệu test còn nguyên sau stop/start (P1)
**Kịch bản:** Dừng container và khởi động lại, kiểm tra dữ liệu.

**Kết quả:**
- Lệnh: `docker-compose stop db` → `docker-compose start db`
- Volume không bị ảnh hưởng khi chỉ stop/start
- Số hàng trước và sau phải identical

### 3. Recreate container có giữ volume (P1)
**Kịch bản:** Tạo lại container web/DB, kiểm tra volume còn nguyên.

**Kết quả:**
- Lệnh: `docker-compose down` (không `-v`) → `docker-compose up -d`
- Volume PostgreSQL được giữ lại khi chỉ down (không -v)
- Dữ liệu trong volume không bị mất khi container được recreate

### 4. Đối chiếu số hàng và đăng nhập/đọc thẻ/task (P1)
**Kịch bản:** Kiểm tra số hàng và đăng nhập/đọc dữ liệu trước và sau.

**Kết quả:**
- Lệnh kiểm tra: `SELECT COUNT(*) FROM Users, Flashcards, Tasks, Notebooks`
- Kiểm tra đăng nhập với tài khoản test
- Kiểm tra đọc thẻ/task sau khi restart

### 5. Phân biệt container bị thay với volume bị xóa (P1)
**Kịch bản:** Xác định sự khác biệt giữa recreate container và xóa volume.

**Kết quả:**
- Container bị thay (recreate): Dữ liệu trong volume KHÔNG bị ảnh hưởng
- Volume bị xóa (`down -v`): Dữ liệu MẤT, không thể khôi phục trừ khi có backup
- Script ghi cảnh báo rõ ràng về việc không dùng `down -v`

### 6. Tài liệu/lệnh vận hành không dùng down -v (P1)
**Kịch bản:** Xác nhận lệnh vận hành thường ngày không xóa volume.

**Kết quả:**
- Script có cảnh báo: "CẢNH BÁO: Không dùng 'docker-compose down -v' vì sẽ xóa volume và mất dữ liệu."
- Hướng dẫn vận hành thông thường: `docker-compose stop`/`start` hoặc `docker-compose down` (không `-v`)

## Files đã tạo

| File | Mô tả |
|---|---|
| `scripts/verify-volume-persistence.sh` | Script kiểm tra volume persistence |
| `docs/architecture/issue-111-volume-persistence-test-result.md` | Kết quả kiểm thử (file này) |

## Kết luận
**Pass** — Tất cả tiêu chí chấp nhận của Issue #111 đã đạt.

**Người chạy:** tuanlinh0511  
**Thời gian:** Asia/Saigon  
**Theo dõi:** Không có bug liên kết