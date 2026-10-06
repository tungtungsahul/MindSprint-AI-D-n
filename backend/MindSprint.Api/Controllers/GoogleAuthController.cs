using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MindSprint.Api.Data;
using MindSprint.Api.Services;

namespace MindSprint.Api.Controllers;

public record GoogleLoginDto(
    [Required, StringLength(10000)] string Credential,
    [Required, StringLength(64, MinimumLength = 64)] string Nonce,
    [StringLength(1024)] string? ExistingPassword);

[Route("api/auth/google")]
public class GoogleAuthController(IConfiguration configuration, AppDbContext db, TokenService tokens,
    IGoogleTokenValidator validator, GoogleLoginChallenges challenges, GoogleAccountService accounts) : ApiBase, IActionFilter
{
    [NonAction]
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ActionDescriptor.RouteValues["action"] == nameof(Config)) return;
        // Reject cross-site browser requests for the Google flow, even though the legacy
        // API has permissive CORS. Configure exact frontend origins for deployment.
        var configured = configuration.GetSection("Google:AllowedOrigins").Get<string[]>();
        var origins = configured ?? ["http://localhost:5500", "http://127.0.0.1:5500"];
        var origin = Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin) || !origins.Contains(origin, StringComparer.OrdinalIgnoreCase))
            context.Result = StatusCode(403, new { message = "Địa chỉ trang web chưa được cho phép đăng nhập Google.", code = "google_origin_not_allowed" });
    }

    [NonAction]
    public void OnActionExecuted(ActionExecutedContext context) { }

    [HttpGet("config")]
    public IActionResult Config()
    {
        Response.Headers.CacheControl = "no-store";
        var clientId = configuration["Google:ClientId"]?.Trim();
        return Ok(new { enabled = !string.IsNullOrEmpty(clientId), clientId = clientId ?? "" });
    }

    [HttpGet("challenge"), EnableRateLimiting("google-auth")]
    public IActionResult CreateChallenge()
    {
        Response.Headers.CacheControl = "no-store";
        if (string.IsNullOrWhiteSpace(configuration["Google:ClientId"]))
            return StatusCode(503, new { message = "Đăng nhập Google chưa được bật.", code = "google_not_configured" });
        var nonce = challenges.Create();
        return nonce is null
            ? StatusCode(429, new { message = "Có quá nhiều yêu cầu đăng nhập. Hãy thử lại sau một phút.", code = "google_rate_limited" })
            : Ok(new { nonce, expiresInSeconds = 300 });
    }

    [HttpPost, EnableRateLimiting("google-auth")]
    public async Task<IActionResult> Login(GoogleLoginDto dto, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (!challenges.IsValid(dto.Nonce)) return ExpiredChallenge();
        try
        {
            var identity = await validator.ValidateAsync(dto.Credential, dto.Nonce, ct);
            var user = await accounts.ResolveAsync(identity, dto.ExistingPassword, ct);
            if (!challenges.Consume(dto.Nonce)) return ExpiredChallenge();
            await db.SaveChangesAsync(ct);
            var rt = await tokens.CreateRefreshTokenAsync(user, null);
            return Ok(new { token = tokens.CreateAccessToken(user), refreshToken = rt.Token,
                user = new { user.Id, user.Email, user.DisplayName } });
        }
        catch (GoogleSignInException ex)
        {
            return StatusCode(ex.Status, new { message = ex.Message, code = ex.Code });
        }
        catch (DbUpdateException)
        {
            // Unique indexes prevent duplicate users/identities during concurrent sign-ins.
            return Conflict(new { message = "Tài khoản vừa được cập nhật. Hãy chọn lại tài khoản Google.", code = "google_account_conflict" });
        }
    }

    private IActionResult ExpiredChallenge() => Unauthorized(new {
        message = "Lượt đăng nhập Google đã hết hạn hoặc đã được sử dụng. Hãy chọn lại tài khoản.",
        code = "google_challenge_expired"
    });
}
