using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Persistence;

/// <summary>Write-side repository wrapping UserManager — keeps EF tracking semantics.</summary>
public sealed class UserRepository : IUserRepository
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IdentityDbContext _db;

    public UserRepository(UserManager<ApplicationUser> userManager, IdentityDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    public async Task<ApplicationUser?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _userManager.FindByIdAsync(id.ToString());

    public async Task<ApplicationUser?> GetByEmailAsync(string email, CancellationToken ct = default)
        => await _userManager.FindByEmailAsync(email);

    public async Task AddAsync(ApplicationUser user, CancellationToken ct = default)
        => await _userManager.CreateAsync(user);

    public async Task UpdateAsync(ApplicationUser user, CancellationToken ct = default)
        => await _userManager.UpdateAsync(user);

    public async Task<IReadOnlyList<Guid>> GetRoleIdsAsync(Guid userId, CancellationToken ct = default)
        => await _db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

    public async Task AddToRoleAsync(Guid userId, Guid roleId, CancellationToken ct = default)
    {
        // Idempotent: skip if the link already exists (mirrors AddToRolesAsync semantics).
        var exists = await _db.UserRoles.AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId, ct);
        if (exists)
            return;

        _db.UserRoles.Add(new IdentityUserRole<Guid> { UserId = userId, RoleId = roleId });
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveFromRolesAsync(
        Guid userId, IReadOnlyCollection<Guid> roleIds, CancellationToken ct = default)
    {
        if (roleIds.Count == 0)
            return;

        var links = await _db.UserRoles
            .Where(ur => ur.UserId == userId && roleIds.Contains(ur.RoleId))
            .ToListAsync(ct);

        if (links.Count == 0)
            return;

        _db.UserRoles.RemoveRange(links);
        await _db.SaveChangesAsync(ct);
    }
}
