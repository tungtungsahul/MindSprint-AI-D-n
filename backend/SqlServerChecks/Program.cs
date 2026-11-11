using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MindSprint.Api.Controllers;
using MindSprint.Api.Data;
using MindSprint.Api.Models;
using MindSprint.Api.Services;

var passed = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception("FAIL: " + label);
    passed++;
    Console.WriteLine("PASS: " + label);
}

var configuration = new ConfigurationBuilder()
    .AddJsonFile(Path.GetFullPath("backend/MindSprint.Api/appsettings.json")).Build();
Check(configuration["Database:Provider"] == "SqlServer", "SQL Server is the application default");
using (var services = new ServiceCollection()
    .AddMindSprintDatabase(configuration, configuration.GetConnectionString("Default")!)
    .BuildServiceProvider())
using (var scope = services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    Check(db is SqlServerDbContext && db.Database.IsSqlServer(), "controllers resolve the SQL Server context");
    Check(ReferenceEquals(db, scope.ServiceProvider.GetRequiredService<SqlServerDbContext>()),
        "controllers share one scoped context for transactions");
    Check(db.Database.GetMigrations().First().EndsWith("InitialCreateSqlServer")
        && db.Database.GetMigrations().All(id => !id.Contains("PostgreSQL")),
        "SQL Server applies only its own migration history");
    Check(!db.Database.HasPendingModelChanges(), "SQL Server migration matches the current model");
    var migrator = db.GetService<IMigrator>();
    var script = migrator.GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
    Check(script.Contains("CREATE TABLE [NotebookSources]") && script.Contains("varbinary(max)")
        && script.Contains("WHERE [GoogleSubject] IS NOT NULL"),
        "SQL Server schema supports file storage and nullable Google identities");
    Check(migrator.GenerateScript(db.Database.GetMigrations().Last(), "0")
        .Contains("DROP TABLE [Users]"), "SQL Server migration can be rolled back");
}

var postgres = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["Database:Provider"] = "PostgreSQL" }).Build();
using (var services = new ServiceCollection().AddMindSprintDatabase(postgres,
    "Host=postgres.example;Database=mindsprint;Username=test").BuildServiceProvider())
using (var scope = services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    Check(db.Database.IsNpgsql() && db.Database.GetMigrations().All(id => !id.EndsWith("InitialCreateSqlServer")),
        "existing PostgreSQL deployments retain their own provider and migrations");
}
var invalid = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["Database:Provider"] = "unknown" }).Build();
try
{
    new ServiceCollection().AddMindSprintDatabase(invalid, "unused");
    throw new Exception("FAIL: an unknown provider must not silently select a database.");
}
catch (InvalidOperationException error) when (error.Message.Contains("Database:Provider"))
{
    Check(true, "invalid database provider fails clearly");
}

if (args.Contains("--live"))
{
    // Always use a new disposable database, never the database named in the supplied connection string.
    var connection = new SqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("MINDSPRINT_SQLSERVER_TEST_CONNECTION")
        ?? "Server=localhost;Integrated Security=True;Encrypt=True;TrustServerCertificate=True")
    { InitialCatalog = "MindSprintSqlServerChecks_" + Guid.NewGuid().ToString("N") };
    var options = new DbContextOptionsBuilder<SqlServerDbContext>().UseSqlServer(connection.ConnectionString).Options;
    await using var db = new SqlServerDbContext(options);
    Check(!await db.Database.CanConnectAsync(), "live checks use a new isolated database");
    try
    {
        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync();
        Check(!(await db.Database.GetPendingMigrationsAsync()).Any(),
            "SQL Server creates all tables and repeated migration is safe");
        var first = new User { Email = "first@example.com", DisplayName = "Người học thứ nhất" };
        var second = new User { Email = "second@example.com", DisplayName = "Người học thứ hai" };
        db.Users.AddRange(first, second);
        await db.SaveChangesAsync();
        Check(await db.Users.CountAsync() == 2, "multiple email accounts may have a null Google identity");

        var due = new DateTime(2026, 11, 10, 1, 2, 3, 456, DateTimeKind.Utc);
        var card = new Flashcard { OwnerId = first.Id, Question = "Cơ sở dữ liệu?", Answer = "SQL Server" };
        var notebook = new Notebook { UserId = first.Id, Title = "Sổ tay SQL Server" };
        db.AddRange(card, notebook);
        await db.SaveChangesAsync();
        db.CardProgresses.Add(new CardProgress { UserId = first.Id, FlashcardId = card.Id, NextReviewDate = due });
        db.NotebookSources.Add(new NotebookSource { NotebookId = notebook.Id, Title = "Tệp thử", FileBytes = [1, 2, 3], Content = "Nội dung tiếng Việt" });
        db.UserStudyDays.Add(new UserStudyDay { UserId = first.Id, StudyDate = new DateOnly(2026, 11, 9) });
        db.StudySchedules.Add(new StudySchedule { UserId = first.Id, StartTime = new TimeOnly(8, 30) });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var savedCard = await db.Flashcards.SingleAsync();
        Check(savedCard.Question == "Cơ sở dữ liệu?", "Vietnamese text round-trips through SQL Server");
        Check(savedCard.UpdatedAt.Kind == DateTimeKind.Utc, "SQL Server timestamps retain UTC semantics");
        Check(await db.NotebookSources.SumAsync(source => (long)(source.FileBytes == null ? 0 : source.FileBytes.Length)) == 3,
            "SQL Server calculates file quotas in the database");
        Check((await db.UserStudyDays.SingleAsync()).StudyDate == new DateOnly(2026, 11, 9)
            && (await db.StudySchedules.SingleAsync()).StartTime == new TimeOnly(8, 30),
            "study dates and times round-trip");

        var controller = new FlashcardsController(db)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, first.Id.ToString())], "test")) } }
        };
        var result = (OkObjectResult)await controller.GetAll(null, null);
        var rows = (List<CardResponse>)result.Value!;
        Check(rows.Single().NextReviewDate == new DateTimeOffset(due).ToUnixTimeMilliseconds(),
            "flashcard queries return the exact UTC review timestamp");
        var tokens = new TokenService(configuration, db);
        var refresh = await tokens.CreateRefreshTokenAsync(await db.Users.FindAsync(first.Id) ?? throw new Exception(), "SQL Server check");
        Check(await tokens.ValidateRefreshTokenAsync(refresh.Token) is not null, "SQL Server persists valid refresh tokens");
        var changed = refresh.Token.ToCharArray();
        var letter = Array.FindIndex(changed, char.IsAsciiLetter);
        changed[letter] = char.IsUpper(changed[letter]) ? char.ToLowerInvariant(changed[letter]) : char.ToUpperInvariant(changed[letter]);
        Check(await tokens.ValidateRefreshTokenAsync(new string(changed)) is null,
            "SQL Server rejects refresh tokens whose letter case was changed");

        await using (var competing = new SqlServerDbContext(options))
        {
            var stale = await competing.Flashcards.SingleAsync();
            savedCard.Version++;
            await db.SaveChangesAsync();
            stale.Version++;
            try
            {
                await competing.SaveChangesAsync();
                throw new Exception("FAIL: stale writes must conflict.");
            }
            catch (DbUpdateConcurrencyException) { Check(true, "SQL Server rejects stale flashcard writes"); }
        }
        // Exercise Down then Up only on this disposable database.
        await db.GetService<IMigrator>().MigrateAsync("0");
        await db.Database.MigrateAsync();
        Check(!await db.Users.AnyAsync(), "SQL Server migration rolls back and recreates the schema");
    }
    finally
    {
        await db.Database.EnsureDeletedAsync();
        Console.WriteLine("Removed the disposable SQL Server check database.");
    }
}
Console.WriteLine($"SQL Server checks: {passed} passed. Use --live for isolated SQL Server integration checks.");
