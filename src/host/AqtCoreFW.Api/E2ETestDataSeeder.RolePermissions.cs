using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Api;

/// <summary>
/// Partial: seeds default RolePermission mappings (role → permission).
/// Creates baseline assignments — admins can modify via Permission Matrix UI.
/// Idempotent: skips if RolePermissions already exist.
/// </summary>
public sealed partial class E2ETestDataSeeder
{
    private static async Task SeedRolePermissionsAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<IdentityDbContext>();
        var roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();

        var allPerms = await db.Permissions
            .Select(p => new { p.Id, p.Code, p.IsSensitive, p.ModuleCode })
            .ToListAsync(ct);
        if (allPerms.Count == 0) return; // Permissions not seeded yet

        var systemAdmin = await roleManager.FindByNameAsync("SystemAdmin");
        var admin       = await roleManager.FindByNameAsync("Admin");
        var govOfficer  = await roleManager.FindByNameAsync("GovOfficer");

        // Per-row idempotent (delta-merge): only INSERT mappings missing for each role.
        // Replaces sentinel-skip which prevented new perms from being mapped on existing dev DBs.
        // INSERT-only — admin custom revokes via UI are preserved (we never DELETE).
        var rolePerms = new List<RolePermission>();

        if (systemAdmin is not null)
        {
            // SystemAdmin → ALL permissions
            var existing = await ExistingPermIdsForRoleAsync(db, systemAdmin.Id, ct);
            foreach (var p in allPerms)
                if (existing.Add(p.Id))
                    rolePerms.Add(new RolePermission { RoleId = systemAdmin.Id, PermissionId = p.Id });
        }

        if (admin is not null)
        {
            // Admin → all except critical system-only perms
            var excludedCodes = new HashSet<string>
            {
                "SYSTEM.CONFIG.MANAGE", "AUDIT.RTBF.EXECUTE", "AI.MODEL.MANAGE",
                // Phase-00 D6: PLATFORM.* tier is SystemAdmin-only, tenant-Admin excluded
                "PLATFORM.WORKFLOW_TAXONOMY.MANAGE",
                // v2.133p6 P0-2: cross-tenant CRUD is SystemAdmin-only — tenant-Admin
                // must NOT be able to list/create/update/archive other tenants. Multi-tenant
                // isolation invariant (memory: multi-tenant-deployment.md).
                "IDENTITY.TENANT.VIEW", "IDENTITY.TENANT.MANAGE",
                // Phase-04 §256: SoD — only SystemAdmin may mutate ClassificationRule rows
                // (flip MustEncryptAtRest). PermCodes.Compliance xml-doc states "(SystemAdmin only)".
                "COMPLIANCE.CLASSIFICATION.MANAGE",
                // Phase-05 QĐ742 §2.2.a: IP filter rule CRUD is SystemAdmin-only.
                // LIST has no tenant filter — Admin seeing all tenants' rules is an info-leak.
                // Both VIEW and MANAGE excluded from tenant-Admin role.
                "SECURITY.IP_RULE.VIEW", "SECURITY.IP_RULE.MANAGE",
                // Phase-05 P0-1: SECURITY.HEALTH.VIEW is SystemAdmin-only.
                // 3 of 6 health query handlers (BackupHistory, AuditIntegrity, KeyRotation)
                // have no tenant filter — granting to tenant-Admin = cross-tenant data leak (OWASP A01).
                "SECURITY.HEALTH.VIEW",
                // Phase-05b W6 QĐ742 §5.1: signature config is SystemAdmin-only.
                // Config stores Vault credential refs + CA thumbprints — tenant-Admin access is privilege escalation.
                "SECURITY.SIGNATURE_CONFIG.VIEW", "SECURITY.SIGNATURE_CONFIG.MANAGE",
            };
            var existing = await ExistingPermIdsForRoleAsync(db, admin.Id, ct);
            foreach (var p in allPerms.Where(p => !excludedCodes.Contains(p.Code)))
                if (existing.Add(p.Id))
                    rolePerms.Add(new RolePermission { RoleId = admin.Id, PermissionId = p.Id });
        }

        if (govOfficer is not null)
        {
            // GovOfficer → officer-tier modules only
            var officerModules = new HashSet<string> { "FORMS", "NOTIF", "SEARCH", "COLLAB" };
            var officerPerms = allPerms.Where(p =>
                officerModules.Contains(p.ModuleCode) ||
                (p.ModuleCode == "FILES" && p.Code.StartsWith("FILES.FILE.")) ||
                (p.ModuleCode == "WORKFLOW" && p.Code.StartsWith("WORKFLOW.TASK.")) ||
                p.Code == "AUDIT.LOG.VIEW" ||
                p.Code == "AUDIT.LOGIN.VIEW" ||
                p.Code == "AUDIT.INCIDENT.VIEW" ||
                p.Code == "REPORT.DASH.VIEW" ||
                p.Code == "REPORT.REPORT.VIEW");

            var existing = await ExistingPermIdsForRoleAsync(db, govOfficer.Id, ct);
            foreach (var p in officerPerms)
                if (existing.Add(p.Id))
                    rolePerms.Add(new RolePermission { RoleId = govOfficer.Id, PermissionId = p.Id });
        }

        if (rolePerms.Count > 0)
        {
            await db.RolePermissions.AddRangeAsync(rolePerms, ct);
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task<HashSet<Guid>> ExistingPermIdsForRoleAsync(
        IdentityDbContext db, Guid roleId, CancellationToken ct)
    {
        var ids = await db.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.PermissionId)
            .ToListAsync(ct);
        return ids.ToHashSet();
    }
}
