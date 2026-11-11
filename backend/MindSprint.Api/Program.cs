using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MindSprint.Api;
using MindSprint.Api.Data;
using MindSprint.Api.Models;
using MindSprint.Api.Services;
using Microsoft.AspNetCore.Identity;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;
cfg["Gemini:ApiKey"] = SecurityConfiguration.GetGeminiApiKey(cfg);
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
var isProduction = builder.Environment.IsProduction();

var connectionString = SecurityConfiguration.GetConnectionString(cfg, isProduction);
var jwtKey = SecurityConfiguration.GetJwtKey(cfg, isProduction);
var corsOrigins = SecurityConfiguration.GetCorsOrigins(cfg, isProduction);

builder.Services.AddMindSprintDatabase(cfg, connectionString);
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", timeout: TimeSpan.FromSeconds(3));
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<TokenService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<GoogleLoginChallenges>();
builder.Services.AddScoped<IGoogleTokenValidator, GoogleTokenValidator>();
builder.Services.AddScoped<GoogleAccountService>();
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("google-auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
        }));
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            message = "Bạn đã thử đăng nhập nhiều lần. Hãy thử lại sau một phút.",
            code = "google_rate_limited", retryAfterSeconds = 60
        }, ct);
    };
});
builder.Services.AddScoped<SpacedRepetitionService>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<GeminiAvailability>();
builder.Services.AddHttpClient<GeminiService>(c => c.Timeout = Timeout.InfiniteTimeSpan)
    .RedactLoggedHeaders(_ => true);
builder.Services.AddHttpClient("web", c => c.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(NotebookUrlReader.CreateHandler);
builder.Services.AddScoped<NotebookAi>();
builder.Services.AddScoped<TutorService>();
builder.Services.AddScoped<TutorOcrService>();
builder.Services.AddSingleton<INotebookPageRenderer, NotebookPageRenderer>();
builder.Services.AddScoped<NotebookUrlImporter>();
builder.Services.AddScoped<WeakAreaService>();

// Render sets PORT dynamically. Bind to all interfaces so the platform's proxy can reach Kestrel.
if (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var port) && port is > 0 and <= 65535)
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = cfg["Jwt:Issuer"],
        ValidateAudience = true, ValidAudience = cfg["Jwt:Audience"],
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateLifetime = true
    };
});
builder.Services.AddAuthorization();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (corsOrigins.Length > 0) p.WithOrigins(corsOrigins);
    p.AllowAnyHeader().AllowAnyMethod();
}));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
    await db.Database.MigrateAsync();      // tạo/cập nhật DB theo Migrations (Code First)
    await DataSeeder.SeedAsync(db, hasher, includeDemoUser: !isProduction); // Không tạo tài khoản demo có mật khẩu mặc định ở Production.
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler(handler => handler.Run(async context =>
    {
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new { message = "Máy chủ chưa xử lý được yêu cầu. Hãy thử lại sau.", code = "server_error" });
    }));
}
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});
if (cfg.GetValue<bool>("Hosting:ServeFrontend"))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapHealthChecks("/readyz", new HealthCheckOptions
{
    ResponseWriter = (context, report) => context.Response.WriteAsJsonAsync(new
    {
        status = report.Status == HealthStatus.Healthy ? "ready" : "unavailable"
    }, context.RequestAborted)
}).AllowAnonymous();
app.Run();
