using Microsoft.AspNetCore.Authentication;

namespace MindSprintAI_ASP.NET_MVC.Services;

public static class ExternalProviderConfig
{
    public const string GoogleVerifiedClaim = "email_verified";
    public static bool Known(string? provider) => provider is "Google" or "Microsoft";
    public static AuthenticationBuilder AddQuizExternalProviders(this AuthenticationBuilder auth, IConfiguration config)
    {
        bool Configured(string name) => !string.IsNullOrWhiteSpace(config[$"Authentication:{name}:ClientId"])
            && !string.IsNullOrWhiteSpace(config[$"Authentication:{name}:ClientSecret"]);
        if (Configured("Google")) auth.AddGoogle(o =>
        {
            o.ClientId = config["Authentication:Google:ClientId"]!;
            o.ClientSecret = config["Authentication:Google:ClientSecret"]!;
            o.SaveTokens = false;
            o.SignInScheme = Microsoft.AspNetCore.Identity.IdentityConstants.ExternalScheme;
            o.Scope.Add("profile");
            o.ClaimActions.MapJsonKey(AvatarService.GooglePictureClaim, "picture");
            o.ClaimActions.MapJsonKey(GoogleVerifiedClaim, "email_verified", System.Security.Claims.ClaimValueTypes.Boolean);
            o.Events.OnRemoteFailure = RemoteFailure;
            o.Events.OnRedirectToAuthorizationEndpoint = SelectAccount;
        });
        if (Configured("Microsoft")) auth.AddMicrosoftAccount("Microsoft", o =>
        {
            o.ClientId = config["Authentication:Microsoft:ClientId"]!;
            o.ClientSecret = config["Authentication:Microsoft:ClientSecret"]!;
            o.SaveTokens = false; // Giữ endpoint common mặc định cho cá nhân và trường/công ty.
            o.SignInScheme = Microsoft.AspNetCore.Identity.IdentityConstants.ExternalScheme;
            o.Events.OnRemoteFailure = RemoteFailure;
            o.Events.OnRedirectToAuthorizationEndpoint = SelectAccount;
        });
        return auth;
    }
    // Luôn hiện trang chọn tài khoản: nếu không, Google/Microsoft tự lấy tài khoản đang đăng nhập trên trình duyệt
    // → không nối được tài khoản thứ hai, và máy dùng chung dễ vào nhầm tài khoản người khác.
    public static string WithSelectAccount(string uri) => Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(uri, "prompt", "select_account");
    private static Task SelectAccount(RedirectContext<Microsoft.AspNetCore.Authentication.OAuth.OAuthOptions> context)
    {
        context.Response.Redirect(WithSelectAccount(context.RedirectUri));
        return Task.CompletedTask;
    }
    private static async Task RemoteFailure(RemoteFailureContext context)
    {
        await context.HttpContext.SignOutAsync(Microsoft.AspNetCore.Identity.IdentityConstants.ExternalScheme);
        context.HandleResponse();
        context.Response.Redirect("/Account/ExternalLoginCallback?failed=true");
    }
}
