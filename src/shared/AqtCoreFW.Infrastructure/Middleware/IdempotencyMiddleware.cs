using System.Text;
using AqtCoreFW.SharedKernel.Application.Caching;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Infrastructure.Middleware;

/// <summary>
/// Idempotency middleware — replays cached response for duplicate requests.
/// Only applies to POST/PUT/PATCH that include X-Idempotency-Key header.
/// Stores response in ICacheService (Redis required) for 24h.
/// Key scoped per-tenant to prevent cross-tenant replay attacks.
/// </summary>
public sealed class IdempotencyMiddleware(
    RequestDelegate next,
    ICacheService cache,
    ILogger<IdempotencyMiddleware> logger)
{
    private const string HeaderName = "X-Idempotency-Key";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    private static readonly HashSet<string> ApplicableMethods = ["POST", "PUT", "PATCH"];

    public async Task InvokeAsync(HttpContext context)
    {
        if (!ApplicableMethods.Contains(context.Request.Method))
        {
            await next(context);
            return;
        }

        var idempotencyKey = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrEmpty(idempotencyKey))
        {
            await next(context);
            return;
        }

        // Scope by tenant to prevent cross-tenant replay. Canonical claim is "tenant_id" (issued by
        // AuthServer); "tenantId" kept as legacy fallback. Reading only "tenantId" collapsed every
        // tenant into the "anon" namespace, which would return a cached 2xx BODY across tenants.
        var tenantId = context.User?.FindFirst("tenant_id")?.Value
            ?? context.User?.FindFirst("tenantId")?.Value
            ?? "anon";
        var cacheKey = $"idempotency:{tenantId}:{ComputeHash(idempotencyKey)}";

        var cached = await cache.GetAsync<CachedIdempotencyResponse>(cacheKey);
        if (cached is not null)
        {
            logger.LogDebug("Idempotency hit for key {Key}", idempotencyKey);
            context.Response.StatusCode = cached.StatusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(cached.Body);
            return;
        }

        // Capture response body
        var originalBody = context.Response.Body;
        using var memStream = new MemoryStream();
        context.Response.Body = memStream;

        await next(context);

        memStream.Seek(0, SeekOrigin.Begin);
        var responseBody = await new StreamReader(memStream).ReadToEndAsync();
        memStream.Seek(0, SeekOrigin.Begin);
        await memStream.CopyToAsync(originalBody);
        context.Response.Body = originalBody;

        // Cache 2xx responses only
        if (context.Response.StatusCode is >= 200 and < 300)
        {
            await cache.SetAsync(cacheKey,
                new CachedIdempotencyResponse(context.Response.StatusCode, responseBody),
                CacheTtl);
        }
    }

    private static string ComputeHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..16];
    }
}

public sealed record CachedIdempotencyResponse(int StatusCode, string Body);

public static class IdempotencyExtensions
{
    public static IApplicationBuilder UseIdempotency(this IApplicationBuilder app) =>
        app.UseMiddleware<IdempotencyMiddleware>();
}
