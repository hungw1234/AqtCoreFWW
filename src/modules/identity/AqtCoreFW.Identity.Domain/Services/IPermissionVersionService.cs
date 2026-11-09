namespace AqtCoreFW.Identity.Domain.Services;

/// <summary>
/// Tracks a monotonically-increasing version counter per user in Redis.
/// Key: perm-version:{userId}
///
/// Consumers (e.g. ClaimsEnrichmentTransformer, API gateways) compare their
/// cached version against the current value to detect stale permission sets
/// without a full DB round-trip.
/// </summary>
public interface IPermissionVersionService
{
    /// <summary>Returns the current permission version for the user. Returns 0 if unavailable.</summary>
    Task<int> GetVersionAsync(Guid userId);

    /// <summary>Atomically increments the version counter, invalidating any cached permission data.</summary>
    Task IncrementAsync(Guid userId);
}
