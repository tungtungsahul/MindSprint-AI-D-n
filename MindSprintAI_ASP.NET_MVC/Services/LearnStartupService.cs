using MindSprintAI_ASP.NET_MVC.Data;

namespace MindSprintAI_ASP.NET_MVC.Services;

public sealed class LearnStartupService(IServiceScopeFactory scopes, TimeProvider clock, IConfiguration config) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<AuditLog>().Attach(); // (NK3) Vai trò khởi tạo có người làm là Hệ thống.
        await AdminService.BootstrapAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            config.GetSection("Admin:Emails").Get<string[]>() ?? []);
        await LearnConnectionService.ResetDisconnectedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            clock.GetUtcNow().UtcDateTime);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
