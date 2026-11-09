using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Application.Hosting;
using AqtCoreFW.SharedKernel.Application.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Identity.Infrastructure.Seeding;

/// <summary>
/// Tenant-agnostic role seeder: seeds the canonical government-hierarchy business roles for every
/// <see cref="TenantSeedProfiles"/> entry (BTC + BNV), with the per-tenant code prefix
/// (profile.RolePrefix). Registered to run before the user seeder, which binds users to these roles.
///
/// Roles are tenant-scoped: Code is unique per (Code, TenantId) and the display name is unique per
/// (TenantId, NormalizedName) (see ApplicationRoleConfiguration), so two tenants can share a role
/// name (e.g. "Bộ trưởng"). Idempotent per (Code, TenantId). Requires the tenant row to exist (the
/// proactive TenantSeeder runs first) — skips-with-warning otherwise to avoid orphaned roles.
/// Default OFF (see <see cref="TenantRoleSeederOptions"/>).
/// </summary>
public sealed class TenantRoleSeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<TenantRoleSeederOptions> options,
    ILogger<TenantRoleSeeder> logger) : IHostedService, IIntegrationTestSeeder
{
    private readonly TenantRoleSeederOptions _opts = options.Value;

    /// <summary>
    /// Canonical 17 government-hierarchy business roles (code suffix + Vietnamese name). The full
    /// role code is profile.RolePrefix + suffix (e.g. "BTC_" + "MINISTER" → "BTC_MINISTER"). The
    /// taxonomy is generic across ministries; only the prefix differs per tenant.
    /// </summary>
    private static readonly (string Suffix, string Name)[] RoleDefinitions =
    {
        ("MINISTER",             "Bộ trưởng"),
        ("DEPUTY_MINISTER",      "Thứ trưởng"),
        ("DEPT_DIRECTOR",        "Vụ trưởng/Cục trưởng"),
        ("DEPT_DEPUTY",          "Phó Vụ trưởng/Phó Cục trưởng"),
        ("REGIONAL_DIRECTOR",    "Chi cục trưởng khu vực"),
        ("REGIONAL_DEPUTY",      "Phó Chi cục trưởng khu vực"),
        ("DIVISION_HEAD",        "Trưởng phòng"),
        ("DIVISION_DEPUTY",      "Phó Trưởng phòng"),
        ("TEAM_LEADER",          "Đội trưởng"),
        ("TEAM_DEPUTY",          "Đội phó"),
        ("SPECIALIST_SENIOR",    "Chuyên viên cao cấp"),
        ("SPECIALIST_PRINCIPAL", "Chuyên viên chính"),
        ("SPECIALIST",           "Chuyên viên"),
        ("OFFICER",              "Cán sự"),
        ("STAFF",                "Nhân viên"),
        ("INSPECTOR",            "Thanh tra viên"),
        ("AUDITOR",              "Kiểm toán viên"),
    };

    /// <summary>Number of canonical roles seeded per tenant.</summary>
    public static int RoleCount => RoleDefinitions.Length;

    public async Task StartAsync(CancellationToken ct)
    {
        if (!_opts.Enabled)
        {
            logger.LogDebug("[TenantRoleSeeder] Disabled — skipping role seed");
            return;
        }

        foreach (var profile in TenantSeedProfiles.All)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            // Roles carry a TenantId; require the tenant row to exist (TenantSeeder runs first) so we
            // never seed roles for a tenant that isn't there. Skip-with-warning keeps a misconfig safe.
            if (!await db.Tenants.AnyAsync(t => t.PublicId == profile.TenantId, ct))
            {
                logger.LogWarning(
                    "[TenantRoleSeeder] Tenant row for {Code} ({TenantId}) not found — skipping role seed. " +
                    "Enable the TenantSeeder (Identity:TenantSeed) so tenant rows exist first.",
                    profile.Code, profile.TenantId);
                continue;
            }

            var normalizer = scope.ServiceProvider.GetRequiredService<ILookupNormalizer>();
            await SeedRolesAsync(db, normalizer, profile.TenantId, profile.RolePrefix, logger, ct);
        }

        // Completion marker (polled by reset-all.ps1) — logged once all tenants are processed.
        logger.LogInformation("[TenantRoleSeeder] All tenants complete");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Seeds the canonical business roles for one tenant with the given code prefix. Idempotent per
    /// (Code, TenantId). Returns the number of roles newly created.
    ///
    /// Inserts directly via the DbContext rather than RoleManager: the default RoleValidator enforces
    /// GLOBAL NormalizedName uniqueness (FindByNameAsync has no tenant scope), which would reject a
    /// second tenant's identically-named roles. Roles here are tenant-scoped, so correctness is
    /// enforced by the (TenantId, NormalizedName) + (Code, TenantId) unique indexes instead. We set
    /// NormalizedName via the same ILookupNormalizer RoleManager would use, so lookups stay consistent.
    /// Static so tests can drive it for any tenant/prefix.
    /// </summary>
    public static async Task<int> SeedRolesAsync(
        IdentityDbContext db,
        ILookupNormalizer normalizer,
        Guid tenantId,
        string rolePrefix,
        ILogger logger,
        CancellationToken ct)
    {
        int created = 0, skipped = 0;

        foreach (var (suffix, name) in RoleDefinitions)
        {
            ct.ThrowIfCancellationRequested();
            var code = rolePrefix + suffix;

            var exists = await db.Roles.AnyAsync(r => r.Code == code && r.TenantId == tenantId, ct);
            if (exists)
            {
                skipped++;
                continue;
            }

            db.Roles.Add(new ApplicationRole
            {
                Id = Guid.NewGuid(),
                Name = name,
                NormalizedName = normalizer.NormalizeName(name),
                Code = code,
                RoleType = RoleType.Business,
                IsActive = true,
                TenantId = tenantId,
                ConcurrencyStamp = Guid.NewGuid().ToString(),
            });
            created++;
        }

        if (created > 0)
            await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "[TenantRoleSeeder] tenant {TenantId}: created={Created}, skipped={Skipped} (of {Total})",
            tenantId, created, skipped, RoleDefinitions.Length);
        return created;
    }
}
