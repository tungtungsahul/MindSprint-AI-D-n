using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindSprintAI_ASP.NET_MVC.Data.Migrations
{
    /// <inheritdoc />
    public partial class BaiLamTheoDe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaperFileName",
                table: "Assignments",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaperStoredName",
                table: "Assignments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaperTitle",
                table: "Assignments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaperUploadedAt",
                table: "Assignments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PaperSubmissions",
                columns: table => new
                {
                    AttemptId = table.Column<long>(type: "bigint", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SavedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Score = table.Column<decimal>(type: "decimal(4,2)", precision: 4, scale: 2, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    GradedById = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    GradedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperSubmissions", x => x.AttemptId);
                    table.ForeignKey(
                        name: "FK_PaperSubmissions_Attempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "Attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaperFiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttemptId = table.Column<long>(type: "bigint", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StoredName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaperFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaperFiles_PaperSubmissions_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "PaperSubmissions",
                        principalColumn: "AttemptId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaperFiles_AttemptId",
                table: "PaperFiles",
                column: "AttemptId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaperFiles");

            migrationBuilder.DropTable(
                name: "PaperSubmissions");

            migrationBuilder.DropColumn(
                name: "PaperFileName",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "PaperStoredName",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "PaperTitle",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "PaperUploadedAt",
                table: "Assignments");
        }
    }
}
