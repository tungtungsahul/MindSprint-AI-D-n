using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MindSprint.Api.Migrations;

public partial class AddStudyPlanTaskMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ClientRequestId",
            table: "Tasks",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "DurationMinutes",
            table: "Tasks",
            type: "integer",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Tasks_UserId_ClientRequestId",
            table: "Tasks",
            columns: new[] { "UserId", "ClientRequestId" },
            unique: true,
            filter: "\"ClientRequestId\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Tasks_UserId_ClientRequestId", table: "Tasks");
        migrationBuilder.DropColumn(name: "ClientRequestId", table: "Tasks");
        migrationBuilder.DropColumn(name: "DurationMinutes", table: "Tasks");
    }
}
