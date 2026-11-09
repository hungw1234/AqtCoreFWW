using System.Diagnostics;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Application.Hosting;
using AqtCoreFW.SharedKernel.Application.Seeding;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Identity.Infrastructure.Seeding;

/// <summary>
/// Tenant-agnostic identity seeder: seeds the 3-pass user population (coverage / weight-fill /
/// dev-reserve) and the position-derived user→role bindings for every <see cref="TenantSeedProfiles"/>
/// entry (BTC + BNV). Owns the identity user + user-role seeding; runs after
/// <see cref="TenantRoleSeeder"/> (needs the business roles to bind against) and reads the org tree
/// each tenant covers from organization.OrgUnits via Dapper (cross-module — no EF ref to Organization).
///
/// Per-tenant identity literals (code/email prefixes, target count, domain) come from the profile, so
/// one code path drives both ministries. Idempotent per tenant (count-gated by profile.UserCodePrefix)
/// and resume-safe; requires the tenant row + org tree to exist first (skip-with-warning otherwise, so
/// the dev 2-restart convergence settles cleanly). Default OFF (see <see cref="TenantUserSeederOptions"/>).
/// </summary>
public sealed partial class TenantUserSeeder(
    IServiceScopeFactory scopeFactory,
    IWebHostEnvironment env,
    IOptions<TenantUserSeederOptions> options,
    IConfiguration config,
    ILogger<TenantUserSeeder> logger) : IHostedService, IIntegrationTestSeeder
{
    private readonly TenantUserSeederOptions _opts = options.Value;

    // Shared dev password for all seeded users (one PBKDF2 hash, reused) — dev-only, not a real secret.
    private readonly string _seedPassword =
        config["SeedPasswords:BtcSeedPassword"] ?? "DevAdmin@12345";

    public async Task StartAsync(CancellationToken ct)
    {
        if (!env.IsDevelopment())
        {
            logger.LogDebug("[TenantUserSeeder] skipped (not Development)");
            return;
        }
        if (!_opts.Enabled)
        {
            logger.LogInformation("[TenantUserSeeder] disabled via config — skipping");
            return;
        }

        var connStr = config.GetConnectionString("Default");
        var sw = Stopwatch.StartNew();

        foreach (var profile in TenantSeedProfiles.All)
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            // Users carry a TenantId; require the tenant row to exist (TenantSeeder runs first) so we
            // never seed orphaned users. Skip-with-warning keeps a misconfig / mid-convergence run safe.
            if (!await db.Tenants.AnyAsync(t => t.PublicId == profile.TenantId, ct))
            {
                logger.LogWarning(
                    "[TenantUserSeeder] Tenant row for {Code} ({TenantId}) not found — skipping user seed. " +
                    "Enable the TenantSeeder (Identity:TenantSeed) so tenant rows exist first.",
                    profile.Code, profile.TenantId);
                continue;
            }

            // Load the tenant's org tree (cross-module raw SQL). OrgUnit is T2: long PK + Guid PublicId
            // AK; ApplicationUser.PrimaryOrgUnitId is the Guid → SELECT PublicId AS Id.
            List<OrgUnitRow> orgUnits;
            await using (var conn = new SqlConnection(connStr))
            {
                orgUnits = (await conn.QueryAsync<OrgUnitRow>(
                    "SELECT PublicId AS Id, Code, [Level] FROM organization.OrgUnits WHERE TenantId=@TenantId ORDER BY [Level], Code",
                    new { TenantId = profile.TenantId })).ToList();
            }

            if (orgUnits.Count == 0)
            {
                logger.LogWarning(
                    "[TenantUserSeeder] No OrgUnits for {Code} ({TenantId}) — skipping (ensure OrgTreeSeeder ran first; the dev 2-restart converges)",
                    profile.Code, profile.TenantId);
                continue;
            }

            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationUser>>();

            await SeedUsersForProfileAsync(db, hasher, profile, _opts, orgUnits, _seedPassword, logger, ct);
            await SeedUserRolesForProfileAsync(db, profile, _opts.BatchSize, logger, ct);
        }

        // Completion marker (polled by reset-all.ps1) — logged once all tenants are processed.
        logger.LogInformation("[TenantUserSeeder] All tenants complete in {Elapsed}s", sw.Elapsed.TotalSeconds);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Dapper row type — raw cross-module read from organization.OrgUnits. Public so the seeder's
    /// static core can be driven with a synthetic tree in tests without touching the Organization schema.
    /// Class form (not positional record) so Dapper materializes via property setters.
    /// </summary>
    public sealed class OrgUnitRow
    {
        public Guid Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public int Level { get; init; }
    }
}
