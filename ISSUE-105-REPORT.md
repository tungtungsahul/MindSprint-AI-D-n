# Báo cáo kết quả Issue #105 — [Tuần 1][Laptop] Chốt baseline và bàn giao phần self-host đang có

* **Người thực hiện:** `phandien665-prog`
* **Người phối hợp / Chủ trì issue:** `tungtungsahul` / `phandien665-prog`
* **Người review:** `minhsonle30122007-png`
* **Nhánh:** `dien/issue-105-selfhost-baseline`
* **Pull Request:** https://github.com/tungtungsahul/MindSprint-AI-D-n/pulls
* **Ngày hoàn thành:** 10/11/2026 (Hạn chốt kế hoạch: 20:00 11/11/2026)
* **Milestone:** Sprint 4 - Tuần 4: Kiểm thử, triển khai, đóng gói
* **Kế hoạch laptop:** [Kế hoạch laptop #92](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/92)
* **Issue:** https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/105

---

## 1. Tóm tắt kết quả nghiệm thu

Đã hoàn thành xây dựng và kiểm chứng toàn diện bộ baseline self-host chạy Docker trên loopback laptop tại **`http://127.0.0.1:8080`**:

| STT | Thành phần | Trạng thái | Chi tiết kỹ thuật |
|:---:|---|:---:|---|
| 1 | **Dockerfile.selfhost** | **✅ PASS** | Multi-stage build .NET 8 SDK + ASP.NET Core 8 runtime, phục vụ song song frontend tĩnh (`wwwroot/`) và Web API tại cổng 8080. |
| 2 | **docker-compose.selfhost.yml** | **✅ PASS** | Điều phối 2 container: `mindsprint-selfhost-db` (PostgreSQL 17-alpine) và `mindsprint-selfhost-web`. Bind loopback `127.0.0.1:8080:8080`, cổng DB được cô lập trong mạng bridge nội bộ theo Issue #118. |
| 3 | **.env.selfhost.example** | **✅ PASS** | Mẫu cấu hình an toàn, không chứa secret thật, có hướng dẫn cụ thể cho các thành viên. |
| 4 | **Chống rò rỉ secret** | **✅ PASS** | Cập nhật `.gitignore` và `.dockerignore` chặn triệt để `.env*`, `.git`, file DB, logs, không để lọt bí mật vào image hoặc repo. |
| 5 | **SecurityConfiguration (C#)** | **✅ PASS** | Bổ sung hàm `IsSelfHost()`, chấp nhận môi trường Production chạy trên localhost/loopback mà không đòi hỏi CORS domain HTTPS cloud. |
| 6 | **Static Hosting & Health Checks** | **✅ PASS** | Bổ sung `ServeFrontend` (`UseDefaultFiles`, `UseStaticFiles`), endpoint `/healthz` (liveness), `/readyz` (readiness kiểm tra kết nối DB timeout 3s trả 503 khi lỗi). |
| 7 | **Frontend api.js Dynamic Origin** | **✅ PASS** | Tự động nhận diện cổng 8080 để trỏ API về `window.location.origin`, tự xóa cache `api-base` cũ trỏ nhầm về 5000 (#108, #110). |
| 8 | **Bộ scripts tự động hóa** | **✅ PASS** | `scripts/selfhost-up`, `scripts/selfhost-down`, `scripts/smoke-selfhost` (hỗ trợ cả PowerShell và Bash). |
| 9 | **Tài liệu hướng dẫn baseline** | **✅ PASS** | `docs/deployment/SELFHOST_BASELINE.md` đặc tả kiến trúc và cách thức các thành viên gối đầu làm tiếp #106–#119. |

---

## 2. Bằng chứng kiểm thử (Verification & Evidence)

### 2.1. Kiểm thử Unit Test C# — SecurityChecks (18/18 PASS)
```
PASS: production accepts configured remote database
PASS: production rejects localhost database
PASS: missing database configuration fails clearly
PASS: production accepts sufficiently long JWT key
PASS: missing JWT key fails clearly
PASS: short JWT key fails clearly
PASS: production rejects placeholder JWT key
PASS: production uses explicit HTTPS CORS allowlist
PASS: production rejects HTTP CORS origin
PASS: missing production CORS allowlist fails clearly
PASS: development retains local HTTP CORS origin
PASS: self-host in production allows same-origin without CORS cloud domain
PASS: self-host in production accepts local HTTP origin
PASS: self-host accepts container internal db host
PASS: production demo endpoint returns 404 without creating a fixed-password account
PASS: development demo login remains available
PASS: demo response does not disclose a password
PASS: production rejects fixed demo credentials through regular login
Security checks: 18 passed. SQLite in memory; no production data or provider calls.
```

### 2.2. Kiểm thử Khởi tạo Dữ liệu — SeedChecks (5/5 PASS)
```
PASS Fresh database contains exactly one system card per seed ExternalId
PASS Seeding preserves personal cards
PASS Seeding with demo disabled adds no shared account
PASS Repeated seeding does not duplicate system cards
PASS Repeated seeding leaves personal content intact
PASS 5 seed checks; 1021 input rows, 955 unique system cards
```

### 2.3. Kiểm thử Khói Tự động — Smoke Test (7/7 PASS)
Chạy script `scripts/smoke-selfhost.ps1`:
```
=== Bat dau Smoke Test tren http://127.0.0.1:8080 ===
  [PASS] Liveness check /healthz tra ve status ok
  [PASS] Readiness check /readyz ket noi database thanh cong
  [PASS] Frontend root (/) tra ve ma 200 va chua MindSprint
  [PASS] Asset /api.js tra ve ma 200 dung noi dung
  [PASS] Asset /style.css tra ve ma 200
  [PASS] API route khong ton tai tra ve ma 404 (khong tra ve HTML)
  [PASS] API /api/flashcards bao ve dung chuan Auth (ma 401)
-------------------------------------------
Ket qua: 7 pass, 0 fail
```

---

## 3. Hướng dẫn tái lập (Reproduction Commands)

### 3.1. Thiết lập và khởi động
```powershell
# 1. Checkout nhánh baseline
git checkout dien/issue-105-selfhost-baseline

# 2. Khởi tạo cấu hình môi trường
Copy-Item .env.selfhost.example .env.selfhost

# 3. Khởi động bằng script tự động
powershell -File scripts/selfhost-up.ps1

# 4. Chạy smoke test kiểm chứng
powershell -File scripts/smoke-selfhost.ps1
```

### 3.2. Dừng hệ thống
```powershell
powershell -File scripts/selfhost-down.ps1
```

---

## 4. Bàn giao cho các nhiệm vụ tiếp theo

* **Issue #107 & #113 (Phan Diễn):** Đã có sẵn `Dockerfile.selfhost` và smoke test để đo thời gian build từ clean source và đo tài nguyên.
* **Issue #106 & #109 (Minh Sơn):** Đã có cấu hình mẫu `.env.selfhost.example` và cơ chế `IsSelfHost`.
* **Issue #108 & #110 (Đình Hùng):** Đã có frontend serving chung origin và cơ chế tự làm sạch cache API.
* **Issue #111 & #99 (Tuấn Linh):** Đã có named volume `mindsprint_selfhost_pgdata` để viết kịch bản sao lưu/phục hồi.
* **Issue #112 & #118 (Minh Chiến):** Đã có endpoint `/healthz`, `/readyz` và cô lập cổng PostgreSQL.
