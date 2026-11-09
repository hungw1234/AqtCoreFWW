using System.Net;
using AqtCoreFW.SharedKernel.Application.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Infrastructure.Middleware;

/// <summary>
/// IP Allowlist/Blocklist middleware (QĐ 742 §2.2.a).
///
/// Evaluation order (deny-first):
///   1. Loopback IPs always pass (self-lockout prevention).
///   2. Cache lookup for current IpRuleSet (key "system:ip-rules", TTL 5 min).
///   3. Cache miss → DB fallback via IIpRuleSetLoader; repopulates cache with 5-min TTL.
///   4. If any Block rule matches the remote IP → 403.
///   5. If any Allow rule matches → bypass remaining Block rules.
///
/// Cache invalidation: CreateIpRuleCommandHandler / UpdateIpRuleCommandHandler /
/// DeleteIpRuleCommandHandler each call cache.RemoveAsync("system:ip-rules") after mutation.
/// </summary>
public sealed class IpFilterMiddleware(
    RequestDelegate next,
    ICacheService cache,
    IServiceScopeFactory scopeFactory,
    ILogger<IpFilterMiddleware> logger)
{
    private const string RulesCacheKey = "system:ip-rules";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public async Task InvokeAsync(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress;

        // Loopback always allowed — prevents admin self-lockout (memory: dev-ports-and-restart-policy).
        if (remoteIp is null || IPAddress.IsLoopback(remoteIp))
        {
            await next(context);
            return;
        }

        var rules = await GetOrLoadRulesAsync(context.RequestAborted);

        if (rules.IsBlocked(remoteIp))
        {
            logger.LogWarning("IP blocked by filter rule: {IpAddress}", remoteIp);
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "Access denied.",
                errorCode = "GOV_SEC_003"
            });
            return;
        }

        await next(context);
    }

    private async Task<IpRuleSet> GetOrLoadRulesAsync(CancellationToken ct)
    {
        var cached = await cache.GetAsync<IpRuleSet>(RulesCacheKey, ct);
        if (cached is not null) return cached;

        // Cache miss: load from DB via scoped IIpRuleSetLoader, repopulate cache.
        await using var scope = scopeFactory.CreateAsyncScope();
        var loader = scope.ServiceProvider.GetService<IIpRuleSetLoader>();

        if (loader is null)
        {
            // IIpRuleSetLoader not registered (e.g. tests without Identity module) — deny nothing.
            logger.LogDebug("IIpRuleSetLoader not registered; IP filter running in pass-through mode.");
            return IpRuleSet.Empty;
        }

        var ruleSet = await loader.LoadAsync(ct);
        await cache.SetAsync(RulesCacheKey, ruleSet, CacheTtl, ct);
        return ruleSet;
    }
}

/// <summary>
/// Compiled IP rule set for efficient per-request evaluation.
///
/// Stores CIDR strings (JSON-serializable for Redis/distributed cache round-trip).
/// IPNetwork is parsed on-demand at evaluation time — no pre-compiled state stored.
///
/// BlockCidrs: CIDR ranges/IPs that should return 403.
/// AllowCidrs: CIDR ranges/IPs that bypass block rules (whitelist override).
///
/// Uses System.Net.IPNetwork (.NET 8+) for subnet matching — no external NuGet required.
/// Evaluation: Allow wins over Block when both match (deny-first, allow-override pattern).
/// </summary>
public sealed record IpRuleSet(string[] BlockCidrs, string[] AllowCidrs)
{
    public static readonly IpRuleSet Empty = new([], []);

    /// <summary>
    /// Returns true if the IP should be blocked.
    /// An explicit Allow rule for the IP overrides any matching Block rule.
    /// Malformed CIDRs are silently skipped (already validated on save).
    /// </summary>
    public bool IsBlocked(IPAddress ip)
    {
        // Explicit allow overrides all block rules.
        foreach (var cidr in AllowCidrs)
            if (IPNetwork.TryParse(cidr, out var n) && n.Contains(ip)) return false;

        // Check block rules.
        foreach (var cidr in BlockCidrs)
            if (IPNetwork.TryParse(cidr, out var n) && n.Contains(ip)) return true;

        return false;
    }
}

public static class IpFilterExtensions
{
    public static IApplicationBuilder UseIpFilter(this IApplicationBuilder app) =>
        app.UseMiddleware<IpFilterMiddleware>();
}
