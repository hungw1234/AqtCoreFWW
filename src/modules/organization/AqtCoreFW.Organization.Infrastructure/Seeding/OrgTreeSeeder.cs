using AqtCoreFW.Organization.Domain.Entities;
using AqtCoreFW.Organization.Infrastructure.Persistence;
using AqtCoreFW.Organization.Infrastructure.Seeding.Loaders;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Hosting;
using AqtCoreFW.SharedKernel.Application.Seeding;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Organization.Infrastructure.Seeding;

/// <summary>
/// Tenant-agnostic OrgUnit hierarchy seeder. Replaces the per-tenant BtcOrgUnitSeeder: one code path
/// seeds any tenant's tree from a <see cref="TenantSeedProfile"/> (identity/scale) + an
/// <see cref="OrgTreePlan"/> (data sources, resolved by <see cref="OrgTreePlans"/>).
///
/// The whole tree is data-driven: every <c>OrgUnitSeedDto</c> carries Level + ParentCode, so the
/// generic loop seeds level-ascending and resolves each node's parent by code — no per-level methods.
/// The one BTC-specific exception (KBNN treasury placeholders generated from masterdata.Wards) is a
/// plan-gated step in the OrgTreeSeeder.Treasury partial.
///
/// Config-gated (Organization:OrgTreeSeed) and idempotent per-code. Requires the proactive tenant
/// seeder (Identity:TenantSeed) to have created each tenant's row first — org units carry a TenantId
/// that references identity.Tenants.PublicId, so a tenant with no row is skipped rather than seeded
/// into orphaned rows.
/// </summary>
public sealed partial class OrgTreeSeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<OrgTreeSeederOptions> options,
    IGuidGenerator gen,
    ILogger<OrgTreeSeeder> logger) : IHostedService, IIntegrationTestSeeder
{
    private readonly OrgTreeSeederOptions _opts = options.Value;

    public async Task StartAsync(CancellationToken ct)
    {
        if (!_opts.Enabled)
        {
            logger.LogInformation("[OrgTreeSeeder] Disabled via config — skipping");
            return;
        }

        foreach (var profile in TenantSeedProfiles.All)
        {
            var plan = OrgTreePlans.TryGet(profile.Code);
            if (plan is null)
            {
                logger.LogInformation(
                    "[OrgTreeSeeder] No tree plan for tenant {Code} — skipping (not yet defined)",
                    profile.Code);
                continue;
            }

            using var scope = scopeFactory.CreateScope();
            var dbOptions = scope.ServiceProvider.GetRequiredService<DbContextOptions<OrgDbContext>>();
            await using var ctx = new OrgDbContext(dbOptions, SystemSeederTenantContext.Instance);

            // Org units carry a TenantId referencing identity.Tenants.PublicId. Seeding a tenant whose
            // row doesn't exist would create orphaned units, so require the proactive TenantSeeder
            // (Identity:TenantSeed, shares TenantSeedProfiles) to have run first. Skip-with-warning
            // keeps a "OrgTreeSeed on, TenantSeed off" misconfig safe instead of silently orphaning.
            if (!await TenantRowExistsAsync(ctx, profile.TenantId, ct))
            {
                logger.LogWarning(
                    "[OrgTreeSeeder] Tenant row for {Code} ({TenantId}) not found — skipping org seed. " +
                    "Enable the TenantSeeder (Identity:TenantSeed) so tenant rows exist first.",
                    profile.Code, profile.TenantId);
                continue;
            }

            // Per-tenant depth comes from the profile (BTC=4, BNV=2); the global option is a ceiling
            // so an operator can cap every tenant shallower for a fast dev seed without touching profiles.
            var effectiveMaxLevel = Math.Min(profile.MaxLevel, _opts.MaxLevel);

            logger.LogInformation(
                "[OrgTreeSeeder] Seeding tree for {Code} (tenant {TenantId}, MaxLevel={MaxLevel})",
                profile.Code, profile.TenantId, effectiveMaxLevel);

            await SeedTreeAsync(ctx, profile.TenantId, plan, gen,
                effectiveMaxLevel, _opts.SkipTreasuryWardPlaceholders, logger, ct);
        }

        // Completion marker (polled by reset-all.ps1). Logged once all tenants are processed,
        // whether they were seeded or skipped, so the poller never hangs on a skipped tenant.
        logger.LogInformation("[OrgTreeSeeder] All tenants complete");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Cross-schema existence check for the tenant's row (identity.Tenants.PublicId). Read via Dapper
    /// on the same connection — same approach the treasury step uses for masterdata.Wards — to avoid a
    /// compile-time dependency on the Identity module.
    /// </summary>
    private static async Task<bool> TenantRowExistsAsync(OrgDbContext ctx, Guid tenantId, CancellationToken ct)
    {
        var connStr = ctx.Database.GetConnectionString();
        if (string.IsNullOrEmpty(connStr))
            return false;

        await using var conn = new SqlConnection(connStr);
        const string sql = "SELECT TOP 1 1 FROM [identity].[Tenants] WHERE PublicId = @id";
        var cmd = new CommandDefinition(sql, new { id = tenantId }, cancellationToken: ct);
        return await conn.ExecuteScalarAsync<int?>(cmd) is not null;
    }

    /// <summary>
    /// Seeds a tenant's org tree from the plan's JSON resources (level-ascending, parent-by-code),
    /// then runs the plan-gated treasury ward step. Idempotent: per-code existence check per level,
    /// so a partial-failure re-run inserts only what's missing. Static + ctx-injected so tests can
    /// drive it directly against a fresh tenant.
    /// </summary>
    public static async Task SeedTreeAsync(
        OrgDbContext ctx,
        Guid tenantId,
        OrgTreePlan plan,
        IGuidGenerator gen,
        int maxLevel,
        bool skipTreasuryWardPlaceholders,
        ILogger logger,
        CancellationToken ct)
    {
        // Load every plan resource into one node set; Level + ParentCode drive ordering, not file order.
        var nodes = new List<Models.OrgUnitSeedDto>();
        foreach (var resource in plan.JsonResources)
        {
            try
            {
                nodes.AddRange(JsonSeedDataLoader.LoadFromResource(resource));
            }
            catch (InvalidOperationException ex)
            {
                logger.LogError(ex, "[OrgTreeSeeder] Failed to load {Resource} — skipping it", resource);
            }
        }

        // Level >= 1 excludes sentinel/meta records (e.g. a "_META" provenance row with level 0).
        var levels = nodes.Select(n => n.Level).Distinct()
            .Where(l => l >= 1 && l <= maxLevel)
            .OrderBy(l => l);

        foreach (var level in levels)
        {
            var levelNodes = nodes.Where(n => n.Level == level).ToList();

            // Parent map = every already-seeded node above this level (DB-fresh, idempotent-safe).
            var parentMap = level <= 1
                ? new Dictionary<string, long>()
                : await ctx.OrgUnits.IgnoreQueryFilters()
                    .Where(o => o.TenantId == tenantId && o.Level < level)
                    .ToDictionaryAsync(o => o.Code, o => o.Id, ct);

            // Per-code idempotency for this level.
            var levelCodes = levelNodes.Select(n => n.Code).ToList();
            var existing = await ctx.OrgUnits.IgnoreQueryFilters()
                .Where(o => o.TenantId == tenantId && levelCodes.Contains(o.Code))
                .Select(o => o.Code)
                .ToHashSetAsync(ct);

            var toInsert = new List<OrgUnit>(levelNodes.Count - existing.Count);
            var skippedNoParent = 0;

            foreach (var n in levelNodes.Where(n => !existing.Contains(n.Code)))
            {
                long? parentId = null;
                if (n.ParentCode is not null)
                {
                    if (!parentMap.TryGetValue(n.ParentCode, out var pid))
                    {
                        skippedNoParent++;
                        logger.LogWarning(
                            "[OrgTreeSeeder] L{Level} {Code} skipped — parent {ParentCode} not found",
                            level, n.Code, n.ParentCode);
                        continue;
                    }
                    parentId = pid;
                }

                toInsert.Add(OrgUnit.Create(gen, n.Name, n.NameEn, n.Code, tenantId, parentId, level));
            }

            if (toInsert.Count > 0)
            {
                await OrgSeedHelpers.BatchInsertAsync(ctx, toInsert, batchSize: 500, ct);
            }

            // Detach so the next level's parent-map query reads from the DB and tracking stays bounded.
            ctx.ChangeTracker.Clear();

            logger.LogInformation(
                "[OrgTreeSeeder] L{Level} complete: inserted={Inserted}, skipped(existing)={Existing}, skipped(no parent)={NoParent}",
                level, toInsert.Count, existing.Count, skippedNoParent);
        }

        if (plan.GenerateTreasuryWardPlaceholders && !skipTreasuryWardPlaceholders && maxLevel >= 4)
        {
            await SeedTreasuryWardPlaceholdersAsync(ctx, tenantId, gen, logger, ct);
        }
    }
}
