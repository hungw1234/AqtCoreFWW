using AqtCoreFW.Identity.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.Middleware;

/// <summary>
/// QĐ742 1.3c/d/đ: Checks if authenticated user's password has expired.
/// Returns 403 with X-Password-Expired header so FE can redirect to change password.
/// Skips check for: unauthenticated, change-password endpoint, health checks.
/// </summary>
public sealed class PasswordExpiryMiddleware(RequestDelegate next, ILogger<PasswordExpiryMiddleware> logger)
{
    // Paths that skip password expiry check (user must be able to change password even when expired)
    private static readonly string[] SkipPaths =
    [
        "/api/v1/account/change-password",
        "/health",
        "/openapi",
        "/connect/token",
    ];

    public async Task InvokeAsync(HttpContext context, UserManager<ApplicationUser> userManager)
    {
        // Skip for unauthenticated requests
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context);
            return;
        }

        // Skip for allowlisted paths
        var path = context.Request.Path.Value ?? "";
        if (SkipPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        // Get user ID from claims
        var sub = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? context.User.FindFirst("sub")?.Value;

        if (sub is not null && Guid.TryParse(sub, out _))
        {
            var user = await userManager.FindByIdAsync(sub);
            if (user?.PasswordExpiresAt is not null && DateTime.UtcNow > user.PasswordExpiresAt.Value)
            {
                logger.LogWarning("Password expired for user {UserId}. Forcing password change.", sub);
                context.Response.StatusCode = 403;
                context.Response.Headers["X-Password-Expired"] = "true";
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "password_expired",
                    message = "Your password has expired. Please change your password to continue.",
                    message_vi = "Mật khẩu đã hết hạn. Vui lòng đổi mật khẩu để tiếp tục.",
                });
                return;
            }
        }

        await next(context);
    }
}
