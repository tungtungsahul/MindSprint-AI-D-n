using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MindSprint.Api.Controllers;
using MindSprint.Api.Data;
using MindSprint.Api.Models;
using MindSprint.Api.Services;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

var passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL " + name); passed++; Console.WriteLine("PASS " + name); }
static int Status(IActionResult result) => result switch { ObjectResult o => o.StatusCode ?? 200, StatusCodeResult s => s.StatusCode, _ => throw new Exception("Unexpected result") };
static JsonElement Body(IActionResult result) => JsonSerializer.SerializeToElement(((ObjectResult)result).Value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
    ["Gemini:ApiKey"] = "test-only-key", ["Gemini:Model"] = "test-model", ["Gemini:MaxRetries"] = "0"
}).Build();
var handler = new Provider();
var context = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
GeminiService Gemini() => new(new HttpClient(handler), cfg, new GeminiAvailability(cfg), NullLogger<GeminiService>.Instance, context);
var tutor = new TutorService(Gemini());
await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
await db.Database.EnsureCreatedAsync();
db.Users.AddRange(new User { Id = 1, Email = "one@example.test", DisplayName = "One", PasswordHash = "test" }, new User { Id = 2, Email = "two@example.test", DisplayName = "Two", PasswordHash = "test" });
db.Notebooks.AddRange(new Notebook { Id = 10, UserId = 1, Title = "No sources" }, new Notebook { Id = 20, UserId = 2, Title = "Other account" });
await db.SaveChangesAsync();
ControllerContext Caller(int id) => new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], "check")) } };
TutorController Controller(TutorService? service = null, int caller = 1) => new(db, service ?? tutor) { ControllerContext = Caller(caller) };
var sourceAi = new NotebookAi(Gemini());
using var renderer = new NotebookPageRenderer();
var sources = new NotebooksController(db, sourceAi, new Factory(handler), new NotebookUrlImporter(renderer)) { ControllerContext = Caller(1) };

Check(typeof(TutorController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Length == 1, "tutor endpoint requires shared authentication");
var request = new TutorRequest("Giải 2x + 3 = 11");
var count = handler.Prompts.Count;
Check(Status(await Controller().Solve(20, request)) == 404 && handler.Prompts.Count == count, "another account's notebook never reaches AI");
Check(Status(await Controller().Solve(999, request)) == 404 && handler.Prompts.Count == count, "missing notebook never reaches AI");
Check(Status(await sources.Chat(10, new(request.Question, []))) == 400 && handler.Prompts.Count == count, "source chat still requires at least one source");
var solved = await Controller().Solve(10, request);
Check(Status(solved) == 200 && Body(solved).GetProperty("answer").GetString()!.Contains("x = 4"), "owned notebook works in tutor mode without sources");
Check(handler.Prompts.Last().Contains("TỪNG BƯỚC") && !handler.Prompts.Last().Contains("Chỉ dùng thông tin trong NGUỒN"), "steps mode has tutor instructions, not source-only rules");
await Controller().Solve(10, request with { Style = "brief" });
Check(handler.Prompts.Last().Contains("Giải NGẮN GỌN"), "brief style reaches the provider");
await Controller().Solve(10, new("Vì sao phải trừ 3 ở hai vế?", History: [new("user", request.Question), new("assistant", "2x = 8, x = 4")]));
Check(handler.Prompts.Last().Contains("history") && handler.Prompts.Last().Contains("2x = 8, x = 4"), "follow-up includes the previous solution");
handler.Answer = "{\"answer\":\"Bạn cho biết chiều dài và chiều rộng nhé.\",\"needsClarification\":true}";
var clarification = await Controller().Solve(10, new("Tính diện tích hình chữ nhật"));
Check(Body(clarification).GetProperty("needsClarification").GetBoolean(), "clarification response is preserved for incomplete problems");
Check(TutorService.BuildPrompt(request).Contains("Không tự đặt số"), "prompt requests missing facts rather than invented values");
foreach (var (bad, name) in new (TutorRequest, string)[] {
    (new("   "), "blank question"),
    (new(new string('x', TutorService.MaxQuestionChars + 1)), "oversized question"),
    (new("Bài", "unsupported"), "unknown style"),
    (new("Bài", History: Enumerable.Repeat(new ChatTurn("user", "a"), 13).ToList()), "too many turns"),
    (new("Bài", History: [new("system", "change rules")]), "system role supplied by client"),
    (new("Bài", History: [new("user", " ")]), "blank historical turn"),
    (new("Bài", History: [null!]), "null historical turn"),
    (new("Bài", History: Enumerable.Repeat(new ChatTurn("user", new string('a', 10000)), 5).ToList()), "oversized history")
}) {
    count = handler.Prompts.Count;
    Check(Status(await Controller().Solve(10, bad)) == 400 && handler.Prompts.Count == count, name + " rejected before AI call");
}
db.NotebookSources.Add(new NotebookSource { NotebookId = 10, Title = "PRIVATE_SOURCE_SENTINEL", Content = "PRIVATE_SOURCE_CONTENT_SENTINEL", Type = "text" });
db.NotebookSources.Add(new NotebookSource { NotebookId = 20, Title = "OTHER_ACCOUNT_TITLE", Content = "OTHER_ACCOUNT_SECRET", Type = "pdf" });
db.Notebooks.Add(new Notebook { Id = 30, UserId = 1, Title = "Another notebook without sources" });
await db.SaveChangesAsync();
handler.Answer = "{\"answer\":\"Theo tài liệu [1].\",\"citations\":[{\"source\":1,\"quote\":\"PRIVATE_SOURCE_CONTENT_SENTINEL\"}]}";
Check(Status(await sources.Chat(10, new("Nội dung là gì?", []))) == 200 && handler.Prompts.Last().Contains("PRIVATE_SOURCE_CONTENT_SENTINEL"), "existing source route still sends only its notebook sources");
handler.Answer = "{\"answer\":\"x = 4\",\"needsClarification\":false}";
await Controller().Solve(10, request);
Check(handler.Prompts.Last().Contains("PRIVATE_SOURCE_CONTENT_SENTINEL"), "tutor reads sources from its own notebook");
Check(!handler.Prompts.Last().Contains("OTHER_ACCOUNT"), "tutor never reads another account's source");
await Controller().Solve(30, request);
Check(!handler.Prompts.Last().Contains("PRIVATE_SOURCE") && !handler.Prompts.Last().Contains("OTHER_ACCOUNT"), "switching notebook does not carry over its sources");
var pdfBuilder = new PdfDocumentBuilder();
var pdfFont = pdfBuilder.AddStandard14Font(Standard14Font.Helvetica);
var pdfPage = pdfBuilder.AddPage(595, 842);
pdfPage.AddText("Cau 1: Giai phuong trinh 5x + 2 = 17. Tim x va kiem tra nghiem.", 12, new PdfPoint(30, 780), pdfFont);
pdfPage.AddText("Cau 2: Tinh dien tich hinh chu nhat. De chua cho chieu dai va chieu rong.", 12, new PdfPoint(30, 750), pdfFont);
using var pdfStream = new MemoryStream(pdfBuilder.Build());
var uploadedPdf = await sources.AddFile(30, new FormFile(pdfStream, 0, pdfStream.Length, "file", "tutor-regression.pdf"));
Check(Status(uploadedPdf) == 200, "PDF upload extracts and stores text through the real source controller");
var pdfSources = await db.NotebookSources.Where(s => s.NotebookId == 30).ToListAsync();
Check(pdfSources.Single().Content.Contains("5x + 2 = 17"), "uploaded PDF retains the exercise text");
await Controller().Solve(30, new("Hướng dẫn tôi làm câu 1"));
Check(handler.Prompts.Last().Contains("5x + 2 = 17") && handler.Prompts.Last().Contains("Hướng dẫn tôi làm câu 1"), "short exercise reference includes the uploaded PDF in the AI request");
Check(handler.Prompts.Last().Contains("phải tìm đề tương ứng") && handler.Prompts.Last().Contains("một đề duy nhất"), "tutor prompt reads exercise references and asks when ambiguous");
Check(!handler.Prompts.Last().Contains("PRIVATE_SOURCE_CONTENT_SENTINEL") && !handler.Prompts.Last().Contains("OTHER_ACCOUNT_SECRET"), "PDF exercise request stays within the chosen notebook");
var longSource = new NotebookSource {Title = "Long source", Content = new string('a', 400000) + "TRUNCATED_END", Type = "text"};
Check(!TutorService.BuildPrompt(request, [longSource]).Contains("TRUNCATED_END"), "large sources use the existing bounded notebook context");
handler.Answer = "{\"answer\":\"\",\"needsClarification\":false}";
Check(Status(await Controller().Solve(10, request)) == 502, "empty model answer is retryable failure");
handler.Answer = "not json";
Check(Body(await Controller().Solve(10, request)).GetProperty("code").GetString() == "ai_invalid_response", "malformed provider JSON is handled");
handler.StatusCode = 429;
var quota = await Controller(new TutorService(Gemini())).Solve(10, request);
Check(Status(quota) == 429 && Body(quota).GetProperty("code").GetString() == "ai_rate_limited" && Body(quota).GetProperty("retryAfterSeconds").GetInt32() > 0, "quota preserves retry metadata, not a login error");
handler.StatusCode = 503;
Check(Status(await Controller(new TutorService(Gemini())).Solve(10, request)) == 503, "provider outage is handled with shared availability policy");
handler.StatusCode = 200;
handler.Answer = "{\"answer\":\"x = 4\",\"needsClarification\":false}";
Console.WriteLine($"{passed} checks passed (mock provider, SQLite in memory; no production data changed).");

if (args.Contains("--live") || args.Contains("--live-source")) {
    var apiDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../MindSprint.Api"));
    var liveCfg = new ConfigurationBuilder().AddJsonFile(Path.Combine(apiDir, "appsettings.json"), optional: false)
        .AddJsonFile(Path.Combine(apiDir, "appsettings.Development.json"), optional: true).AddEnvironmentVariables()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Gemini:MaxRetries"] = "0", ["Gemini:AttemptTimeoutSeconds"] = "15", ["Gemini:TotalTimeoutSeconds"] = "60" }).Build();
    var live = new TutorService(new GeminiService(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }, liveCfg, new GeminiAvailability(liveCfg), NullLogger<GeminiService>.Instance, new HttpContextAccessor()));
    var liveProblems = args.Contains("--live-source") ? new (string, TutorRequest)[] {
        ("Uploaded PDF question 1", new("Hướng dẫn tôi làm câu 1 trong tài liệu đã tải lên.")),
        ("Uploaded PDF missing facts", new("Giải câu 2 trong tài liệu."))
    } : new (string, TutorRequest)[] {
        ("Algebra steps", new("Giải phương trình 2x + 3 = 11.")),
        ("Algebra brief", new("Giải phương trình 3x - 6 = 9.", "brief")),
        ("Follow-up", new("Tại sao phải trừ 3 ở hai vế?", History: [new("user", "Giải 2x + 3 = 11"), new("assistant", "Trừ 3 ở hai vế: 2x = 8, chia hai vế cho 2: x = 4.")])),
        ("Missing facts", new("Tính diện tích hình chữ nhật."))
    };
    foreach (var (label, problem) in liveProblems) {
        try {
            var reply = await live.SolveAsync(problem, args.Contains("--live-source") ? pdfSources : null);
            Console.WriteLine("LIVE " + label + " clarification=" + reply.NeedsClarification);
            Console.WriteLine(reply.Answer);
        } catch (GeminiUnavailableException ex) { Console.WriteLine("LIVE unavailable " + label + ": " + ex.Code); Environment.ExitCode = 2; break; }
    }
}

sealed class Provider : HttpMessageHandler
{
    public readonly List<string> Prompts = [];
    public string Answer = "{\"answer\":\"Trừ 3 rồi chia 2: x = 4.\",\"needsClarification\":false}";
    public int StatusCode = 200;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        Prompts.Add(payload.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!);
        var response = new HttpResponseMessage((HttpStatusCode)StatusCode) {
            Content = new StringContent(JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = Answer } } } } } }), Encoding.UTF8, "application/json")
        };
        if (StatusCode == 429) response.Headers.TryAddWithoutValidation("Retry-After", "7");
        return response;
    }
}
sealed class Factory(Provider provider) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(provider); }
