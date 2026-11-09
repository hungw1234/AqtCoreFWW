using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Persistence;

/// <summary>EF Core + RoleManager repository for ApplicationRole write operations.</summary>
public sealed class RoleRepository(
    IdentityDbContext db,
    RoleManager<ApplicationRole> roleManager) : IRoleRepository
{
    public async Task<ApplicationRole?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await db.Roles.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<ApplicationRole?> GetByNameAsync(string name, CancellationToken ct = default)
        => await roleManager.FindByNameAsync(name);

    public async Task<ApplicationRole?> GetByNameInTenantScopeAsync(
        string name, Guid? tenantId, CancellationToken ct = default)
    {
        // Normalize the same way RoleManager/the store would, then match within the tenant's scope.
        // A tenant can hold at most one role per NormalizedName (unique index), plus an optional
        // same-named system-wide (null-tenant) role; prefer the tenant-owned one deterministically.
        var normalized = roleManager.NormalizeKey(name);
        var matches = await db.Roles
            .Where(r => r.NormalizedName == normalized && (r.TenantId == tenantId || r.TenantId == null))
            .ToListAsync(ct);

        return matches.FirstOrDefault(r => r.TenantId == tenantId)
            ?? matches.FirstOrDefault(r => r.TenantId == null);
    }

    public async Task<IReadOnlyList<ApplicationRole>> ListAllAsync(CancellationToken ct = default)
        => await db.Roles.AsNoTracking().ToListAsync(ct);

    public async Task<bool> ExistsByCodeAndTenantAsync(string code, Guid tenantId, CancellationToken ct = default)
        => await db.Roles.AnyAsync(r => r.Code == code && r.TenantId == tenantId, ct);

    public async Task AddAsync(ApplicationRole role, CancellationToken ct = default)
    {
        // RoleManager handles NormalizedName generation
        var result = await roleManager.CreateAsync(role);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"RoleManager.CreateAsync failed: {string.Join(", ", result.Errors.Select(e => e.Description))}");
    }

    public async Task UpdateAsync(ApplicationRole role, CancellationToken ct = default)
    {
        var result = await roleManager.UpdateAsync(role);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"RoleManager.UpdateAsync failed: {string.Join(", ", result.Errors.Select(e => e.Description))}");
    }

    public async Task SoftDeleteAsync(ApplicationRole role, CancellationToken ct = default)
    {
        role.IsActive = false;
        await db.SaveChangesAsync(ct);
    }
}
