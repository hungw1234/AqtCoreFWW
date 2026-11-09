using System.Diagnostics;
using AqtCoreFW.Organization.Domain.Entities;
using AqtCoreFW.Organization.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Application.Hosting;
using AqtCoreFW.SharedKernel.Application.Seeding;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Organization.Infrastructure.Seeding;

/// <summary>
/// Tenant-agnostic IHostedService that seeds UserOrgUnitAssignment + StaffPositionHistory rows for every
/// <see cref="TenantSeedProfiles"/> entry (BTC + BNV). Owns the assignment + history seeding path.
///
/// Runs AFTER the identity user seeder (TenantUserSeeder, Identity.Infrastructure) — users must exist
/// first. Graceful PER-TENANT degradation: a tenant with no seed users yet logs a warning + is skipped
/// (next restart completes; the dev 2-restart convergence settles cleanly).
///
/// Cross-module pattern: Organization.Infrastructure cannot reference Identity.Infrastructure, so user
/// rows are read via raw Dapper SQL (identity.AspNetUsers) in the same DB. Writes go through OrgDbContext
/// with <see cref="SystemSeederTenantContext"/> bypassing the global tenant filter.
///
/// Self-heal: the identity user seeder wipes + recreates seed users (new GUIDs) on drift; that wipe does
/// NOT cascade into the Organization schema, so prior assignments are orphaned. <see cref="SeedOrphanPurger"/>
/// removes those tenant-scoped orphans before re-seeding, so the assignment set always tracks the current
/// user GUIDs. The position-derived logic is level-agnostic (reads only PrimaryOrgUnitId + PositionName),
/// so a shallow tree (BNV, 2 levels) seeds identically to a deep one (BTC, 4 levels).
/// </summary>
public sealed class TenantAssignmentSeeder(
    IServiceScopeFactory scopeFactory,
    IWebHostEnvironment env,
    IOptions<TenantAssignmentSeederOptions> options,
    IConfiguration config,
    ILogger<TenantAssignmentSeeder> logger) : IHostedService, IIntegrationTestSeeder
{
    private readonly TenantAssignmentSeederOptions _opts = options.Value;

    public async Task StartAsync(CancellationToken ct)
    {
        if (!env.IsDevelopment())
        {
            logger.LogDebug("[TenantAssignmentSeeder] skipped (not Development)");
            return;
        }
        if (!_opts.Enabled)
        {
            logger.LogInformation("[TenantAssignmentSeeder] disabled via config — skipping");
            return;
        }

        var connStr = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Default missing — required by TenantAssignmentSeeder");

        var sw = Stopwatch.StartNew();
        bool anyTenantMissingUsers = false;
        foreach (var profile in TenantSeedProfiles.All)
        {
            using var scope = scopeFactory.CreateScope();
            var dbOptions = scope.ServiceProvider.GetRequiredService<DbContextOptions<OrgDbContext>>();
            await using var orgDb = new OrgDbContext(dbOptions, SystemSeederTenantContext.Instance);

            var result = await SeedAssignmentsForProfileAsync(connStr, orgDb, profile, _opts, logger, ct);
            if (!result.UsersPresent) anyTenantMissingUsers = true;
        }

        // Convergence contract: only emit the completion marker when EVERY tenant's users were present.
        // If a tenant's users have not been seeded yet (the
        // identity user seeder runs in a different module — cross-module boot ordering), withhold the
        // completion marker and emit the retry marker so the dev reset-all restarts and converges on the
        // next boot. Emitting "All tenants complete" prematurely would let reset-all declare success with
        // zero assignments.
        if (anyTenantMissingUsers)
        {
            logger.LogWarning(
                "[TenantAssignmentSeeder] incomplete — some tenants had no seed users yet; will retry next startup ({Elapsed}s)",
                sw.Elapsed.TotalSeconds);
        }
        else
        {
            logger.LogInformation(
                "[TenantAssignmentSeeder] All tenants complete in {Elapsed}s", sw.Elapsed.TotalSeconds);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Seeds assignments + histories for one tenant. Static so tests can drive it directly with a real
    /// SQL connection + OrgDbContext (no IHostedService / scope-factory plumbing). Idempotent (per-user
    /// HashSet lookup), orphan-self-healing, and race-tolerant (returns with a warning when the tenant's
    /// users have not been seeded yet). Returns the rows inserted this pass.
    /// </summary>
    public static async Task<(int Assignments, int Histories, bool UsersPresent)> SeedAssignmentsForProfileAsync(
        string connStr,
        OrgDbContext orgDb,
        TenantSeedProfile profile,
        TenantAssignmentSeederOptions opts,
        ILogger logger,
        CancellationToken ct)
    {
        var tenantId = profile.TenantId;

        // Re-query the tenant's seed users via Dapper (cross-module safe — no Identity EF dep).
        // EmployeeCode prefix is the tenant's natural key; users get fresh GUIDs on every drift-reseed,
        // so we always resolve the CURRENT GUID set here rather than caching ids across boots.
        // UserCodePrefix is trusted seed config (e.g. "BTC-SEED-"); it must stay free of SQL LIKE
        // metacharacters (% _ [) since it flows unescaped into the LIKE pattern below.
        List<UserRow> users;
        await using (var conn = new SqlConnection(connStr))
        {
            users = (await conn.QueryAsync<UserRow>(@"
                SELECT Id, PrimaryOrgUnitId, PositionName
                FROM [identity].AspNetUsers
                WHERE TenantId = @TenantId AND EmployeeCode LIKE @Prefix",
                new { TenantId = tenantId, Prefix = profile.UserCodePrefix + "%" })).ToList();
        }

        if (users.Count == 0)
        {
            logger.LogWarning(
                "[TenantAssignmentSeeder] {Code}: no seed users yet — the identity user seeder must run " +
                "first. Will retry next startup.", profile.Code);
            return (0, 0, false);
        }

        // SELF-HEAL: purge orphan assignments + histories (UserId no longer in AspNetUsers) tenant-scoped
        // before re-seed, so a prior boot's now-deleted-user GUIDs do not linger.
        await SeedOrphanPurger.PurgeAsync(connStr, tenantId, opts.OrphanPurgeCeiling, logger, ct);

        // Pre-load already-assigned user IDs → resume-safe + idempotent.
        // NOTE: do NOT gate on total tenant Assignments count — e2e seed or other fixtures may have
        // inserted non-seed rows which would false-trigger a skip. Per-user HashSet lookup is the sole
        // idempotency mechanism.
        var alreadyAssignedIds = await orgDb.Assignments
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId)
            .Select(a => a.UserId)
            .ToHashSetAsync(ct);

        // Fast-path: every seed user already assigned → skip.
        if (users.All(u => alreadyAssignedIds.Contains(u.Id)))
        {
            logger.LogInformation(
                "[TenantAssignmentSeeder] {Code}: all {Count} users already assigned — skip",
                profile.Code, users.Count);
            return (0, 0, true);
        }

        // OrgUnit is T2: PrimaryOrgUnitId in identity.AspNetUsers = PublicId (Guid).
        // UserOrgUnitAssignment.OrgUnitId = long (FK to OrgUnit.Id surrogate).
        // Resolve all referenced PublicIds → internal long Ids in a single query.
        var referencedPublicIds = users
            .Where(u => u.PrimaryOrgUnitId.HasValue)
            .Select(u => u.PrimaryOrgUnitId!.Value)
            .Distinct()
            .ToList();

        Dictionary<Guid, long> publicIdToLongId = referencedPublicIds.Count > 0
            ? await orgDb.OrgUnits
                .IgnoreQueryFilters()
                .Where(o => referencedPublicIds.Contains(o.PublicId))
                .ToDictionaryAsync(o => o.PublicId, o => o.Id, CancellationToken.None)
            : [];

        var assignments = new List<UserOrgUnitAssignment>();
        var histories = new List<StaffPositionHistory>();
        int skipped = 0;

        foreach (var u in users)
        {
            if (alreadyAssignedIds.Contains(u.Id)) { skipped++; continue; }
            if (u.PrimaryOrgUnitId is null || string.IsNullOrEmpty(u.PositionName)) { skipped++; continue; }

            if (!publicIdToLongId.TryGetValue(u.PrimaryOrgUnitId.Value, out var orgUnitLongId))
            {
                logger.LogWarning(
                    "[TenantAssignmentSeeder] {Code}: User {UserId}: OrgUnit PublicId {PublicId} not found — skipped",
                    profile.Code, u.Id, u.PrimaryOrgUnitId.Value);
                skipped++;
                continue;
            }

            assignments.Add(UserOrgUnitAssignment.Create(
                u.Id, orgUnitLongId, u.PositionName, tenantId));

            histories.Add(StaffPositionHistory.Create(
                u.Id, orgUnitLongId, u.PositionName, tenantId,
                DateTimeOffset.UtcNow.AddYears(-1)));
        }

        // Bulk insert via EF — assignments + histories TOGETHER per batch so the pair rolls back
        // atomically if a batch fails mid-flight (prevents orphan assignment-without-history state).
        int insertedAssign = 0, insertedHist = 0;
        for (int i = 0; i < assignments.Count; i += opts.BatchSize)
        {
            var end = Math.Min(i + opts.BatchSize, assignments.Count);
            var size = end - i;
            orgDb.Assignments.AddRange(assignments.GetRange(i, size));
            orgDb.PositionHistories.AddRange(histories.GetRange(i, size));
            await orgDb.SaveChangesAsync(ct);
            orgDb.ChangeTracker.Clear();
            insertedAssign += size;
            insertedHist += size;
        }

        logger.LogInformation(
            "[TenantAssignmentSeeder] {Code} complete: assignments={Assignments}, histories={Histories}, skipped={Skipped} (of {Users} users)",
            profile.Code, insertedAssign, insertedHist, skipped, users.Count);

        return (insertedAssign, insertedHist, true);
    }

    /// <summary>
    /// Dapper row type — raw cross-module read from identity.AspNetUsers. Class form (not positional
    /// record) so Dapper materializes via property setters: positional records with nullable params
    /// (Guid?, string?) lack the parameterless ctor Dapper needs and silently fail on null rows.
    /// </summary>
    private sealed class UserRow
    {
        public Guid Id { get; init; }
        public Guid? PrimaryOrgUnitId { get; init; }
        public string? PositionName { get; init; }
    }
}
