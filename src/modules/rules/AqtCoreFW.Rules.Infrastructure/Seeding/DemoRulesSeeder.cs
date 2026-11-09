using AqtCoreFW.Rules.Domain.Entities;
using AqtCoreFW.Rules.Domain.Enums;
using AqtCoreFW.Rules.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Hosting;
using AqtCoreFW.SharedKernel.Application.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Rules.Infrastructure.Seeding;

/// <summary>
/// Tenant-agnostic demo rules seeder: seeds one Draft <see cref="RuleSet"/> with two expression
/// <see cref="Rule"/>s plus one <see cref="DecisionTable"/> per <see cref="TenantSeedProfiles"/> entry
/// (BTC + BNV) — the two rule-authoring modes the module supports. Definitions only; no rule evaluation
/// or test-case runs.
///
/// <para>Left in <b>Draft</b> (<see cref="RuleSet.Activate"/> would flip it to Active and raise a domain
/// event): the seeder only stages the definition; an operator activates it. No version snapshot is
/// seeded (a <see cref="RuleVersion"/> is created only on activation).</para>
///
/// <para>Config-gated (<c>Rules:DemoRulesSeed</c>, default OFF) and idempotent per (TenantId, Name).
/// Self-contained: RuleSet/Rule/DecisionTable carry no cross-module FK, so this is module-resident
/// (mirrors the Workflow / Forms / Integration demo seeders) and needs no tenant-existence guard (a
/// RuleSet's TenantId is an unenforced Guid stamp).</para>
///
/// <para>Cross-tenant reads/writes from a single startup scope rely on the dev/seed connection being
/// db_owner (the RLS predicate bypasses for db_owner; <see cref="SystemSeederTenantContext"/> leaves
/// SESSION_CONTEXT unset and reports IsSystemAdmin so the EF global filter is bypassed too).</para>
/// </summary>
public sealed class DemoRulesSeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<DemoRulesSeederOptions> options,
    IGuidGenerator gen,
    ILogger<DemoRulesSeeder> logger) : IHostedService, IIntegrationTestSeeder
{
    private readonly DemoRulesSeederOptions _opts = options.Value;

    /// <summary>Stable RuleSet name (the idempotency key together with TenantId).</summary>
    public const string DemoRuleSetName = "Bộ quy tắc nghiệp vụ (demo)";

    // Seeder-authored rows are attributed to the system actor (all-zero Guid, mirrors E2E seeder).
    private static readonly Guid SystemActorId = Guid.Empty;

    public async Task StartAsync(CancellationToken ct)
    {
        if (!_opts.Enabled)
        {
            logger.LogDebug("[DemoRulesSeeder] Disabled — skipping rules demo seed");
            return;
        }

        foreach (var profile in TenantSeedProfiles.All)
        {
            using var scope = scopeFactory.CreateScope();
            var dbOptions = scope.ServiceProvider.GetRequiredService<DbContextOptions<RulesDbContext>>();

            // Construct with the system seed context so the tenant global filter is bypassed — the
            // idempotency probe + inserts span both tenants from one startup scope (mirror the other
            // demo seeders).
            await using var ctx = new RulesDbContext(dbOptions, SystemSeederTenantContext.Instance);

            await SeedTenantRulesAsync(ctx, profile, gen, logger, ct);
        }

        logger.LogInformation("[DemoRulesSeeder] All tenants complete");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Seeds the demo RuleSet for one tenant. Idempotent per (TenantId, Name): a second run is a no-op.
    /// Two-phase save: (1) the RuleSet + its two Rules in one SaveChanges — the Rules are added through
    /// the mapped <see cref="RuleSet.Rules"/> navigation, so EF fixes up their RuleSetId from the
    /// DB-assigned RuleSet.Id; (2) the DecisionTable, which is NOT a navigation on RuleSet and so needs
    /// the now-assigned RuleSet.Id passed explicitly. Both phases are wrapped in one transaction so a
    /// phase-2 fault cannot leave a committed RuleSet whose probe (keyed on the RuleSet) would skip it
    /// forever on a plain restart. Static + ctx-injected so tests can drive it directly.
    /// </summary>
    public static async Task SeedTenantRulesAsync(
        RulesDbContext ctx,
        TenantSeedProfile profile,
        IGuidGenerator gen,
        ILogger logger,
        CancellationToken ct)
    {
        // Idempotency probe on (TenantId, Name). RuleSet has no unique (TenantId, Name) index and no
        // natural Code, so this is a convention key, not a DB-enforced one — acceptable because seeders
        // run serially at a single startup (no concurrent double-seed) and the data is dev-only; not
        // worth a migration for a gated-OFF demo. IgnoreQueryFilters also drops the soft-delete filter,
        // so a soft-deleted demo RuleSet is treated as present and not resurrected (intended).
        var exists = await ctx.RuleSets
            .IgnoreQueryFilters()
            .AnyAsync(r => r.TenantId == profile.TenantId && r.Name == DemoRuleSetName, ct);
        if (exists)
        {
            logger.LogInformation(
                "[DemoRulesSeeder] {Code}: demo rule set already present — skipping", profile.Code);
            return;
        }

        await using var tx = await ctx.Database.BeginTransactionAsync(ct);

        // ── Phase 1: RuleSet (Draft) + its two expression rules (FK fixed up via the Rules nav) ──────
        var ruleSet = RuleSet.Create(
            gen,
            name: DemoRuleSetName,
            tenantId: profile.TenantId,
            createdBy: SystemActorId,
            description: $"Bộ quy tắc nghiệp vụ demo cho {profile.Name}.",
            category: "demo",
            targetEntityType: "Generic");

        ruleSet.AddRule(Rule.Create(
            gen, ruleSetId: 0, name: "Số tiền tối thiểu",
            expression: """{"field":"so_tien","operator":"GreaterThanOrEqual","value":1000000}""",
            createdBy: SystemActorId, priority: 1, enabled: true,
            errorMessage: "Số tiền phải lớn hơn hoặc bằng 1.000.000.",
            failureAction: FailureAction.Throw));

        ruleSet.AddRule(Rule.Create(
            gen, ruleSetId: 0, name: "Mức độ ưu tiên hợp lệ",
            expression: """{"field":"muc_do_uu_tien","operator":"In","value":["Thấp","Trung bình","Cao"]}""",
            createdBy: SystemActorId, priority: 2, enabled: true,
            errorMessage: null, failureAction: FailureAction.Skip));

        await ctx.RuleSets.AddAsync(ruleSet, ct);
        await ctx.SaveChangesAsync(ct); // ruleSet.Id assigned by IDENTITY; Rule.RuleSetId fixed up via nav

        // ── Phase 2: DecisionTable keyed by the assigned ruleSet.Id (no nav on RuleSet) ─────────────
        var decisionTable = DecisionTable.Create(
            ruleSetId: ruleSet.Id,
            name: "Bảng định tuyến phê duyệt (demo)",
            inputColumns: """[{"name":"so_tien","type":"number"}]""",
            outputColumns: """[{"name":"cap_phe_duyet","type":"string"}]""",
            rows: """[{"so_tien":"< 10000000","cap_phe_duyet":"Trưởng phòng"},{"so_tien":">= 10000000","cap_phe_duyet":"Giám đốc"}]""",
            tenantId: profile.TenantId,
            createdBy: SystemActorId);

        await ctx.DecisionTables.AddAsync(decisionTable, ct);
        await ctx.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        logger.LogInformation(
            "[DemoRulesSeeder] {Code}: seeded demo rule set (Draft) with 2 rules + 1 decision table", profile.Code);
    }
}
