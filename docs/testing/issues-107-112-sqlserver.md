# Kiểm chứng Docker và readiness trên SQL Server Express

Ngày kiểm tra **15/11/2026** (Asia/Saigon). Môi trường Windows, Docker client và Engine
29.7.2, Docker Compose v5.5.0, Python 3.14.0. Ứng dụng chạy cấu hình Production với SQL
Server Express 2022, cổng loopback tạm và volume mới dành riêng cho mỗi lượt thử.

- Nguồn từ `git archive` tại commit `452426d00559bd98bfebb13704006062ee7aa7c0`.
- Thay đổi duy nhất trong bản nguồn thử là bản sửa `.dockerignore` trong workspace.
  SHA-256 của file là `540db8ecbf7a3ba3e3032c40f7f9d48d28e9dfa3d763dec70363ebdb1f9ef602`.
- Web image ID `sha256:2bc1c0f6ba3f46db5945ea3118b990fa3f232c1d3ec135d94eef7601b7e339fb`.
  Lượt kiểm tra readiness có log dùng lại đúng image này, chỉ bật định dạng log JSON qua Compose override.
- SQL Server image ID `sha256:cf8f0ab2ebe61feec2eed2629061a07d247f791c2a8ddda74a4ac9f06a291718`.

## Issue #107 — Build và kiểm tra image

**Kết quả kiểm thử: Pass với bản sửa `.dockerignore`.** Trên commit gốc, BuildKit vẫn nhận
file log và file backup giả đặt trong hai thư mục nguồn được phép. Bản sửa loại trừ thêm
`logs/`, `backups/`, `output/`, các file `*.log`, `*.bak` và `appsettings.*.json` ở mọi cấp.
Cấu hình cơ sở `appsettings.json` vẫn có trong image.

| Kiểm tra | Kết quả |
| --- | --- |
| BuildKit export build context bằng Dockerfile hai dòng `FROM scratch`, `COPY . /` | Loại đủ 12 file giả dùng để kiểm tra. Context vẫn có `frontend/build.mjs` và `backend/MindSprint.Api/MindSprint.Api.csproj`. |
| Build frontend/API với `Dockerfile.selfhost` và `--no-cache` | Thành công trong 45,82 giây, image mang nhãn revision của commit nguồn. |
| Kiểm tra manifest của thư mục ứng dụng `/app` trong image đã build | 311 file. Có DLL API và trang frontend. Không có file env, cấu hình riêng, file backup hoặc log đã kiểm tra. |
| Smoke trên SQL Server Express với dữ liệu riêng | Frontend, runtime-config, readiness, kiểm tra chặn truy cập đường dẫn cấu hình, đăng ký/đăng nhập, task, flashcard và nguồn sổ tay đều qua. Google sign-in và Gemini dùng chế độ tắt trong stack thử. |

File giả đặt tại thư mục gốc, trong frontend và trong backend bao gồm `.env.selfhost`,
Gemini secret, `.env.local`, `appsettings.Development.json`, `appsettings.Local.json`,
file trong các thư mục log, backup, output và file `*.log`, `*.bak` đặt trực tiếp trong thư mục nguồn.

## Issue #112 — Liveness và readiness

**Kết quả kiểm thử: Pass.** Đo bằng `curl.exe --max-time 8 --write-out`; thời gian trong
bảng là thời gian HTTP quan sát từ máy chạy Docker.

| Trạng thái | `/healthz` | `/readyz` |
| --- | --- | --- |
| DB đang chạy | `200 ok`, 4,531 ms | `200 ready`, 29,656 ms |
| Đã xác nhận container DB dừng | `200 ok`, 2,279 ms | `503 unavailable`, 3.997,263 ms |
| DB đã khởi động lại | `200 ok`, 3,500 ms | `200 ready`, 60,847 ms |

Từ lệnh khởi động DB đến lần kiểm tra readiness qua mất 5,829 giây. Container web giữ
nguyên ID và số lần restart bằng 0. Dòng log dưới đây chỉ giữ thời điểm UTC, mã sự kiện,
tên health check, trạng thái và thời gian. HTTP response chỉ trả trường trạng thái.

```text
2026-11-14T18:03:14.648488949Z [app log event 104] database Unhealthy after 3981.4644 ms
2026-11-14T18:03:15.398261500Z [app log event 104] database Unhealthy after 1.12 ms
```

[Health check của ứng dụng](../../backend/MindSprint.Api/Data/DatabaseHealthCheck.cs) chạy
`SELECT 1` với timeout kết nối và truy vấn. [Đăng ký health check](../../backend/MindSprint.Api/Program.cs#L26)
đặt timeout 3 giây. Khi DB dừng, thời gian quan sát từ phía client là gần 4 giây và dưới
ngưỡng 8 giây của smoke hiện hành.
[Compose](../../compose.selfhost.yaml#L40) chờ DB healthy trước khi chạy web và dùng
`/readyz` làm health check web. [Lệnh Start](../../scripts/selfhost.ps1#L119) chờ Compose và
kiểm tra JSON từ `/readyz`. [Smoke](../../tests/selfhost-smoke.py#L58) cũng kiểm tra
`/readyz`. Chế độ `--recovery` của smoke xác minh readiness báo lỗi trong lúc DB dừng.

## Chạy lại và đối chiếu

Chạy từ root repo trong PowerShell 7.3 trở lên với Docker Desktop đang chạy.

```powershell
$reviewIgnore = (Resolve-Path -LiteralPath .dockerignore).Path
& ./scripts/verify-selfhost-107-112.ps1 -Revision 452426d -DockerIgnoreOverride $reviewIgnore
```

Script xuất context bằng `docker build --output type=local`, sau đó build image bằng
`docker build --no-cache -f Dockerfile.selfhost` và liệt kê file trong `/app` qua
`docker run --rm --network none`. Nó tạo Compose project riêng, đợi
`up -d --no-build --wait`, chạy `python tests/selfhost-smoke.py <URL loopback>`, rồi
dùng `stop db` và `start --wait db` để đo các trạng thái HTTP. Cuối lượt chạy, script
dùng `down --volumes` trên chính project thử. Hai lượt ghi bằng chứng ở
`output/mindsprint-review-f4e467a1166f/evidence.txt` (build) và
`output/mindsprint-review-620e8d93c5a8/evidence.txt` (readiness và log).

Reviewer được ghi trong cả hai issue là @minhsonle30122007-png. Các kết quả trong báo
cáo là số đo từ lượt chạy kiểm thử.
