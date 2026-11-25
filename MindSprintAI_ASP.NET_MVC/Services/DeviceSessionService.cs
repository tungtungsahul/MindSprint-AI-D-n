using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MindSprintAI_ASP.NET_MVC.Data;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public record DeviceRow(long Id, string Label, DateTime FirstSeenAt, DateTime LastSeenAt,
    string? LastSignInIp, string? LastLocation, bool IsCurrent);
public record DevicePage(IReadOnlyList<DeviceRow> Devices);

public static class DeviceSession
{
    // (T7) id hàng không tái sử dụng; cookie Identity được bảo vệ, không lấy mã phiên từ qem_device.
    public const string Claim = "qem_device_session";
    public static long? Id(ClaimsPrincipal? principal)
    {
        var claims = principal?.FindAll(Claim).ToArray();
        return claims?.Length == 1 && long.TryParse(claims[0].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
    }

    public static void Preserve(ClaimsPrincipal? current, ClaimsPrincipal? fresh)
    {
        if (Id(current) is long id && fresh?.Identity is ClaimsIdentity identity
            && current!.FindFirstValue(ClaimTypes.NameIdentifier) == fresh.FindFirstValue(ClaimTypes.NameIdentifier))
        {
            foreach (var old in identity.FindAll(Claim).ToArray()) identity.RemoveClaim(old);
            identity.AddClaim(new(Claim, id.ToString(CultureInfo.InvariantCulture)));
        }
    }

    public static IServiceCollection AddDeviceSessions(this IServiceCollection services)
    {
        services.AddScoped<DeviceSessionService>();
        services.Replace(ServiceDescriptor.Scoped<ISecurityStampValidator, DeviceSecurityStampValidator>());
        services.Configure<SecurityStampValidatorOptions>(o =>
        {
            o.ValidationInterval = TimeSpan.FromMinutes(1);
            o.OnRefreshingPrincipal = c =>
            {
                TwoFactorService.PreserveSignInTime(c.CurrentPrincipal, c.NewPrincipal);
                Preserve(c.CurrentPrincipal, c.NewPrincipal);
                return Task.CompletedTask;
            };
        });
        return services;
    }
}

public sealed class DeviceSessionService(AppDbContext db)
{
    // (T7, T10) fail closed khi thiếu claim; kiểm cả chủ sở hữu, không dựa cookie nhận diện trình duyệt.
    public Task<bool> IsValidAsync(ClaimsPrincipal? principal)
    {
        var user = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var id = DeviceSession.Id(principal);
        return id == null || user == null ? Task.FromResult(false)
            : db.KnownDevices.AnyAsync(d => d.Id == id && d.UserId == user);
    }

    public async Task<DevicePage> ListAsync(string userId, ClaimsPrincipal principal)
    {
        if (principal.FindFirstValue(ClaimTypes.NameIdentifier) != userId) throw new ForbiddenException();
        var current = DeviceSession.Id(principal);
        // (T6) quyền và sắp xếp ở service; view chỉ hiển thị.
        return new(await db.KnownDevices.AsNoTracking().Where(d => d.UserId == userId)
            .OrderByDescending(d => d.LastSeenAt).ThenByDescending(d => d.Id)
            .Select(d => new DeviceRow(d.Id, d.Label, d.FirstSeenAt, d.LastSeenAt, d.LastSignInIp, d.LastLocation, d.Id == current)).ToListAsync());
    }

    public async Task DeleteAsync(string userId, ClaimsPrincipal principal, long? deviceId)
    {
        if (principal.FindFirstValue(ClaimTypes.NameIdentifier) != userId) throw new ForbiddenException();
        // (T7, T9) cùng khóa với đăng nhập: không ghi lại IP/row vừa xóa từ entity cũ.
        await using var tx = await db.Database.BeginTransactionAsync();
        await Tang7cLock.AcquireAsync(db, "device:" + userId);
        if (!await IsValidAsync(principal)) throw new ForbiddenException();
        var current = DeviceSession.Id(principal)!.Value;
        if (deviceId == current) throw new BusinessException("Không thể xóa thiết bị này. Hãy dùng Đăng xuất.");
        var targets = db.KnownDevices.Where(d => d.UserId == userId && d.Id != current);
        if (deviceId != null) targets = targets.Where(d => d.Id == deviceId);
        var deleted = await targets.ExecuteDeleteAsync();
        if (deviceId != null && deleted == 0) throw new ForbiddenException(); // không lộ thiết bị người khác có tồn tại.
        db.Audit?.Add(deviceId == null ? AuditAction.OtherDevicesSignedOut : AuditAction.DeviceDeleted, "User", userId, userId); // (NK3)
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}

// (T7) chỉ chạy kiểm DB khi Identity thực sự kiểm stamp (1 phút), không thêm query mỗi request.
public sealed class DeviceSecurityStampValidator(IOptions<SecurityStampValidatorOptions> options,
    SignInManager<ApplicationUser> signIn, ILoggerFactory logger, DeviceSessionService sessions)
    : SecurityStampValidator<ApplicationUser>(options, signIn, logger)
{
    protected override async Task<ApplicationUser?> VerifySecurityStamp(ClaimsPrincipal? principal)
    {
        // (T10) phiên cũ thiếu mã bị từ chối ngay trong lượt kiểm, không cần truy vấn user.
        if (!GuestAccess.IsGuest(principal) && DeviceSession.Id(principal) == null) return null;
        var user = await base.VerifySecurityStamp(principal);
        // (T10) khách có cơ chế phiên riêng ở GuestSessionMiddleware, không có đăng nhập T2.
        return user != null && (user.IsGuest || await sessions.IsValidAsync(principal)) ? user : null;
    }
}

// (T7) tracker hiện không giữ hàm Abort; từ chối + đóng ở lần gọi/kết nối lại kế tiếp.
public sealed class DeviceSessionHubFilter(DeviceSessionService sessions) : IHubFilter
{
    private async Task CheckAsync(HubCallerContext context)
    {
        if (GuestAccess.IsGuest(context.User)) return; // phiên khách được kiểm riêng, giữ luồng làm bài khách.
        if (await sessions.IsValidAsync(context.User)) return;
        context.Abort();
        throw new HubException("Bạn đã bị đăng xuất khỏi thiết bị này. Hãy đăng nhập lại.");
    }
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        await CheckAsync(context.Context);
        return await next(context);
    }
    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        await CheckAsync(context.Context);
        await next(context);
    }
}
