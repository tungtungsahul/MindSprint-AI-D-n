using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindSprintAI_ASP.NET_MVC.Data.Migrations
{
    /// <inheritdoc />
    public partial class DoiTenHienThi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DisplayNameChangedAt",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayNameChangedAt",
                table: "AspNetUsers");
        }
    }
}
