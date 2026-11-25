using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace MindSprintAI_ASP.NET_MVC.Data;

// EF tools chỉ dựng model, không khởi động worker/email hay kết nối DB để tạo migration.
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    // (NK3) EF không hỗ trợ savepoint với MARS. Các truy vấn đã được materialize trước lần ghi tiếp.
    public static string AuditConnectionString(string? connectionString) =>
        new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString ?? "") { MultipleActiveResultSets = false }.ConnectionString;
    private static readonly ServiceProvider ModelServices = new ServiceCollection()
        .Configure<IdentityOptions>(o => o.Stores.SchemaVersion = IdentitySchemaVersions.Version3).BuildServiceProvider();
    // Identity EF 10 đọc schema từ ApplicationServiceProvider cả khi context được dựng trực tiếp.
    public static DbContextOptions<AppDbContext> BuildOptions(string connectionString) => new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer(AuditConnectionString(connectionString)).UseApplicationServiceProvider(ModelServices).Options;
    public AppDbContext CreateDbContext(string[] args) => new(BuildOptions(
        "Server=localhost;Database=MindSprintAI;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True"));
}
