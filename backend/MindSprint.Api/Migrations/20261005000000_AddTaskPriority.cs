using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MindSprint.Api.Data;

namespace MindSprint.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261005000000_AddTaskPriority")]
public class AddTaskPriority : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(name: "Priority", table: "Tasks", type: "nvarchar(max)", nullable: false, defaultValue: "Medium");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "Priority", table: "Tasks");
}
