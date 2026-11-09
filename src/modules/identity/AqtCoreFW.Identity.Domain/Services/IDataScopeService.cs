using AqtCoreFW.Identity.Domain.Models;

namespace AqtCoreFW.Identity.Domain.Services;

/// <summary>
/// Resolves the effective data scope for a user by merging role-based scopes and user overrides.
/// Results are cached in Redis (key: scope:{userId}, TTL 5 min).
/// Call InvalidateAsync after any role or override change.
/// </summary>
public interface IDataScopeService
{
    /// <summary>Returns the merged, cached ResolvedDataScope for the given user.</summary>
    Task<ResolvedDataScope> ResolveAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Evicts the Redis cache entry so the next ResolveAsync re-reads from DB.</summary>
    Task InvalidateAsync(Guid userId);
}
