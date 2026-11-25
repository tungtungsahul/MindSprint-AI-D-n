using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindSprintAI_ASP.NET_MVC.Data.Migrations
{
    /// <inheritdoc />
    public partial class XepHangLop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LeaderboardEnabled",
                table: "Classrooms",
                type: "bit",
                nullable: false,
                defaultValue: true);   // lớp đã có cũng bật xếp hạng (người dùng chốt 17/11)
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LeaderboardEnabled",
                table: "Classrooms");
        }
    }
}
