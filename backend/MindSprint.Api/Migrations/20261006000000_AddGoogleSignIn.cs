using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MindSprint.Api.Data;

namespace MindSprint.Api.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261006000000_AddGoogleSignIn")]
public class AddGoogleSignIn : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "GoogleSubject", table: "Users",
            type: "nvarchar(255)", maxLength: 255, nullable: true);
        migrationBuilder.CreateIndex(name: "IX_Users_GoogleSubject", table: "Users",
            column: "GoogleSubject", unique: true, filter: "[GoogleSubject] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Users_GoogleSubject", table: "Users");
        migrationBuilder.DropColumn(name: "GoogleSubject", table: "Users");
    }
}
