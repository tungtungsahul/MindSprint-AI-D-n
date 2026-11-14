# Issue #109 - Bằng chứng kiểm tra bổ sung

Ngày ghi nhận: 14/11/2026 (Asia/Saigon)
Issue: https://github.com/tungtungsahul/MindSprint-AI-D-n/issues/109
Branch: `fix/issue-109-production-localhost`
Code commit: `62a5cf8a241c7de2efe134d5e176d94c019d5951`
Baseline self-host: SQL Server Express 2022 theo issue #105; mô tả PostgreSQL cũ trong #109 không được dùng làm cấu hình nghiệm thu hiện tại.

## Thay đổi trên branch

- Production chấp nhận origin loopback HTTP cho triển khai laptop; public origin vẫn phải dùng HTTPS.
- Cấu hình connection string localhost được chấp nhận cho database laptop; giá trị thiếu vẫn bị từ chối rõ ràng.
- Security harness có assertion cho origin localhost Production và connection string localhost.

## Kết quả kiểm tra trên branch

| Kiểm tra | Kết quả | Ghi chú |
|---|---|---|
| `python -m pytest -q` | PASS: 48 passed | Regression suite Python; không thay thế kiểm tra backend ASP.NET Core. |
| `dotnet run --project backend/SecurityChecks/SecurityChecks.csproj` | BLOCKED | Máy có .NET runtime 8.0.3 nhưng không có .NET SDK; không biên dịch/chạy được harness C#. |
| `docker compose ... config/up` và tạo image | BLOCKED | Docker CLI/Engine không có trong môi trường thực hiện; không có image ID mới cho branch này. |
| Startup Production với DB/JWT/config thiếu hoặc sai | NOT RUN | Cần chạy lại SecurityChecks và startup trên SDK/Docker có sẵn; chưa ghi nhận log startup thực tế cho branch này. |
| Browser smoke localhost -> API -> DB trên image branch | NOT RUN | Không tạo image mới và không chạy stack trong lượt xác minh này. |

Lệnh .NET đã thử: `dotnet run --project backend/SecurityChecks/SecurityChecks.csproj`.
Kết quả môi trường: `No .NET SDKs were found`.

## Bằng chứng baseline có thể đối chiếu

Báo cáo [self-host SQL Server ngày 10/11](https://github.com/tungtungsahul/MindSprint-AI-D-n/blob/0cbbce18843270c555dae8d8f60e93dfe543af0c/docs/testing/selfhost-2026-11-10.md) ghi nhận stack laptop Production, smoke API/browser, truy cập DB, cùng image và artifact của baseline:

- Image build sạch baseline: `mindsprint-selfhost-verified:20261110`, ID `sha256:b0731fff6400c15a86e8fb78b17ed2fe831597920ee34dcfd8918993d957067d`.
- Image đang chạy tại lượt baseline: ID `sha256:8d1d5516c29928c206195bf932410c99c8251c96449b63cfb1a21af1cb21d343`.
- SHA256 gói nguồn baseline: `92d09fcb1936fa5f07ccc7576470456b9133afb625b64ab86319bed1c15b27e3`.

Các image/hash trên thuộc lượt kiểm tra baseline ngày 10/11, không phải artifact được build từ commit #109 `62a5cf8`. Chỉ dùng để dẫn lại các smoke case baseline đã chạy; không coi chúng là bằng chứng runtime cho code commit này. Log test baseline được báo cáo lưu cục bộ trong thư mục `output/`; báo cáo công khai không đính kèm raw logs hoặc secrets.

## Trạng thái nghiệm thu

Kết quả hiện tại chứng minh phần thay đổi cấu hình đã được đưa lên branch và Python regression suite đạt. Chưa đủ cơ sở đánh dấu toàn bộ issue #109 là PASS: cần chạy build/harness .NET, kiểm tra thiếu/sai cấu hình trên stack test riêng, xác nhận cùng-origin browser/API/DB với image từ đúng SHA, đính kèm log/ảnh đã che secret và chờ reviewer đối chiếu. Không có dữ liệu production hoặc secret nào được sử dụng trong lượt kiểm tra này.