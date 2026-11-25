using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using MindSprintAI_ASP.NET_MVC.Data.Entities;

namespace MindSprintAI_ASP.NET_MVC.Services;

public sealed class AccountLifecycleService(SignInManager<ApplicationUser> signIn, AuditLog audit)
{
    public async Task LogoutAsync(string userId)
    {
        await signIn.SignOutAsync();
        await signIn.Context.SignOutAsync(TwoFactorService.PasskeyPendingScheme);
        await audit.WriteSeparateAsync(AuditAction.LoggedOut, "User", userId, userId); // (NK3)
    }
}
