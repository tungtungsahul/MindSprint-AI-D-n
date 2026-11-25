using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindSprintAI_ASP.NET_MVC.Data.Migrations
{
    /// <inheritdoc />
    public partial class BatRcsi : Migration
    {
        /// <inheritdoc />
        // Code khóa mang từ QuizEduManager (AttemptLock, đường nhanh tín hiệu) được viết và thử tải với RCSI bật
        // (BAI_HOC_TOI_UU_THU_TAI.md mục 3.5). Để trong migration để mọi DB tạo mới đều bật, không phụ thuộc script máy chủ.
        // Tách riêng vì ALTER DATABASE không chạy trong giao dịch; ROLLBACK IMMEDIATE ngắt kết nối khác, đừng chạy lúc DB đang dùng.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;", suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT OFF WITH ROLLBACK IMMEDIATE;", suppressTransaction: true);
        }
    }
}
