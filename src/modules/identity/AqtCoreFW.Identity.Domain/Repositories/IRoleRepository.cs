using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.Identity.Domain.Repositories;

/// <summary>Write-side role repository.</summary>
public interface IRoleRepository
{
    Task<ApplicationRole?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ApplicationRole?> GetByNameAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Resolves a role by display name within a tenant's scope. Role display names are unique per
    /// tenant (not globally), so a bare name lookup is ambiguous; this scopes the match to the given
    /// tenant, preferring a tenant-owned role (TenantId == tenantId) over a same-named system-wide
    /// role (TenantId == null). Returns null if neither exists.
    /// </summary>
    Task<ApplicationRole?> GetByNameInTenantScopeAsync(string name, Guid? tenantId, CancellationToken ct = default);

    Task<IReadOnlyList<ApplicationRole>> ListAllAsync(CancellationToken ct = default);

    /// <summary>Check if a role code already exists for the given tenant.</summary>
    Task<bool> ExistsByCodeAndTenantAsync(string code, Guid tenantId, CancellationToken ct = default);

    Task AddAsync(ApplicationRole role, CancellationToken ct = default);
    Task UpdateAsync(ApplicationRole role, CancellationToken ct = default);
    Task SoftDeleteAsync(ApplicationRole role, CancellationToken ct = default);
}
