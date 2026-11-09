using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.Identity.Domain.Repositories;

/// <summary>
/// Write-side repository for IpFilterRule (T1 entity).
/// Read-side list queries use EF directly in query handlers (no Dapper needed — small dataset).
/// Middleware DB fallback also uses this repo for cache repopulation.
/// </summary>
public interface IIpFilterRuleRepository
{
    Task<IpFilterRule?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<IpFilterRule>> ListAllEnabledAsync(CancellationToken ct = default);
    Task<IReadOnlyList<IpFilterRule>> ListAllAsync(CancellationToken ct = default);
    Task AddAsync(IpFilterRule entity, CancellationToken ct = default);
    Task UpdateAsync(IpFilterRule entity, CancellationToken ct = default);
    Task DeleteAsync(IpFilterRule entity, CancellationToken ct = default);

    /// <summary>
    /// Returns true if an active (non-deleted) rule with the same TenantId + Cidr already exists.
    /// Used for application-level pre-validation before the DB unique index fires.
    /// Pass excludeId to allow updating an existing rule to the same CIDR (self-update check).
    /// </summary>
    Task<bool> ExistsByCidrAsync(Guid? tenantId, string cidr, long? excludeId = null, CancellationToken ct = default);
}
