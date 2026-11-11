# ADR-006: SQL Server cho local và bản demo tự host

**Trạng thái:** Chấp nhận theo yêu cầu người dùng. **Ngày:** 09/11/2026.

## Bối cảnh

Người dùng chọn SQL Server để kết nối database dự án. Máy Windows đã có instance
SQL Server mặc định, thư viện EF Core SQL Server 8.0.8 cũng đã có trong backend.
Render Blueprint cũ và các migration hiện có dùng PostgreSQL.

## Quyết định

Mặc định chọn `Database:Provider=SqlServer`, kết nối `localhost/mindsprint` bằng
Windows Authentication. Demo Docker dùng SQL Server Express 2022 với volume riêng.
Context `SqlServerDbContext` kế thừa model chung và dùng lịch sử migration riêng
trong `Migrations/SqlServer`; các controller vẫn nhận `AppDbContext` trong cùng scope.

Render chỉ định `PostgreSQL` rõ ràng và giữ migration cũ. Hai bộ migration cho phép
thêm kết nối SQL Server mà không làm hỏng database PostgreSQL đang có. Schema
SQL Server dùng `nvarchar`, `varbinary(max)`, `datetime2` và khôi phục UTC khi đọc.
Token, Google subject và mã thẻ dùng collation nhị phân `Latin1_General_100_BIN2`
để giữ so sánh chữ hoa/thường chính xác như PostgreSQL.

## Phương án được cân nhắc

- MySQL: người dùng chọn SQL Server, không thêm provider MySQL.
- Dùng lại migration PostgreSQL: kiểu cột và identity khác SQL Server, không phù hợp.
- Xóa lịch sử PostgreSQL: làm mất đường nâng cấp của các môi trường cũ.

## Hệ quả và kiểm chứng

Đổi provider không chuyển dữ liệu PostgreSQL. Database và Docker volume cũ được
giữ nguyên; chuyển tài khoản/sổ tay cũ cần kế hoạch xuất/nhập riêng. Khi sửa model
chung cần cập nhật migration của cả hai provider nếu vẫn hỗ trợ Render cũ.

`backend/SqlServerChecks` kiểm tra chọn provider, migration/snapshot riêng và SQL
schema. Chế độ `--live` tạo database thử ngẫu nhiên, xác minh migration Up/Down,
UTC, tiếng Việt, dữ liệu file, lịch học, truy vấn flashcard, token và concurrency,
rồi xóa database thử. Xem [hướng dẫn kết nối](../SQL-SERVER.md).

**Nguồn:** [SQL Server provider](https://learn.microsoft.com/en-us/ef/core/providers/sql-server/),
[migration cho nhiều provider](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/providers),
[DateTime.Kind](https://learn.microsoft.com/en-us/ef/core/modeling/value-conversions#specify-the-datetimekind-when-reading-dates).
