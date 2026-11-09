using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.Identity.Domain.Repositories;

/// <summary>Write-side user repository — read-side uses IReadDbConnection (Dapper).</summary>
public interface IUserRepository
{
    Task<ApplicationUser?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ApplicationUser?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task AddAsync(ApplicationUser user, CancellationToken ct = default);
    Task UpdateAsync(ApplicationUser user, CancellationToken ct = default);

    // --- User-role links by RoleId (tenant-deterministic; avoids name-based global resolution) ---

    /// <summary>Returns the RoleIds currently assigned to the user.</summary>
    Task<IReadOnlyList<Guid>> GetRoleIdsAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Adds the user to the role by RoleId. Idempotent — no-op if already assigned.</summary>
    Task AddToRoleAsync(Guid userId, Guid roleId, CancellationToken ct = default);

    /// <summary>Removes the given RoleIds from the user. No-op for ids the user does not hold.</summary>
    Task RemoveFromRolesAsync(Guid userId, IReadOnlyCollection<Guid> roleIds, CancellationToken ct = default);
}
