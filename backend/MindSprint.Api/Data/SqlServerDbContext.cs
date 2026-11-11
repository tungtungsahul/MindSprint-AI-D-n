using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MindSprint.Api.Models;

namespace MindSprint.Api.Data;

// Separate context type keeps SQL Server migrations apart from the PostgreSQL history.
public class SqlServerDbContext(DbContextOptions<SqlServerDbContext> options) : AppDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<User>().HasIndex(u => u.GoogleSubject)
            .HasFilter("[GoogleSubject] IS NOT NULL");
        // SQL Server's default collation may ignore case; authentication identifiers must not.
        builder.Entity<RefreshToken>().Property(token => token.Token).UseCollation("Latin1_General_100_BIN2");
        builder.Entity<User>().Property(user => user.GoogleSubject).UseCollation("Latin1_General_100_BIN2");
        builder.Entity<Flashcard>().Property(card => card.ExternalId).UseCollation("Latin1_General_100_BIN2");
        // datetime2 stores UTC clock values but does not preserve DateTime.Kind.
        var utc = new ValueConverter<DateTime, DateTime>(value => value,
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        foreach (var property in builder.Model.GetEntityTypes().SelectMany(entity => entity.GetProperties()))
            if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                property.SetValueConverter(utc);
    }
}

public class SqlServerDbContextFactory : IDesignTimeDbContextFactory<SqlServerDbContext>
{
    public SqlServerDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();
        return new SqlServerDbContext(new DbContextOptionsBuilder<SqlServerDbContext>()
            .UseSqlServer(SecurityConfiguration.GetConnectionString(configuration, false)).Options);
    }
}
