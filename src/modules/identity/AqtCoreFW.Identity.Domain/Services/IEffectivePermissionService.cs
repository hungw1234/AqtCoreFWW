using AqtCoreFW.Identity.Domain.Models;

namespace AqtCoreFW.Identity.Domain.Services;

/// <summary>
/// Resolves the full effective permission summary for a user by merging
/// direct roles, group roles, and active delegations.
/// Results are cached in Redis (key: perm:{userId}, TTL 10 min).
/// Call InvalidateAsync after any role/group/delegation change.
/// </summary>
public interface IEffectivePermissionService
{
    /// <summary>Returns the cached or freshly-computed permission summary for the user.</summary>
    Task<EffectivePermissionSummary> GetSummaryAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Evicts the Redis cache entry so the next call re-reads from DB.</summary>
    Task InvalidateAsync(Guid userId);
}
