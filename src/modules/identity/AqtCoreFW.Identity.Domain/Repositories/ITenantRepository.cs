using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.ValueObjects;

namespace AqtCoreFW.Identity.Domain.Repositories;

/// <summary>Write-side tenant repository — read-side uses IReadDbConnection (Dapper).</summary>
public interface ITenantRepository
{
    /// <summary>Gets tenant by PublicId (Guid AK), returns null if not found.</summary>
    Task<Tenant?> GetByPublicIdAsync(Guid publicId, CancellationToken ct = default);

    /// <summary>Gets tenant by internal long PK (for FK lookups inside handlers).</summary>
    Task<Tenant?> GetByInternalIdAsync(long id, CancellationToken ct = default);

    /// <summary>Gets tenant by Code (case-insensitive), returns null if not found.</summary>
    Task<Tenant?> GetByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>Checks if a tenant with the given PublicId exists.</summary>
    Task<bool> ExistsAsync(Guid publicId, CancellationToken ct = default);

    /// <summary>Checks if a tenant with the given Code already exists.</summary>
    Task<bool> ExistsByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>Lists tenants with optional search and status filter (paginated).</summary>
    Task<(IReadOnlyList<Tenant> Items, int TotalCount)> ListAsync(
        string? search,
        TenantStatus? status,
        int page,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>Adds a new tenant to the repository.</summary>
    Task AddAsync(Tenant tenant, CancellationToken ct = default);

    /// <summary>Updates an existing tenant.</summary>
    Task UpdateAsync(Tenant tenant, CancellationToken ct = default);
}
