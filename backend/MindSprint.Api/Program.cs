using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MindSprint.Api.Data;
using MindSprint.Api.Models;
using MindSprint.Api.Services;
using Microsoft.AspNetCore.Identity;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(cfg.GetConnectionString("Default")));
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
builder.Services.AddHttpClient<GeminiService>(c => c.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddHttpClient("web", c => c.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(NotebookUrlReader.CreateHandler);
builder.Services.AddScoped<NotebookAi>();
builder.Services.AddScoped<TutorService>();
builder.Services.AddSingleton<INotebookPageRenderer, NotebookPageRenderer>();
builder.Services.AddScoped<NotebookUrlImporter>();

builder.WebHost.UseUrls("http://localhost:5000", "http://localhost:5100");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = cfg["Jwt:Issuer"],
        ValidateAudience = true, ValidAudience = cfg["Jwt:Audience"],
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Jwt:Key"]!)),
        ValidateLifetime = true
    };
    o.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = ctx =>
        {
            Console.WriteLine($"[AUTH ERROR] OnAuthenticationFailed: {ctx.Exception}");
            return Task.CompletedTask;
        },
        OnChallenge = ctx =>
        {
            Console.WriteLine($"[AUTH CHALLENGE] Error: {ctx.Error}, Desc: {ctx.ErrorDescription}");
            return Task.CompletedTask;
        },
        OnTokenValidated = ctx =>
        {
            Console.WriteLine($"[AUTH SUCCESS] Token validated for: {ctx.Principal?.Identity?.Name}");
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
    await db.Database.MigrateAsync();      // tạo/cập nhật DB theo Migrations (Code First)
    await DataSeeder.SeedAsync(db, hasher); // bơm từ vựng và tạo tài khoản demo
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.Run();
