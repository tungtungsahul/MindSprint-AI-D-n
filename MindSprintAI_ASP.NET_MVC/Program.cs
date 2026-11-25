using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;
using MindSprintAI_ASP.NET_MVC.Services;
using MindSprintAI_ASP.NET_MVC.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.DataProtection;
using Serilog;
using Microsoft.AspNetCore.HttpOverrides;

// Lỗi của chính Serilog (vd không mở được file nhật ký) mặc định bị nuốt im lặng; in ra stderr để thấy trong journalctl.
Serilog.Debugging.SelfLog.Enable(Console.Error);
// (NK1) Bắt lỗi cả trước khi host hoàn tất; cấu hình host thay bootstrap sau Build.
Log.Logger = SystemLogging.Configure(new LoggerConfiguration(),
    new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: true).AddEnvironmentVariables().Build(),
    Directory.GetCurrentDirectory()).CreateBootstrapLogger();
try
{

// Ô số HTML luôn gửi dấu chấm ("1.5"); máy đặt vùng Việt Nam sẽ đọc sai thành 15 nếu dùng văn hóa máy.
// Chữ hiển thị tiếng Việt (dấu phẩy) định dạng riêng ở ScoreFormatter.
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, services, log) =>
    SystemLogging.Configure(log, context.Configuration, context.HostingEnvironment.ContentRootPath));

// Bảo mật mục 1 (người dùng duyệt 2026-11-06), xem docs/BAO_MAT_TRIEN_KHAI.md:
// - Tên ứng dụng cố định: đổi thư mục triển khai vẫn giải mã được key/cookie (mặc định gắn theo đường dẫn).
// - Mã hóa chính các file khóa: có chứng chỉ thì dùng chứng chỉ (server Linux), không thì DPAPI trên Windows.
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("MindSprintAI")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "dataprotection-keys")));
var keyCertificate = builder.Configuration["DataProtection:CertificatePath"];
var keyRingProtected = true;
if (!string.IsNullOrWhiteSpace(keyCertificate))
    dataProtection.ProtectKeysWithCertificate(System.Security.Cryptography.X509Certificates.X509CertificateLoader
        .LoadPkcs12FromFile(keyCertificate, builder.Configuration["DataProtection:CertificatePassword"]));
else if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi();
else keyRingProtected = false;

// Add services to the container.
builder.Services.AddSingleton<SlowSqlInterceptor>();
builder.Services.AddDbContext<AppDbContext>((sp, o) =>
    o.UseSqlServer(AppDbContextFactory.AuditConnectionString(builder.Configuration.GetConnectionString("Default")))
        .AddInterceptors(sp.GetRequiredService<SlowSqlInterceptor>()));
builder.Services.AddScoped<AuditLog>();
builder.Services.AddScoped<AccountLifecycleService>();
builder.Services.AddScoped<AccountDeletionService>();
builder.Services.AddScoped<AuditQueryService>();
builder.Services.AddHostedService<LogRetentionWorker>();

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
    {
        o.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        o.SignIn.RequireConfirmedEmail = true;          // 55
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 8;                  // bảo mật mục 3 (2026-11-06): mật khẩu cũ ngắn hơn vẫn đăng nhập được, chỉ áp khi đặt mới
        o.Lockout.MaxFailedAccessAttempts = 10;         // 55: sai 10 lần khóa 15 phút
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddUserStore<ProtectedUserStore>()
    .AddDefaultTokenProviders()
    .AddErrorDescriber<VietnameseIdentityErrors>();

builder.Services.Configure<IdentityPasskeyOptions>(o =>
{
    o.ServerDomain = string.IsNullOrWhiteSpace(builder.Configuration["Passkeys:ServerDomain"]) ? null : builder.Configuration["Passkeys:ServerDomain"];
    o.UserVerificationRequirement = "required";
    o.ResidentKeyRequirement = "required"; // Đăng nhập không cần nhập email.
});
builder.Services.AddAuthentication().AddCookie(TwoFactorService.PasskeyPendingScheme, o =>
{
    o.Cookie.Name = ".MindSprintAI.PasskeyPending";
    o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = CookieSecurePolicy.Always; o.Cookie.SameSite = SameSiteMode.Strict;
    o.ExpireTimeSpan = TimeSpan.FromMinutes(5); o.SlidingExpiration = false;
});

// Đổi mật khẩu/đặt lại → thiết bị khác bị đăng xuất trong vòng 1 phút (55).
builder.Services.AddDeviceSessions(); // (T7, T10) kiểm phiên cùng nhịp stamp và giữ claim khi refresh.
builder.Services.AddAuthentication().AddQuizExternalProviders(builder.Configuration);
builder.Services.Configure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, o =>
{
    o.ExpireTimeSpan = TimeSpan.FromMinutes(5);
    o.SlidingExpiration = false;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    // Lax, không Strict: 6c đăng nhập Google/Microsoft quay về qua chuyển hướng từ trang khác; Strict thì trình duyệt
    // không gửi cookie này ở bước 2 → luôn "hết hạn". Lax vẫn chặn POST từ trang khác.
    o.Cookie.SameSite = SameSiteMode.Lax;
});
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/Login";
    o.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
});
// Yêu cầu fetch (trả lời câu hỏi) gửi token chống giả mạo qua header này.
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "RequestVerificationToken";
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.Configure<ForwardedHeadersOptions>(o => DeploymentSecurity.ConfigureProxy(o, builder.Configuration));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSignalR(o => { o.AddFilter<NotGuestHubFilter>(); o.AddFilter<DeviceSessionHubFilter>(); });
builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, GuestClaimsFactory>();
builder.Services.AddScoped<GuestService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ILearnDevice, LearnDevice>();
builder.Services.AddSingleton<DeviceSignalKey>();
builder.Services.AddScoped<DeviceSignalService>();
builder.Services.AddHostedService<ScreenMonitorWorker>();
builder.Services.AddSingleton<LearnConnectionTracker>();
builder.Services.AddSingleton<ILearnNotifier, SignalRLearnNotifier>();
builder.Services.AddScoped<LearnConnectionService>();
builder.Services.AddHostedService<LearnStartupService>();
builder.Services.AddHostedService<JoinCodeCleanupService>();
builder.Services.AddEmail(builder.Configuration, builder.Environment); // có Smtp:* thì gửi thật (7a), không thì ghi file
builder.Services.AddScoped<PasswordResetService>();
builder.Services.AddScoped<AccountEmailService>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<TurnstileService>(http => http.Timeout = TimeSpan.FromSeconds(10));
// Không chạy bất kỳ HttpClient logger nào với key cá nhân, kể cả khi bật Trace toàn ứng dụng.
builder.Services.AddGeminiClient();
builder.Services.AddScoped<UserApiKeyService>();
builder.Services.AddScoped<SourceTextService>();
builder.Services.AddScoped<AiAssistantService>();
builder.Services.AddScoped<LoginService>();
builder.Services.AddScoped<TwoFactorService>();
builder.Services.AddScoped<PasskeyService>();
builder.Services.AddScoped<ExternalLoginService>();
builder.Services.AddScoped<ProfileService>();
builder.Services.AddScoped<AvatarService>();
builder.Services.AddScoped<JoinCodeGuardService>();
builder.Services.AddScoped<AccountLockService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<ContentReportService>();
builder.Services.AddScoped<UnhideRequestService>();
builder.Services.AddScoped<IAuthorizationHandler, SystemAdminHandler>();
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("NotGuest", p => p.RequireAssertion(c => !GuestAccess.IsGuest(c.User)));
    o.AddPolicy("SystemAdmin", p => p.RequireAuthenticatedUser().AddRequirements(new SystemAdminRequirement()));
});
builder.Services.AddAccountRateLimits();
builder.Services.AddSingleton<IQuestionGrader, ChoiceGrader>();
builder.Services.AddSingleton<IQuestionGrader, TextInputGrader>();
builder.Services.AddSingleton<IQuestionGrader, NumberInputGrader>();
builder.Services.AddSingleton<IQuestionGrader, SurveyGrader>();
builder.Services.AddScoped<JoinCodeService>();
builder.Services.AddScoped<StudyLibraryService>();
builder.Services.AddScoped<UserSettingPresetService>();
builder.Services.AddScoped<KanbanService>();
builder.Services.AddScoped<HomeService>();
builder.Services.AddScoped<StudyScheduleService>();
builder.Services.AddScoped<StudyStatsService>();
builder.Services.AddSingleton<VocabCatalog>();
builder.Services.AddScoped<VocabLibraryService>();
builder.Services.AddScoped<WeakAreaService>();
builder.Services.AddScoped<OverviewService>();
builder.Services.AddScoped<AiGenerateService>();
builder.Services.AddScoped<NotebookService>();
builder.Services.AddScoped<NotebookAiService>();
// Đọc nguồn Sổ tay từ đường link: chỉ địa chỉ công khai, không tự chuyển hướng (NotebookUrlReader kiểm từng bước).
builder.Services.AddHttpClient(NotebookUrlReader.ClientName, http => http.Timeout = TimeSpan.FromSeconds(20)).ConfigurePrimaryHttpMessageHandler(NotebookUrlReader.CreateHandler);
builder.Services.AddScoped<TrashService>();
builder.Services.AddScoped<TrashPurgeService>();
builder.Services.AddHostedService<TrashPurgeWorker>();
builder.Services.AddScoped<RegradeService>();
builder.Services.AddScoped<ClassroomService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<EmailLinks>();
builder.Services.AddScoped<InvitationService>();
builder.Services.AddSingleton<GeoIpLocation>();
builder.Services.AddScoped<NewDeviceService>();
builder.Services.AddScoped<ReminderService>();
builder.Services.AddHostedService<ReminderWorker>();
builder.Services.AddScoped<StudySetCloneService>();
builder.Services.AddScoped<ClassTransferService>();
builder.Services.AddScoped<LecturerPromotionService>();
builder.Services.AddHostedService<LecturerPromotionWorker>();
builder.Services.AddScoped<AssignmentService>();
builder.Services.AddScoped<AttemptService>();
builder.Services.AddSingleton<PaperFileStore>();
builder.Services.AddScoped<PaperTestService>();
builder.Services.AddScoped<RankingService>();
builder.Services.AddScoped<ClassDemoSeedService>();
builder.Services.AddSingleton<DemoJobRunner>();
builder.Services.AddScoped<GradebookService>();
builder.Services.AddScoped<RetakeService>();
builder.Services.AddScoped<FlashcardService>();
builder.Services.AddScoped<ExcelImportService>();
builder.Services.AddScoped<DemoSeedService>();
builder.Services.AddScoped<SurveyService>();

builder.Services.AddControllersWithViews(o =>
{
    o.Filters.Add<BusinessExceptionFilter>();
    o.Filters.Add<NotGuestFilter>();
    o.Filters.Add<PolicyGateFilter>();
    o.Filters.Add<StaleFormFilter>();
    o.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
});

var app = builder.Build();
app.Logger.LogInformation("Host starting {Environment} {Version}", app.Environment.EnvironmentName,
    typeof(Brand).Assembly.GetName().Version?.ToString()); // (NK1)
// (NK1) Chỉ boolean; không ghi địa chỉ, đường dẫn khóa hay nội dung cấu hình.
app.Logger.LogInformation("Features SMTP={Smtp} Google={Google} Microsoft={Microsoft} GeoIP={GeoIp} Turnstile={Turnstile} DataProtection={DataProtection}",
    app.Configuration.GetSection("Smtp").Get<SmtpSettings>()?.Configured ?? false,
    !string.IsNullOrWhiteSpace(app.Configuration["Authentication:Google:ClientId"]) && !string.IsNullOrWhiteSpace(app.Configuration["Authentication:Google:ClientSecret"]),
    !string.IsNullOrWhiteSpace(app.Configuration["Authentication:Microsoft:ClientId"]) && !string.IsNullOrWhiteSpace(app.Configuration["Authentication:Microsoft:ClientSecret"]),
    File.Exists(Path.GetFullPath(app.Configuration["GeoIp:CityDatabasePath"] ?? "App_Data/geoip/dbip-city-lite.mmdb", app.Environment.ContentRootPath)),
    !string.IsNullOrWhiteSpace(app.Configuration["Turnstile:SecretKey"]) && !string.IsNullOrWhiteSpace(app.Configuration["Turnstile:SiteKey"]),
    Directory.Exists(Path.Combine(app.Environment.ContentRootPath, "App_Data", "dataprotection-keys")));

// Bảo mật mục 1–2: báo ngay lúc khởi động nếu chạy thật mà cấu hình còn yếu (không in giá trị bí mật).
DeploymentSecurity.Validate(app.Configuration, app.Environment.IsDevelopment(), app.Logger);
if (!app.Environment.IsDevelopment())
{
    if (!keyRingProtected)
        app.Logger.LogWarning("Khóa Data Protection chưa được mã hóa: đặt DataProtection__CertificatePath/__CertificatePassword và chmod 700 thư mục App_Data.");
    if (!(app.Configuration.GetSection("Smtp").Get<SmtpSettings>()?.Configured ?? false))
        app.Logger.LogWarning("Chưa cấu hình SMTP: email chỉ ghi ra App_Data/emails, người dùng không nhận được. Đặt Smtp__Host/__UserName/__Password.");
}

app.UseForwardedHeaders();
// Như ApplicationBuilderExtensions của nhóm: trình duyệt không đoán kiểu tệp, không gửi đường dẫn đầy đủ sang trang khác.
app.Use((ctx, next) =>
{
    ctx.Response.Headers.XContentTypeOptions = "nosniff";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    return next();
});
// (NK1) Ngoài ExceptionHandler: thấy mã trả về cuối và user sau Authentication.
app.UseSerilogRequestLogging(o =>
{
    o.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    o.EnrichDiagnosticContext = SystemLogging.EnrichRequest;
    o.GetLevel = (_, _, _) => Serilog.Events.LogEventLevel.Information;
});
app.UseMiddleware<AuditScopeMiddleware>();
// (NK1) Viết trang lỗi trực tiếp, không cần cơ sở dữ liệu (xem Services/ErrorPage.cs).
app.UseExceptionHandler(new ExceptionHandlerOptions { ExceptionHandler = ErrorPage.WriteAsync, SuppressDiagnosticsCallback = _ => true });
app.UseMiddleware<RequestExceptionCaptureMiddleware>();
// 404/403 rỗng khi mở trang -> trang "Không tìm thấy trang" / "Không có quyền" (ErrorPage.WantsStatusPage); tệp tĩnh, fetch/JSON giữ mã trơn.
app.UseStatusCodePages(status => ErrorPage.WantsStatusPage(status.HttpContext)
    ? ErrorPage.WriteStatusPageAsync(status.HttpContext) : Task.CompletedTask);
// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseMiddleware<LearnDeviceMiddleware>();

app.UseAuthentication();
app.UseMiddleware<GuestSessionMiddleware>();
app.UseMiddleware<AccountLockMiddleware>();
app.UseMiddleware<AdminTwoFactorMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();

app.MapStaticAssets();
app.MapHub<LearnHub>("/hubs/learn");
// NFR-08 của nhóm: /healthz = app còn sống; /readyz = nối được database (503 nếu không, không lộ cấu hình).
app.MapGet("/healthz", () => Results.Text("ok"));
app.MapGet("/readyz", async (AppDbContext db, CancellationToken ct) =>
{
    try { if (await db.Database.CanConnectAsync(ct)) return Results.Text("ready"); } catch { }
    return Results.Text("not ready", statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


// Dữ liệu mẫu lớp học từ dòng lệnh (giống 2 nút ở trang Quản trị), chạy xong thì thoát, không mở web:
//   dotnet MindSprintAI_ASP.NET_MVC.dll --seed-class-demo <email giảng viên>     (tạo)
//   dotnet MindSprintAI_ASP.NET_MVC.dll --remove-class-demo <email quản trị viên> (xóa đúng phần dữ liệu mẫu)
// Dùng ở máy dev (trang Quản trị đòi bật 2FA) hoặc khi không vào được trang Quản trị.
if (args is [("--seed-class-demo" or "--remove-class-demo") and var demoCommand, var demoEmail])
{
    using var scope = app.Services.CreateScope();
    var sp = scope.ServiceProvider;
    var owner = await sp.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(demoEmail)
        ?? throw new InvalidOperationException("Không tìm thấy tài khoản " + demoEmail);
    var demo = sp.GetRequiredService<ClassDemoSeedService>();
    if (demoCommand == "--remove-class-demo")
        Console.WriteLine($"Đã xóa dữ liệu mẫu (tài khoản mẫu: {await demo.RemoveAsync(owner.Id, sp.GetRequiredService<AccountDeletionService>(), sp.GetRequiredService<ClassroomService>(),
            sp.GetRequiredService<AssignmentService>(), sp.GetRequiredService<StudyLibraryService>())}).");
    else
    {
        var r = await demo.SeedAsync(owner.Id);
        Console.WriteLine($"Đã tạo {r.Students} học viên mẫu, {r.Classes} lớp, {r.Assignments} bài giao, {r.Attempts} lượt làm. "
            + $"Mật khẩu chung và danh sách tài khoản: App_Data/{ClassDemoSeedService.AccountsFile}");
    }
    return;
}
app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host stopped unexpectedly");
    throw;
}
finally { Log.Information("Host stopped"); Log.CloseAndFlush(); }
