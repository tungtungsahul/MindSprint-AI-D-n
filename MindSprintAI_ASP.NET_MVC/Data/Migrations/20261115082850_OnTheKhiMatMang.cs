using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindSprintAI_ASP.NET_MVC.Data.Migrations
{
    /// <inheritdoc />
    public partial class OnTheKhiMatMang : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowOfflineCards",
                table: "Assignments",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowOfflineCards",
                table: "Assignments");
        }
    }
}
