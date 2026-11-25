using System.Data.Common;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace MindSprintAI_ASP.NET_MVC.Services;

public static class LogRedaction
{
    public static string MaskEmail(string? email)
    {
        var at = email?.LastIndexOf('@') ?? -1;
        if (at <= 0 || at == email!.Length - 1) return "***";
        return email[..Math.Min(4, at)] + "***" + email[at..];
    }
    public static string StripQuery(string? path) => (path ?? "").Split('?', '#')[0];
    public static string EmailDomain(string? email)
    {
        var at = email?.LastIndexOf('@') ?? -1;
        return at > 0 && Regex.IsMatch(email![(at + 1)..], @"^[a-zA-Z0-9.-]+$") ? email![(at + 1)..] : "unknown";
    }
    internal static bool Sensitive(string name) => Regex.IsMatch(name,
        "Password|Secret|Token|Code|Key|Cookie|Authorization|RequestBody|Headers", RegexOptions.IgnoreCase)
        && name != "StatusCode";
}

// (NK2) Cả thuộc tính đính trực tiếp và object lồng nhau được lọc trước mọi sink.
public sealed class RedactionEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var property in logEvent.Properties.ToList())
        {
            if (LogRedaction.Sensitive(property.Key)) logEvent.RemovePropertyIfPresent(property.Key);
            else logEvent.AddOrUpdateProperty(new(property.Key, Named(property.Key, property.Value)));
        }
    }
    private static LogEventPropertyValue Named(string name, LogEventPropertyValue value) =>
        name.Contains("Email", StringComparison.OrdinalIgnoreCase) && value is ScalarValue { Value: string email }
            ? new ScalarValue(LogRedaction.MaskEmail(email)) : Clean(value);
    private static LogEventPropertyValue Clean(LogEventPropertyValue value) => value switch
    {
        StructureValue s => new StructureValue(s.Properties.Where(p => !LogRedaction.Sensitive(p.Name))
            .Select(p => new LogEventProperty(p.Name, Named(p.Name, p.Value))), s.TypeTag),
        SequenceValue s => new SequenceValue(s.Elements.Select(Clean)),
        DictionaryValue d => new DictionaryValue(d.Elements.Where(p => !LogRedaction.Sensitive(p.Key.Value?.ToString() ?? ""))
            .Select(p => new KeyValuePair<ScalarValue, LogEventPropertyValue>(p.Key, Named(p.Key.Value?.ToString() ?? "", p.Value)))),
        _ => value
    };
}

public static class SystemLogging
{
    public const long FileSizeLimit = 50L * 1024 * 1024;
    public const int RetentionDays = 30;
    public static string DirectoryPath(IConfiguration config, string root) =>
        Path.GetFullPath(config["Logs:Directory"] ?? Path.Combine("App_Data", "logs"), root);

    public static LoggerConfiguration Configure(LoggerConfiguration log, IConfiguration config, string root)
    {
        var directory = DirectoryPath(config, root);
        Directory.CreateDirectory(directory);
        return log.MinimumLevel.Information().MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            // Lý do mã chống giả mạo bị từ chối (cookie thiếu / không khớp / khác người dùng) chỉ được ghi ở mức Information.
            .MinimumLevel.Override("Microsoft.AspNetCore.Mvc.ViewFeatures.Filters", LogEventLevel.Information)
            // (NK1) ExceptionHandler chuyển exception vào event truy cập duy nhất.
            .Filter.ByExcluding(e => e.Properties.TryGetValue("SourceContext", out var source)
                && source.ToString().Contains("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware"))
            .Enrich.FromLogContext().Enrich.With<RedactionEnricher>()
            .WriteTo.Console(new CompactJsonFormatter())
            .WriteTo.File(new CompactJsonFormatter(), Path.Combine(directory, "mindsprint-.log"),
                rollingInterval: RollingInterval.Day, fileSizeLimitBytes: FileSizeLimit, rollOnFileSizeLimit: true,
                retainedFileCountLimit: null, retainedFileTimeLimit: TimeSpan.FromDays(RetentionDays),
                // shared cần named mutex: trên Linux trong dịch vụ systemd không tạo được, file sink báo "locked" rồi bỏ ghi
                // (Debian 13/11/2026). Linux không khóa file khi đọc nên một tiến trình ghi là đủ; Windows giữ shared để đọc được khi đang ghi.
                buffered: false, shared: OperatingSystem.IsWindows());
    }

    public static void EnrichRequest(IDiagnosticContext diagnostic, HttpContext http)
    {
        diagnostic.Set("RequestPath", LogRedaction.StripQuery(http.Request.Path));
        diagnostic.Set("UserId", http.User.FindFirstValue(ClaimTypes.NameIdentifier));
        diagnostic.Set("GuestId", http.Items["AuditGuestId"]);
        diagnostic.Set("RemoteIp", http.Connection.RemoteIpAddress?.ToString());
        diagnostic.Set("RequestId", AuditLog.RequestId(http));
    }
}

// (NK1) Chỉ chuyển exception vào sự kiện truy cập; không in thêm một bản ở đây.
public sealed class RequestExceptionCaptureMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, IDiagnosticContext diagnostic)
    {
        try { await next(http); }
        catch (Exception ex)
        {
            diagnostic.SetException(ex);
            diagnostic.Set("RequestId", AuditLog.RequestId(http));
            throw;
        }
    }
}

// (NK1) EF mặc định không ghi giá trị tham số. Raw SQL trong dự án luôn dùng tham số.
public sealed class SlowSqlInterceptor(ILogger<SlowSqlInterceptor> logger) : DbCommandInterceptor
{
    private void Completed(DbCommand command, CommandExecutedEventData data)
    {
        if (data.Duration > TimeSpan.FromSeconds(1))
            logger.LogWarning("Slow SQL {DurationMs} ms: {CommandText}", data.Duration.TotalMilliseconds, command.CommandText);
    }
    public override DbDataReader ReaderExecuted(DbCommand c, CommandExecutedEventData d, DbDataReader r) { Completed(c, d); return r; }
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand c, CommandExecutedEventData d, DbDataReader r, CancellationToken ct = default)
    { Completed(c, d); return ValueTask.FromResult(r); }
    public override int NonQueryExecuted(DbCommand c, CommandExecutedEventData d, int r) { Completed(c, d); return r; }
    public override ValueTask<int> NonQueryExecutedAsync(DbCommand c, CommandExecutedEventData d, int r, CancellationToken ct = default)
    { Completed(c, d); return ValueTask.FromResult(r); }
    public override object? ScalarExecuted(DbCommand c, CommandExecutedEventData d, object? r) { Completed(c, d); return r; }
    public override ValueTask<object?> ScalarExecutedAsync(DbCommand c, CommandExecutedEventData d, object? r, CancellationToken ct = default)
    { Completed(c, d); return ValueTask.FromResult(r); }
}
