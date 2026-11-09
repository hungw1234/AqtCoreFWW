using System.Text;
using AqtCoreFW.SharedKernel.Application.Caching;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Infrastructure.Middleware;

/// <summary>
/// Prevents double-submit on mutating requests within a 5-second window.
/// Complements IdempotencyMiddleware (which uses X-Idempotency-Key for replay/24h TTL).
/// This middleware targets short-lived UI race conditions using X-Request-Token.
///
/// Flow:
///   1. Client sends X-Request-Token header on POST/PUT/PATCH/DELETE.
///   2. If token already in Redis cache → 409 Conflict immediately.
///   3. If new token → store in Redis with 5s TTL, proceed to next middleware.
///
/// Key scoped per-tenant to prevent cross-tenant token collisions.
/// </summary>
public sealed class AntiDoubleSubmitMiddleware(
    RequestDelegate next,
    ICacheService cache,
    ILogger<AntiDoubleSubmitMiddleware> logger)
{
    private const string HeaderName = "X-Request-Token";
    private static readonly TimeSpan TokenTtl = TimeSpan.FromSeconds(5);
    private static readonly HashSet<string> MutatingMethods = ["POST", "PUT", "PATCH", "DELETE"];

    public async Task InvokeAsync(HttpContext context)
    {
        if (!MutatingMethods.Contains(context.Request.Method))
        {
            await next(context);
            return;
        }

        var token = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrEmpty(token))
        {
            // No token — not enforced; let request through
            await next(context);
            return;
        }

        // Canonical tenant claim is "tenant_id" (issued by AuthServer); accept legacy "tenantId"
        // as a fallback. Reading only "tenantId" silently collapsed every tenant into the "anon"
        // namespace, defeating the per-tenant scoping this key promises.
        var tenantId = context.User?.FindFirst("tenant_id")?.Value
            ?? context.User?.FindFirst("tenantId")?.Value
            ?? "anon";
        var cacheKey = $"double-submit:{tenantId}:{ComputeHash(token)}";

        var existing = await cache.GetAsync<bool?>(cacheKey);
        if (existing is not null)
        {
            logger.LogWarning(
                "Double-submit detected for token {TokenPrefix} tenant {TenantId}",
                token[..Math.Min(8, token.Length)], tenantId);

            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                """{"success":false,"errors":[{"message":"Duplicate request detected. Please wait before retrying."}]}""");
            return;
        }

        // Mark token as seen for 5 seconds
        await cache.SetAsync(cacheKey, true, TokenTtl);

        await next(context);
    }

    private static string ComputeHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..16];
    }
}

public static class AntiDoubleSubmitExtensions
{
    /// <summary>Register AntiDoubleSubmitMiddleware after auth so tenantId claim is available.</summary>
    public static IApplicationBuilder UseAntiDoubleSubmit(this IApplicationBuilder app) =>
        app.UseMiddleware<AntiDoubleSubmitMiddleware>();
}
