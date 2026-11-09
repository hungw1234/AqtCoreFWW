namespace AqtCoreFW.Infrastructure.Middleware;

/// <summary>
/// Abstraction for loading the current IpRuleSet from a persistent store.
/// Implemented by AqtCoreFW.Identity.Infrastructure to read from IpFilterRule table.
/// Registered by the API host at composition root — IpFilterMiddleware resolves this
/// via IServiceScopeFactory so the scoped EF DbContext lifetime is respected.
/// </summary>
public interface IIpRuleSetLoader
{
    /// <summary>Loads all enabled IP filter rules and builds an IpRuleSet for middleware evaluation.</summary>
    Task<IpRuleSet> LoadAsync(CancellationToken ct = default);
}
