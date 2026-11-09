using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Application.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.Seeding;

/// <summary>
/// Partial: bind a tenant's seeded users to that tenant's business roles, derived from each user's
/// PositionName. Role codes are tenant-scoped (profile.RolePrefix + position code), matching the roles
/// TenantRoleSeeder created. Idempotent — skips users already bound (the IdentityUserRole composite PK
/// would otherwise duplicate). Returns the number of bindings inserted.
/// </summary>
public sealed partial class TenantUserSeeder
{
    public static async Task<int> SeedUserRolesForProfileAsync(
        IdentityDbContext db,
        TenantSeedProfile profile,
        int batchSize,
        ILogger logger,
        CancellationToken ct)
    {
        var tenantId = profile.TenantId;

        // Load this tenant's roles: Code → RoleId.
        var roleMap = await db.Roles
            .Where(r => r.TenantId == tenantId && r.Code.StartsWith(profile.RolePrefix))
            .Select(r => new { r.Id, r.Code })
            .ToDictionaryAsync(r => r.Code, r => r.Id, ct);

        if (roleMap.Count == 0)
        {
            logger.LogWarning(
                "[TenantUserSeeder] {Code}: no {Prefix}* roles found — run the role seeder first",
                profile.Code, profile.RolePrefix);
            return 0;
        }

        var users = await db.Users
            .Where(u => u.TenantId == tenantId && u.EmployeeCode!.StartsWith(profile.UserCodePrefix))
            .Select(u => new { u.Id, u.PositionName })
            .ToListAsync(ct);

        if (users.Count == 0)
        {
            logger.LogWarning("[TenantUserSeeder] {Code}: no seeded users found — run the user seed first", profile.Code);
            return 0;
        }

        // Pre-load already-bound user ids → resume-safe.
        var userIds = users.Select(u => u.Id).ToList();
        var alreadyBoundUserIds = await db.UserRoles
            .Where(ur => userIds.Contains(ur.UserId))
            .Select(ur => ur.UserId)
            .ToHashSetAsync(ct);

        // PositionTitle → PositionCode reverse map (shared Positions list).
        var positionByTitle = Positions.ToDictionary(p => p.Title, p => p.Code);

        var toInsert = new List<IdentityUserRole<Guid>>();
        int unmapped = 0;

        foreach (var u in users)
        {
            if (alreadyBoundUserIds.Contains(u.Id)) continue;
            if (string.IsNullOrEmpty(u.PositionName)) { unmapped++; continue; }

            if (!positionByTitle.TryGetValue(u.PositionName, out var posCode))
                posCode = "STAFF";

            // Tenant-scoped role code = prefix + position code (e.g. "BNV_" + "DEPT_DIRECTOR").
            var roleCode = profile.RolePrefix + posCode;
            if (!roleMap.TryGetValue(roleCode, out var roleId)) { unmapped++; continue; }

            toInsert.Add(new IdentityUserRole<Guid> { UserId = u.Id, RoleId = roleId });
        }

        int inserted = 0;
        foreach (var chunk in toInsert.Chunk(batchSize))
        {
            db.UserRoles.AddRange(chunk);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            inserted += chunk.Length;
        }

        logger.LogInformation(
            "[TenantUserSeeder] {Code}: user-roles: inserted={Inserted}, already-bound={Skipped}, unmapped={Unmapped} (of {Users})",
            profile.Code, inserted, alreadyBoundUserIds.Count, unmapped, users.Count);
        return inserted;
    }
}
