using Microsoft.EntityFrameworkCore;
using MindSprint.Api.Services;

namespace MindSprint.Api.Data;

public static class DatabaseConfiguration
{
    public static IServiceCollection AddMindSprintDatabase(this IServiceCollection services,
        IConfiguration configuration, string connectionString)
    {
        switch ((configuration["Database:Provider"] ?? "SqlServer").ToLowerInvariant())
        {
            case "sqlserver":
                services.AddDbContext<SqlServerDbContext>(options => options.UseSqlServer(connectionString));
                services.AddScoped<AppDbContext>(provider => provider.GetRequiredService<SqlServerDbContext>());
                break;
            case "postgresql":
                services.AddDbContext<AppDbContext>(options =>
                    options.UseNpgsql(PostgresConnectionString.Normalize(connectionString)));
                break;
            default:
                throw new InvalidOperationException("Database:Provider must be SqlServer or PostgreSQL.");
        }
        return services;
    }
}
