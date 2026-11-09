using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RateLimitMetadata = System.Threading.RateLimiting.MetadataName;

namespace AqtCoreFW.Infrastructure.RateLimiting;

/// <summary>
/// ASP.NET Core built-in rate limiter — app-level defense layer.
/// Phase 06 YARP adds gateway-level rate limiting (second layer).
/// Authenticated: keyed by userId JWT sub claim (unforgeable).
/// Anonymous: keyed by RemoteIpAddress.
/// Policies:
///   GlobalLimiter — 100 req/min authenticated, 20 req/min anonymous (partitioned by identity)
///   "default"     — alias used by YARP route config (100/min authenticated, 20/min anonymous)
///   "anonymous"   — 60 req/min per IP
///   "authenticated" — 600 req/min per userId
///   "read-ops"    — 60 req/min per IP (search, list endpoints)
///   "write-ops"   — 20 req/min per IP
///   "public-form-submit" — 5 req/min per IP (public form spam prevention)
///   "mfa-verify"  — 5 req/min per client (TOTP brute-force prevention)
///   "formula-evaluate" — 30 req/min per IP (public realtime formula compute, dev: 300)
///   "public-field-options" — 15 req/min per IP (public cascade option resolve, dev: 150; stricter than
///                            formula-evaluate as it can trigger an outbound external data-source fetch)
/// </summary>
public static class RateLimiterConfiguration
{
    /// <summary>
    /// Registers ASP.NET Core rate limiting middleware with 9 policies.
    /// Can be disabled via configuration: "RateLimiting:Enabled": false (for benchmarks).
    /// Default: enabled (true) — backward compatible with Dev/Prod.
    /// </summary>
    public static IServiceCollection AddGovRateLimiter(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        // Opt-out pattern: default ON (backward compat), only Benchmark env sets false
        var enabled = configuration?.GetValue<bool>("RateLimiting:Enabled", true) ?? true;
        if (!enabled)
        {
            // Benchmark / load-test mode — register no-op limiter (services still needed by UseRateLimiter middleware)
            services.AddRateLimiter(options =>
            {
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                    RateLimitPartition.GetNoLimiter<string>("unlimited"));
                foreach (var policy in new[] { "gateway", "anonymous", "authenticated", "write-ops", "read-ops", "batch-ops", "public-form-submit", "mfa-verify", "formula-evaluate", "public-field-options" })
                {
                    options.AddPolicy(policy, _ => RateLimitPartition.GetNoLimiter<string>("unlimited"));
                }
            });
            return services;
        }

        var isDev = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";

        services.AddRateLimiter(options =>
        {
            // Global partitioned limiter: authenticated/anonymous per-minute limits.
            // Development: 1000/200 to avoid throttling E2E tests.
            // Production: 100/20 for abuse prevention.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
            {
                var isAuth = ctx.User?.Identity?.IsAuthenticated == true;
                var partitionKey = isAuth
                    ? ctx.User!.FindFirst("sub")?.Value ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anon"
                    : ctx.Connection.RemoteIpAddress?.ToString() ?? "anon";
                var permitLimit = isAuth ? (isDev ? 1000 : 100) : (isDev ? 200 : 20);

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 5
                });
            });

            // "gateway" policy — referenced by YARP route config (RateLimiterPolicy: "gateway")
            // Mirrors global limiter limits; keyed by IP for YARP reverse-proxy scenario
            // NOTE: "default" is reserved by ASP.NET Core YARP integration — must use custom name
            options.AddFixedWindowLimiter("gateway", opt =>
            {
                opt.Window = TimeSpan.FromMinutes(1);
                opt.PermitLimit = 100;
                opt.QueueLimit = 5;
                opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });

            // Anonymous endpoints: 60 req/min per IP (dev: 600)
            options.AddFixedWindowLimiter("anonymous", opt =>
            {
                opt.Window = TimeSpan.FromMinutes(1);
                opt.PermitLimit = isDev ? 600 : 60;
                opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });

            // Authenticated endpoints: 600 req/min per userId
            options.AddFixedWindowLimiter("authenticated", opt =>
            {
                opt.Window = TimeSpan.FromMinutes(1);
                opt.PermitLimit = 600;
                opt.QueueLimit = 20;
                opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });

            // Read operations (search, list): 60 req/min per IP (dev: 600)
            options.AddFixedWindowLimiter("read-ops", opt =>
            {
                opt.Window = TimeSpan.FromMinutes(1);
                opt.PermitLimit = isDev ? 600 : 60;
                opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });

            // Write operations (POST/PUT/DELETE): 20 req/min per IP (dev: 200)
            options.AddFixedWindowLimiter("write-ops", opt =>
            {
                opt.Window = TimeSpan.FromMinutes(1);
                opt.PermitLimit = isDev ? 200 : 20;
                opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });

            // Batch operations (multi-item POST): 10 req/min per IP (dev: 100)
            // Stricter than write-ops because each call mutates up to 50 items.
            options.AddFixedWindowLimiter("batch-ops", opt =>
            {
                opt.Window = TimeSpan.FromMinutes(1);
                opt.PermitLimit = isDev ? 100 : 10;
                opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });

            // Public form submission: 5 submissions/min per IP (dev: 50)
            options.AddFixedWindowLimiter("public-form-submit", opt =>
            {
                opt.Window = TimeSpan.FromMinutes(1);
                opt.PermitLimit = isDev ? 50 : 5;
                opt.QueueLimit = 0;
                opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });

            // MFA verify: 5 attempts/min per client (dev: 50)
            options.AddFixedWindowLimiter("mfa-verify", opt =>
            {
                opt.Window = TimeSpan.FromMinutes(1);
                opt.PermitLimit = isDev ? 50 : 5;
                opt.QueueLimit = 0;
                opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });

            // Public formula-evaluate: realtime typing (debounced ~300ms) — 30 req/min per IP
            // (dev: 300). Higher than public-form-submit since it is read-only, no-op display
            // compute, but still capped since it is an unauthenticated surface.
            options.AddFixedWindowLimiter("formula-evaluate", opt =>
            {
                opt.Window = TimeSpan.FromMinutes(1);
                opt.PermitLimit = isDev ? 300 : 30;
                opt.QueueLimit = 0;
                opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });

            // Public cascade field-options: anonymous cascade resolve — 15 req/min per IP (dev: 150).
            // STRICTER than formula-evaluate (30/min): resolving a reference field can trigger an
            // outbound external data-source fetch, so the abuse surface (quota burn / SSRF-adjacent
            // amplification) is higher than the in-process formula compute. Unauthenticated, QueueLimit=0.
            options.AddFixedWindowLimiter("public-field-options", opt =>
            {
                opt.Window = TimeSpan.FromMinutes(1);
                opt.PermitLimit = isDev ? 150 : 15;
                opt.QueueLimit = 0;
                opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });

            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (ctx, ct) =>
            {
                ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                // Dynamic Retry-After from lease metadata (window remaining)
                var retrySeconds = 60;
                if (ctx.Lease.TryGetMetadata(RateLimitMetadata.RetryAfter, out var retryAfter))
                    retrySeconds = Math.Max(1, (int)retryAfter.TotalSeconds);

                ctx.HttpContext.Response.Headers["Retry-After"] = retrySeconds.ToString();
                ctx.HttpContext.Response.Headers["X-RateLimit-Limit"] = ResolvePolicyLimit(ctx.HttpContext).ToString();

                await ctx.HttpContext.Response.WriteAsJsonAsync(new
                {
                    type = "https://gov.vn/errors/gov_sys_005",
                    title = "Too Many Requests",
                    status = 429,
                    errorCode = "GOV_SYS_005",
                    detail = $"Rate limit exceeded. Retry after {retrySeconds} seconds."
                }, ct);
            };
        });

        return services;
    }

    /// <summary>
    /// Resolves the permit limit for the rate-limiting policy applied to the current endpoint.
    /// Falls back to global limiter limits (100 auth / 20 anon) when no named policy is present.
    /// </summary>
    private static int ResolvePolicyLimit(HttpContext httpContext)
    {
        var policyName = httpContext.GetEndpoint()
            ?.Metadata.GetMetadata<EnableRateLimitingAttribute>()
            ?.PolicyName;

        var isDev = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";

        return policyName switch
        {
            "anonymous" => isDev ? 600 : 60,
            "authenticated" => 600,
            "read-ops" => isDev ? 600 : 60,
            "write-ops" => isDev ? 200 : 20,
            "batch-ops" => isDev ? 100 : 10,
            "public-form-submit" => isDev ? 50 : 5,
            "mfa-verify" => isDev ? 50 : 5,
            "formula-evaluate" => isDev ? 300 : 30,
            "public-field-options" => isDev ? 150 : 15,
            "gateway" => 100,
            // Global limiter: auth=100(dev:1000), anon=20(dev:200)
            _ => httpContext.User?.Identity?.IsAuthenticated == true
                ? (isDev ? 1000 : 100)
                : (isDev ? 200 : 20)
        };
    }
}
