# Kết nối MindSprint với SQL Server

Từ ngày 09/11/2026, backend mặc định dùng SQL Server; bản vá EF Core hiện tại là 8.0.34.
Máy hiện tại có SQL Server instance mặc định `localhost`. Database ứng dụng là
`mindsprint`, xác thực bằng tài khoản Windows đang chạy backend.

## Chạy local

`appsettings.json` và cấu hình Development trên máy dùng:

```json
{
  "Database": { "Provider": "SqlServer" },
  "ConnectionStrings": {
    "Default": "Server=localhost;Database=mindsprint;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
  }
}
```

Chạy từ thư mục gốc:

```powershell
dotnet run --project backend/MindSprint.Api
```

Backend tự tạo database, chạy migration SQL Server và seed từ vựng. Tài khoản
Windows cần quyền tạo database lần đầu. Khóa JWT vẫn phải có ít nhất 32 ký tự;
giữ khóa JWT/Gemini trong cấu hình Development cục bộ, User Secrets hoặc biến môi trường.

Trong SSMS: Server name `localhost`, Authentication `Windows Authentication`,
chọn Trust server certificate nếu dùng chứng chỉ tự ký của máy local, rồi mở database `mindsprint`.

Instance Express có thể dùng `Server=.\SQLEXPRESS`; LocalDB dùng
`Server=(localdb)\MSSQLLocalDB` nếu instance đó đã cài trên máy.
Đổi `ConnectionStrings__Default` hoặc cấu hình Development khi đổi instance.
Biến môi trường và User Secrets có thể ghi đè các file cấu hình.

Nếu dùng SQL Login, đặt chuỗi kết nối riêng bằng biến môi trường hoặc User Secrets:

```text
Server=YOUR_SERVER,1433;Database=mindsprint;User ID=YOUR_LOGIN;Password=YOUR_PASSWORD;Encrypt=True;TrustServerCertificate=False
```

SQL Server phải bật SQL Authentication để dùng SQL Login. `TrustServerCertificate=True`
chỉ dành cho máy local/container demo có chứng chỉ tự ký; server triển khai cần
chứng chỉ hợp lệ và `TrustServerCertificate=False`.

## Migration và dữ liệu

`SqlServerDbContext` dùng migration/snapshot trong `Migrations/SqlServer`.
`AppDbContext` giữ lịch sử PostgreSQL cũ. Không chạy migration PostgreSQL lên SQL Server.

```powershell
dotnet ef migrations add TenThayDoi --context SqlServerDbContext --output-dir Migrations/SqlServer --project backend/MindSprint.Api
dotnet ef database update --context SqlServerDbContext --project backend/MindSprint.Api
```

Khi thay đổi model dùng chung, cập nhật cả hai bộ migration nếu tiếp tục duy trì
Render/PostgreSQL. `render.yaml` chỉ định `Database__Provider=PostgreSQL` và nhận chuỗi
kết nối PostgreSQL từ Render. Đổi provider không sao chép tài khoản, thẻ hay sổ tay
từ PostgreSQL; dữ liệu cũ được giữ nguyên. Muốn chuyển dữ liệu cần xuất/nhập riêng.

`datetime2` không lưu `DateTime.Kind`; SQL Server context gắn lại UTC khi đọc để giữ
định dạng thời gian API. Các cột tiếng Việt dùng `nvarchar`, file dùng `varbinary(max)`.
Token, Google subject và mã thẻ dùng collation `Latin1_General_100_BIN2` để so sánh
chính xác chữ hoa/thường, không phụ thuộc collation mặc định của server.

## Docker và kiểm tra

Xem [demo tự host](SELF-HOST-DEMO.md) để chạy web cùng SQL Server Express trong Docker.
SQL Server dùng volume `sqlserver-data` riêng; volume PostgreSQL cũ không bị xóa.
Các lệnh backup/restore, chuyển sang database khôi phục và kiểm tra phục hồi được
ghi tại [hướng dẫn vận hành self-host](SELF-HOST-DEMO.md#sao-lưu-và-khôi-phục-sql-server).

```powershell
dotnet run --project backend/SqlServerChecks
dotnet run --project backend/SqlServerChecks -- --live
```

Lệnh đầu kiểm tra cấu hình, migration và tách biệt provider mà không kết nối server.
`--live` mặc định kết nối `localhost` bằng Windows Authentication, tạo database
`MindSprintSqlServerChecks_<GUID>`, kiểm tra Up/Down, dữ liệu tiếng Việt, UTC,
file, lịch học, truy vấn flashcard, token (kể cả đổi chữ hoa/thường) và concurrency rồi xóa database thử.
Có thể đặt `MINDSPRINT_SQLSERVER_TEST_CONNECTION` để chọn server khác; tên database
trong biến này luôn được thay bằng tên database thử ngẫu nhiên.

Tham khảo Microsoft: [SQL Server provider](https://learn.microsoft.com/en-us/ef/core/providers/sql-server/),
[migration cho nhiều provider](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/providers),
[giữ DateTime.Kind](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions#specify-the-datetimekind-when-reading-dates).
