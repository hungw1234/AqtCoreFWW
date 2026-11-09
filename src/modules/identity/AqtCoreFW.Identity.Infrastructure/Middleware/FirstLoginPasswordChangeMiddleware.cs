using AqtCoreFW.Identity.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.Middleware;

/// <summary>
/// QĐ 742 1.3a: Enforces password change on first login (MustChangePassword flag).
/// Returns 403 with X-Must-Change-Password header so FE can redirect to change-password page.
/// Skips check for: unauthenticated, change-password endpoint, auth/connect endpoints, health.
/// </summary>
public sealed class FirstLoginPasswordChangeMiddleware(
    RequestDelegate next,
    ILogger<FirstLoginPasswordChangeMiddleware> logger)
{
    // Paths that bypass the first-login password-change gate.
    // Users must be able to reach change-password even when MustChangePassword == true.
    private static readonly string[] SkipPaths =
    [
        "/api/v1/account/change-password",
        "/api/v1/account/logout",
        "/health",
        "/openapi",
        "/connect/token",
        "/connect/logout",
    ];

    public async Task InvokeAsync(HttpContext context, UserManager<ApplicationUser> userManager)
    {
        // Skip for unauthenticated requests — no user to check.
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        // Skip allowlisted paths so the user can actually change the password.
        var path = context.Request.Path.Value ?? "";
        if (SkipPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        // Resolve user ID from standard sub/NameIdentifier claim.
        var sub = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? context.User.FindFirst("sub")?.Value;

        if (sub is not null && Guid.TryParse(sub, out _))
        {
            var user = await userManager.FindByIdAsync(sub);
            if (user?.MustChangePassword == true)
            {
                logger.LogWarning(
                    "User {UserId} must change password before proceeding. Blocking request to {Path}.",
                    sub, path);

                context.Response.StatusCode = 403;
                context.Response.Headers["X-Must-Change-Password"] = "true";
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "must_change_password",
                    message = "Password change required before continuing.",
                    message_vi = "Bạn cần đổi mật khẩu trước khi tiếp tục.",
                });
                return;
            }
        }

        await next(context);
    }
}
