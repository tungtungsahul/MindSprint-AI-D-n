using System.Diagnostics;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

// (NK3) Một instance theo scope; chỉ nhận mã đối tượng, tuyệt đối không chuyển form/lý do tự do vào Note.
public sealed class AuditLog(AppDbContext db, IHttpContextAccessor accessor, IServiceScopeFactory scopes,
    TimeProvider clock, ILogger<AuditLog> logger)
{
    private readonly HashSet<(AuditAction, string, string)> recorded = [];
    public bool HasFailure { get; private set; }
    public AuditAction? PasswordAction { get; set; }
    public void Attach() => db.Audit = this;
    public void Warn() => logger.LogWarning("Audit write failed; business operation continues");
    public static string RequestId(HttpContext? http) => Activity.Current?.Id ?? http?.TraceIdentifier ?? "";

    // (NK3) Chưa có màn hình khôi phục lớp/câu riêng. Hook service vẫn bao phủ thay đổi
    // khôi phục được thêm sau này, không cần mở thêm quyền hay tạo một luồng nghiệp vụ khác.
    public void CaptureRestores()
    {
        foreach (var entry in db.ChangeTracker.Entries<Classroom>().Where(e => e.State == EntityState.Modified).ToList())
            if (entry.Property(e => e.DeletedAt).OriginalValue != null && entry.Entity.DeletedAt == null)
                Add(AuditAction.ClassroomRestored, "Classroom", entry.Entity.Id);
        foreach (var entry in db.ChangeTracker.Entries<Question>().Where(e => e.State == EntityState.Modified).ToList())
            if (entry.Property(e => e.DeletedAt).OriginalValue != null && entry.Entity.DeletedAt == null)
                Add(AuditAction.QuestionRestored, "Question", entry.Entity.Id);
    }

    public async Task RejectedAsync(Exception exception, string? controllerAction = null)
    {
        var http = accessor.HttpContext;
        if (http == null) return;
        // (NK1) Một lỗi có thể đi qua catch của controller rồi filter; chỉ ghi loại và đường dẫn một lần.
        if (!ReferenceEquals(http.Items["LoggedBusinessException"], exception))
        {
            http.Items["LoggedBusinessException"] = exception;
            logger.LogInformation("Business rejection {ExceptionType} at {RequestPath}", exception.GetType().Name, LogRedaction.StripQuery(http.Request.Path));
        }
        if (HttpMethods.IsPost(http.Request.Method) && !HasFailure)
            await WriteSeparateAsync(AuditAction.ActionRejected, "ControllerAction",
                controllerAction ?? http.GetRouteValue("controller") + "/" + http.GetRouteValue("action"),
                result: exception is ForbiddenException ? AuditResult.Denied : AuditResult.Failed);
    }

    public void Add(AuditAction action, string targetType, object targetId, string? actorId = null,
        AuditResult result = AuditResult.Success, Func<string>? resolveTarget = null)
    {
        try
        {
            var row = Create(action, targetType, targetId.ToString() ?? "", actorId, result);
            var key = (action, row.TargetType, row.TargetId);
            if (resolveTarget != null && row.TargetId == "0") key = (action, row.TargetType, Guid.NewGuid().ToString());
            if (!recorded.Add(key)) return;
            if (resolveTarget != null) row.ResolveTarget = () =>
            {
                var resolved = resolveTarget(); recorded.Remove(key); recorded.Add((action, row.TargetType, resolved)); return resolved;
            };
            db.AuditEntries.Add(row);
        }
        catch { Warn(); }
    }

    // (NK3) Lỗi/không có transaction: context riêng không vô tình commit dữ liệu nghiệp vụ.
    public async Task WriteSeparateAsync(AuditAction action, string targetType, object targetId,
        string? actorId = null, AuditResult result = AuditResult.Success)
    {
        try
        {
            var row = Create(action, targetType, targetId.ToString() ?? "", actorId, result);
            if (!recorded.Add((action, row.TargetType, row.TargetId))) return;
            using var scope = scopes.CreateScope();
            var separate = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            separate.AuditEntries.Add(row);
            await separate.SaveChangesAsync();
            if (result != AuditResult.Success) HasFailure = true;
        }
        catch { Warn(); }
    }

    private AuditEntry Create(AuditAction action, string type, string id, string? actorId, AuditResult result)
    {
        var http = accessor.HttpContext;
        actorId ??= http?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var guest = actorId != null && (GuestAccess.IsGuest(http?.User)
            || db.Users.Local.Any(u => u.Id == actorId && u.IsGuest));
        var guestId = guest ? http?.Items["AuditGuestId"]?.ToString()
            ?? db.Guests.Local.FirstOrDefault(g => g.UserId == actorId)?.Id.ToString() ?? actorId : null;
        return new() { At = clock.GetUtcNow().UtcDateTime, Action = action, TargetType = Limit(type, 50),
            TargetId = Limit(id, 64), ActorUserId = guest ? null : actorId, ActorGuestId = guestId,
            ActorKind = guest ? AuditActorKind.Guest : actorId == null ? AuditActorKind.System : AuditActorKind.User,
            Result = result, RequestId = Limit(RequestId(http), 100) };
    }
    private static string Limit(string text, int max) => text[..Math.Min(text.Length, max)];
}

// (NK3) Khởi tạo trước Authentication để mọi service dùng chung context nhật ký.
public sealed class AuditScopeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AuditLog audit)
    {
        audit.Attach();
        await next(context);
        // (NK3) Bao gồm từ chối ở Authorization/NotGuest trước khi vào service/filter nghiệp vụ.
        if (HttpMethods.IsPost(context.Request.Method) && context.Response.StatusCode == 403 && !audit.HasFailure)
            await audit.WriteSeparateAsync(AuditAction.ActionRejected, "ControllerAction",
                context.GetRouteValue("controller") + "/" + context.GetRouteValue("action"), result: AuditResult.Denied);
    }
}
