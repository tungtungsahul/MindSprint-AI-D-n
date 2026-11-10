# 🚀 MindSprint AI - Tài liệu Baseline Self-Host trên Laptop

> **Nhiệm vụ:** [#105 — [Tuần 1][Laptop] Chốt baseline và bàn giao phần self-host đang có](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/105)  
> **Kế hoạch tổng thể:** [Kế hoạch laptop 09–22/11/2026 (#92)](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/92)  
> **Người thực hiện:** `@phandien665-prog` / `@tungtungsahul`  
> **Reviewer:** `@minhsonle30122007-png`  
> **Thời điểm chốt baseline:** 10/11/2026  
> **URL mục tiêu:** `http://127.0.0.1:8080` (loopback laptop)

---

## 1. Mục tiêu và Kiến trúc Baseline

Bản self-host laptop đưa toàn bộ hệ thống MindSprint AI vào môi trường Docker cục bộ, chạy trực tiếp trên laptop của nhóm mà không phụ thuộc vào cloud hosting bên ngoài (Render/Vercel) cho các nghiệp vụ CRUD, Spaced Repetition, Kanban và Quản lý tài khoản.

```
                     [ TRÌNH DUYỆT LAPTOP ]
                               │
               http://127.0.0.1:8080 (Loopback)
                               ▼
        ┌──────────────────────────────────────────────┐
        │       Container: mindsprint-selfhost-web     │
        │   (Port 8080:8080 trên 127.0.0.1 duy nhất)   │
        │                                              │
        │  ┌────────────────────┐ ┌──────────────────┐ │
        │  │ Frontend Tĩnh      │ │ ASP.NET Core API │ │
        │  │ (HTML/CSS/JS/Wasm) │ │ (REST Controllers│ │
        │  │ wwwroot/           │ │  & EF Core Npgsql)│ │
        │  └────────────────────┘ └──────────────────┘ │
        │           │                       │          │
        │           └────── Same Origin ────┘          │
        │           /healthz   /readyz                 │
        └──────────────────────┬───────────────────────┘
                               │ Docker Network (Bridge)
                               ▼
        ┌──────────────────────────────────────────────┐
        │       Container: mindsprint-selfhost-db      │
        │   PostgreSQL 17-alpine (Internal Port 5432)   │
        │   * Không mở port ra ngoài host (#118)       │
        │   * Volume: mindsprint_selfhost_pgdata       │
        └──────────────────────────────────────────────┘
```

---

## 2. Danh mục tệp bàn giao (Deliverables)

| Tệp / Đường dẫn | Mô tả nhiệm vụ |
|---|---|
| `Dockerfile.selfhost` | Multi-stage build (.NET 8 SDK + ASP.NET Core 8 runtime), đóng gói backend và sao chép frontend vào `wwwroot/`. Chạy Production ở cổng 8080. |
| `docker-compose.selfhost.yml` | Khai báo 2 dịch vụ: `db` (PostgreSQL 17-alpine) và `web` (ASP.NET Core + Frontend). Cấu hình healthcheck, volume và bind loopback 127.0.0.1:8080. |
| `.env.selfhost.example` | Mẫu biến môi trường an toàn, che secret thật, có chú thích chi tiết cho thành viên sao chép khi thiết lập. |
| `.dockerignore` & `.gitignore` | Loại bỏ hoàn toàn `.env*`, `.git`, file DB, logs, secrets, không để lọt vào context build hay image. |
| `backend/MindSprint.Api/SecurityConfiguration.cs` | Thêm hỗ trợ nhận diện môi trường Self-Host (`IsSelfHost`), cho phép cấu hình Production trên localhost mà không đòi hỏi HTTPS domain cloud. |
| `backend/MindSprint.Api/Program.cs` | Bổ sung Static File Hosting (`ServeFrontend`), endpoint liveness `/healthz`, endpoint readiness `/readyz` (kiểm tra kết nối PostgreSQL với timeout 3s). |
| `frontend/api.js` | Tự động nhận diện cổng 8080 để gọi API cùng origin (`window.location.origin`), xóa cache cũ tránh trỏ nhầm về cổng 5000 (#108, #110). |
| `scripts/selfhost-up.ps1` / `.sh` | Kịch bản tự động kiểm tra `.env.selfhost`, chạy docker compose và đợi readiness check. |
| `scripts/selfhost-down.ps1` / `.sh` | Kịch bản dừng container an toàn, giữ nguyên volume dữ liệu PostgreSQL. |
| `scripts/smoke-selfhost.ps1` / `.sh` | Kịch bản kiểm thử tự động 7 tiêu chí: liveness, readiness, HTML root, JS/CSS static, bảo vệ 404 cho API sai, bảo vệ 401 cho auth route. |

---

## 3. Hướng dẫn thiết lập và khởi động từ Checkout sạch

### Bước 1: Lấy mã nguồn nhánh baseline
```powershell
git fetch origin
git checkout dien/issue-105-selfhost-baseline
```

### Bước 2: Tạo tệp cấu hình môi trường từ mẫu
```powershell
Copy-Item .env.selfhost.example .env.selfhost
```
> [!IMPORTANT]
> Tệp `.env.selfhost` chứa thông tin mật khẩu DB và JWT key cục bộ. Không chỉnh sửa tệp `.env.selfhost.example` bằng thông tin bí mật thật và không commit `.env.selfhost` vào repo.

### Bước 3: Khởi động stack bằng script
```powershell
powershell -File scripts/selfhost-up.ps1
```
Hoặc dùng lệnh Docker Compose trực tiếp:
```powershell
docker compose -f docker-compose.selfhost.yml --env-file .env.selfhost up -d --build
```

### Bước 4: Chạy kiểm thử khói (Smoke Test)
```powershell
powershell -File scripts/smoke-selfhost.ps1
```
**Kết quả mong đợi:** 7/7 tiêu chí đạt `[PASS]`.

---

## 4. Hướng dẫn cho các thành viên phụ thuộc (#106 — #119)

* **Minh Sơn (`@minhsonle30122007-png`) — [#106](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/106) & [#109](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/109):**  
  Mẫu `.env.selfhost.example` và cơ chế `SecurityConfiguration.IsSelfHost` đã sẵn sàng. Bạn có thể bổ sung các validation chuyên sâu cho biến bắt buộc và cấu hình production startup.
* **Đình Hùng (`@James-Lloyd20`) — [#108](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/108) & [#110](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/110):**  
  `frontend/api.js` đã hỗ trợ fallback về cùng origin khi chạy ở cổng 8080 và tự làm sạch cache `api-base` cũ. Bạn có thể mở devtools Network/Console để kiểm tra các luồng CRUD/Kanban/SRS.
* **Tuấn Linh (`@tuanlinh0511`) — [#95](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/95) & [#111](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/111):**  
  Volume PostgreSQL được đặt tên cố định `mindsprint_selfhost_pgdata`. Bạn có thể dùng `docker exec mindsprint-selfhost-db pg_dump` để viết kịch bản backup/restore.
* **Phan Diễn (`@phandien665-prog`) — [#107](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/107) & [#113](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/113):**  
  Sử dụng `Dockerfile.selfhost` và `scripts/smoke-selfhost.ps1` để đo đạc thời gian build sạch, thời gian khởi động container và mức tiêu thụ RAM/CPU.
* **Minh Chiến (`@mchihihaha`) — [#112](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/112) & [#118](https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/118):**  
  Endpoint `/healthz` (liveness) và `/readyz` (readiness) đã được tích hợp trong `Program.cs`. Compose đã cô lập cổng DB và chỉ bind loopback `127.0.0.1:8080`.
