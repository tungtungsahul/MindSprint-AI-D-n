using System.Security.Claims;
using System.Text.Json;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MindSprint.Api.Controllers;
using MindSprint.Api.Data;
using MindSprint.Api.Models;
using MindSprint.Api.Services;

var directory = Path.GetFullPath("artifacts/concurrency-checks");
Directory.CreateDirectory(directory);
var connection = "Data Source=" + Path.Combine(directory, Guid.NewGuid().ToString("N") + ".db");
DbContextOptions<AppDbContext> Options(SaveGate? gate = null) {
    var builder = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection);
    if (gate is not null) builder.AddInterceptors(gate);
    return builder.Options;
}
var passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAIL " + name); passed++; Console.WriteLine("PASS " + name); }
static int Status(IActionResult result) => result is ObjectResult o ? o.StatusCode ?? 200 : ((StatusCodeResult)result).StatusCode;
static JsonElement Body(IActionResult result) => JsonSerializer.SerializeToElement(((ObjectResult)result).Value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
FlashcardsController Controller(AppDbContext db, int user = 1) => new(db) { ControllerContext = new() { HttpContext = new DefaultHttpContext {
    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString())], "check"))
} } };
await using (var seed = new AppDbContext(Options())) {
    await seed.Database.EnsureCreatedAsync();
    seed.Users.AddRange(new User { Id = 1, Email = "owner@example.test" }, new User { Id = 2, Email = "other@example.test" });
    seed.RefreshTokens.AddRange(
        new RefreshToken { UserId = 1, Token = "synthetic-live", ExpiresAt = DateTime.UtcNow.AddDays(1) },
        new RefreshToken { UserId = 1, Token = "synthetic-expired", ExpiresAt = DateTime.UtcNow.AddDays(-1) },
        new RefreshToken { UserId = 1, Token = "synthetic-revoked", ExpiresAt = DateTime.UtcNow.AddDays(1), IsRevoked = true });
    seed.Flashcards.Add(new Flashcard { OwnerId = 1, ExternalId = "synthetic-card", Question = "Original", Answer = "Answer", Version = 1 });
    await seed.SaveChangesAsync();
}

await using (var createDb = new AppDbContext(Options()))
{
    var dto = new CardDto("nfr06-retry-card", "general", "NFR-06", "Retry me", "Saved once", null, null);
    var first = await Controller(createDb).Create(dto);
    var replay = await Controller(createDb).Create(dto);
    Check(Status(first) == 200 && Status(replay) == 200, "replaying a flashcard create with the same external ID succeeds");
    Check(Body(first).GetProperty("id").GetString() == Body(replay).GetProperty("id").GetString(), "idempotent replay returns the original card ID");
    Check(await createDb.Flashcards.CountAsync(f => f.ExternalId == dto.ExternalId) == 1, "idempotent replay stores only one flashcard");

    var changedPayload = dto with { Answer = "Changed after the first request" };
    Check(Status(await Controller(createDb).Create(changedPayload)) == 409, "reusing an external ID with different content is rejected");
    Check((await createDb.Flashcards.SingleAsync(f => f.ExternalId == dto.ExternalId)).Answer == dto.Answer, "a conflicting replay does not overwrite the original card");

    Check(Status(await Controller(createDb, 2).Create(dto)) == 409, "another account cannot claim an existing external ID");
    Check(await createDb.Flashcards.CountAsync(f => f.ExternalId == dto.ExternalId) == 1, "cross-account replay does not disclose or duplicate the original card");
}

var createGate = new SaveGate();
await using (var createOne = new AppDbContext(Options(createGate)))
await using (var createTwo = new AppDbContext(Options(createGate)))
{
    var dto = new CardDto("nfr06-concurrent-retry-card", "general", "NFR-06", "Concurrent retry", "Saved once", null, null);
    var results = await Task.WhenAll(Controller(createOne).Create(dto), Controller(createTwo).Create(dto));
    Check(results.All(result => Status(result) == 200), "concurrent replays with the same external ID return success");
    await using var inspectCreates = new AppDbContext(Options());
    Check(await inspectCreates.Flashcards.CountAsync(f => f.ExternalId == dto.ExternalId) == 1, "concurrent replays persist one flashcard");
}

var aiFailureConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Gemini:ApiKey"] = "synthetic-test-key",
    ["Gemini:Model"] = "synthetic-primary",
    ["Gemini:FallbackModels:0"] = "synthetic-backup",
    ["Gemini:MaxRetries"] = "0",
    ["Gemini:RetryDelayMilliseconds"] = "1",
    ["Gemini:AttemptTimeoutSeconds"] = "1",
    ["Gemini:TotalTimeoutSeconds"] = "2"
}).Build();
var failedGemini = new GeminiService(new HttpClient(new StatusHandler(HttpStatusCode.ServiceUnavailable))
    { Timeout = Timeout.InfiniteTimeSpan }, aiFailureConfig, new GeminiAvailability(aiFailureConfig),
    NullLogger<GeminiService>.Instance, new HttpContextAccessor());
var aiController = new AiController(failedGemini)
{
    ControllerContext = new() { HttpContext = new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1")], "check"))
    } }
};
Check(Status(await aiController.Flashcards(new GenerateDto("synthetic source", 3))) == 503, "AI provider outage returns a controlled 503");
await using (var crudDuringAiFailure = new AppDbContext(Options()))
{
    var dto = new CardDto("nfr06-crud-during-ai-outage", "general", "NFR-06", "CRUD still works", "Provider independent", null, null);
    var result = await Controller(crudDuringAiFailure).Create(dto);
    Check(Status(result) == 200 && await crudDuringAiFailure.Flashcards.CountAsync(f => f.ExternalId == dto.ExternalId) == 1,
        "flashcard creation remains available after a Gemini 503");
}

if (!args.Contains("--cards")) {
    var gate = new SaveGate();
    await using var one = new AppDbContext(Options(gate));
    await using var two = new AppDbContext(Options(gate));
    var cfg = new ConfigurationBuilder().Build();
    var results = await Task.WhenAll(new TokenService(cfg, one).RotateAsync("synthetic-live", "tab A"), new TokenService(cfg, two).RotateAsync("synthetic-live", "tab B"));
    Check(results.Count(r => r is not null) == 1, "one old refresh token can issue only one replacement under concurrency");
    await using var inspect = new AppDbContext(Options());
    Check(await inspect.RefreshTokens.CountAsync(r => !r.IsRevoked && r.ExpiresAt > DateTime.UtcNow) == 1, "losing rotation does not leave an active token");
    Check(await inspect.RefreshTokens.CountAsync() == 4, "losing rotation insert is rolled back with the revoked-token update");
    Check(!one.ChangeTracker.Entries<RefreshToken>().Any(e => e.State == EntityState.Added) && !two.ChangeTracker.Entries<RefreshToken>().Any(e => e.State == EntityState.Added), "loser does not leave a pending replacement in the scoped context");
    var tokens = new TokenService(cfg, inspect);
    foreach (var invalid in new[] { "synthetic-live", "synthetic-expired", "synthetic-revoked", "missing" })
        Check(await tokens.RotateAsync(invalid, null) is null, "refresh rejects replay/expired/revoked/missing: " + invalid);
    var winningToken = results.Single(r => r is not null)!;
    Check(await tokens.RotateAsync(winningToken.Token, null) is not null, "the winning replacement can rotate normally");

    await using var seedRevoke = new AppDbContext(Options());
    seedRevoke.RefreshTokens.Add(new RefreshToken { UserId = 1, Token = "synthetic-logout", ExpiresAt = DateTime.UtcNow.AddDays(1) });
    await seedRevoke.SaveChangesAsync();
    var revokeGate = new SaveGate();
    await using var rotating = new AppDbContext(Options(revokeGate));
    await using var revoking = new AppDbContext(Options(revokeGate));
    var rotation = new TokenService(cfg, rotating).RotateAsync("synthetic-logout", null);
    var revocation = new TokenService(cfg, revoking).RevokeAsync("synthetic-logout", 1);
    await Task.WhenAll(rotation, (Task)revocation);
    Check(await revocation, "revoking concurrently with rotation remains an idempotent success");
    Check(await seedRevoke.RefreshTokens.AsNoTracking().Where(r => r.Token == "synthetic-logout").AllAsync(r => r.IsRevoked), "concurrent logout leaves the old token revoked");

    var loginGate = new SaveGate();
    await using var loginOne = new AppDbContext(Options(loginGate));
    await using var loginTwo = new AppDbContext(Options(loginGate));
    var sessions = await Task.WhenAll(
        new TokenService(cfg, loginOne).CreateRefreshTokenAsync(new User { Id = 1 }, "login A"),
        new TokenService(cfg, loginTwo).CreateRefreshTokenAsync(new User { Id = 1 }, "login B"));
    Check(sessions.Select(r => r.Token).Distinct().Count() == 2, "concurrent logins can clean old tokens without failing each other");
    Check(await seedRevoke.RefreshTokens.AsNoTracking().CountAsync(r => !r.IsRevoked && sessions.Select(s => s.Token).Contains(r.Token)) == 2, "both concurrent login sessions remain active after cleanup");
}

if (!args.Contains("--tokens")) {
    var gate = new SaveGate();
    await using (var one = new AppDbContext(Options(gate)))
    await using (var two = new AppDbContext(Options(gate))) {
        var results = await Task.WhenAll(
            Controller(one).Update("synthetic-card", new(null, "general", null, "Edit A", "Answer", null, 1)),
            Controller(two).Update("synthetic-card", new(null, "general", null, "Edit B", "Answer", null, 1)));
        Check(results.Select(Status).Order().SequenceEqual(new[] { 200, 409 }), "two updates from the same version return one success and one conflict");
        await using var inspect = new AppDbContext(Options());
        var card = await inspect.Flashcards.SingleAsync(f => f.ExternalId == "synthetic-card");
        Check(card.Version == 2, "winning update advances the version exactly once");
        var conflict = Body(results.Single(r => Status(r) == 409));
        Check(conflict.GetProperty("serverVersion").GetInt32() == 2 && conflict.GetProperty("serverCard").GetProperty("question").GetString() == card.Question, "conflict includes the actual winning card for the client to reload");
        Check(Status(await Controller(inspect).Update("synthetic-card", new(null, "general", null, "Stale", "Answer", null, 1))) == 409, "later stale updates are rejected");
        Check(Status(await Controller(inspect, 2).Update("synthetic-card", new(null, "general", null, "Foreign", "Answer", null, 2))) == 404, "concurrency handling retains card ownership checks");
        Check(Status(await Controller(inspect).Update("synthetic-card", new(null, "general", null, "Next", "Answer", null, 2))) == 200, "retrying with the current version succeeds");
    }
    gate = new SaveGate();
    await using (var editing = new AppDbContext(Options(gate)))
    await using (var deleting = new AppDbContext(Options(gate))) {
        var results = await Task.WhenAll(
            Controller(editing).Update("synthetic-card", new(null, "general", null, "Last edit", "Answer", null, 3)),
            Controller(deleting).Delete("synthetic-card", 3));
        var editStatus = Status(results[0]); var deleteStatus = Status(results[1]);
        Check((editStatus == 200 && deleteStatus == 409) || (editStatus == 404 && deleteStatus == 204), "edit/delete races return a conflict or not-found instead of a server error");
    }
}
Console.WriteLine($"{passed} concurrency checks passed; isolated SQLite only.");

sealed class SaveGate : SaveChangesInterceptor
{
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int waiting;
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) {
        if (Interlocked.Increment(ref waiting) == 2) release.TrySetResult();
        await release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        return result;
    }
}

sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{}") });
}
