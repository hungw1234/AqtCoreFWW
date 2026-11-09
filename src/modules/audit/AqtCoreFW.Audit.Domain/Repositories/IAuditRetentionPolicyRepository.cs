using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Audit.Domain.Repositories;

/// <summary>
/// CRUD repository for <see cref="AuditRetentionPolicy"/> — QĐ 742 §3.2.b configurable
/// retention windows per (tenant, category). Mutable: <c>UpdateAsync</c>/<c>DeleteAsync</c>
/// allowed (unlike <see cref="IAuditLogRepository"/>).
/// </summary>
public interface IAuditRetentionPolicyRepository
{
    Task<AuditRetentionPolicy?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>List policies; optionally filter by tenant. Pass null to list global defaults too.</summary>
    Task<IReadOnlyList<AuditRetentionPolicy>> ListAsync(
        Guid? tenantIdFilter,
        CancellationToken cancellationToken = default);

    Task AddAsync(AuditRetentionPolicy policy, CancellationToken cancellationToken = default);

    Task UpdateAsync(AuditRetentionPolicy policy, CancellationToken cancellationToken = default);

    Task DeleteAsync(AuditRetentionPolicy policy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolution precedence at purge time per Phase-01 plan §"AuditRetentionPolicy":
    /// (tenant + category) → (tenant + null category) → (null tenant + null category).
    /// Returns the most-specific match or null if no fallback exists.
    /// </summary>
    Task<AuditRetentionPolicy?> ResolveAsync(
        Guid tenantId,
        AuditCategory category,
        CancellationToken cancellationToken = default);
}
