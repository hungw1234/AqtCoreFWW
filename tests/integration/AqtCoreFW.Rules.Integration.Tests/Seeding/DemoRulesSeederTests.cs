using AqtCoreFW.Rules.Domain.Entities;
using AqtCoreFW.Rules.Domain.Enums;
using AqtCoreFW.Rules.Infrastructure.Persistence;
using AqtCoreFW.Rules.Infrastructure.Seeding;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Seeding;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AqtCoreFW.Rules.Integration.Tests.Seeding;

/// <summary>
/// Integration coverage for <see cref="DemoRulesSeeder"/>. The seeder stages one Draft RuleSet + two
/// expression rules + one decision table per tenant (definitions only — no activation, no version
/// snapshot). Real-SQL because the value under test is the mixed save shape (Rules fixed up via the
/// RuleSet navigation in one SaveChanges, DecisionTable keyed by the DB-assigned RuleSet.Id in a
/// second) and the (TenantId, Name) idempotency probe, neither of which an in-memory provider
/// exercises faithfully.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class DemoRulesSeederTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public DemoRulesSeederTests(WebAppFixture app) => _app = app;

    // ── T1: one Draft RuleSet + 2 rules + 1 decision table per tenant, FKs + diacritics correct ──

    [Fact]
    public async Task T1_Seeds_RuleSetWithRulesAndDecisionTable_PerTenant_WithValidFks()
    {
        await SeedTenantAsync(TenantSeedProfiles.BoTaiChinh);
        await SeedTenantAsync(TenantSeedProfiles.BoNoiVu);

        foreach (var profile in TenantSeedProfiles.All)
        {
            await using var ctx = NewFilterFreeCtx();

            var ruleSet = await ctx.RuleSets
                .IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(r => r.TenantId == profile.TenantId && r.Name == DemoRulesSeeder.DemoRuleSetName);

            ruleSet.Status.Should().Be(RuleSetStatus.Draft, $"{profile.Code} demo rule set must stay Draft");
            ruleSet.Category.Should().Be("demo");

            var rules = await ctx.Rules
                .IgnoreQueryFilters().AsNoTracking()
                .Where(r => r.RuleSetId == ruleSet.Id)
                .ToListAsync();

            rules.Should().HaveCount(2, "the seeder authors two expression rules");
            rules.Should().Contain(r => r.Name == "Số tiền tối thiểu" && r.FailureAction == FailureAction.Throw,
                "Vietnamese diacritics + FailureAction must round-trip");
            rules.Should().OnlyContain(r => r.RuleSetId == ruleSet.Id,
                "rules must be fixed up to the DB-assigned RuleSet.Id via the navigation");

            var decisionTable = await ctx.DecisionTables
                .IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(d => d.TenantId == profile.TenantId && d.RuleSetId == ruleSet.Id);

            decisionTable.Name.Should().Be("Bảng định tuyến phê duyệt (demo)", "diacritics round-trip");
            decisionTable.RuleSetId.Should().Be(ruleSet.Id,
                "the second save phase must key the decision table to the assigned RuleSet.Id");
        }
    }

    // ── T2: re-running the seeder is a no-op (idempotent per TenantId + Name) ──

    [Fact]
    public async Task T2_SecondRun_IsNoOp_NoDuplicates()
    {
        var profile = TenantSeedProfiles.BoNoiVu;

        await SeedTenantAsync(profile);
        var (ruleSetsBefore, rulesBefore, tablesBefore) = await CountAsync(profile);

        // Faithful second run through a fresh context (mirrors a second startup).
        await SeedTenantAsync(profile);
        var (ruleSetsAfter, rulesAfter, tablesAfter) = await CountAsync(profile);

        ruleSetsBefore.Should().Be(1);
        rulesBefore.Should().Be(2);
        tablesBefore.Should().Be(1);
        ruleSetsAfter.Should().Be(1, "idempotency: a second seed run must not create a duplicate rule set");
        rulesAfter.Should().Be(2, "no duplicate rules on re-run");
        tablesAfter.Should().Be(1, "no duplicate decision table on re-run");
    }

    // ── T3: the (TenantId, Name) key is tenant-scoped — both tenants share a Name as distinct rows ──

    [Fact]
    public async Task T3_SameName_AcrossTenants_AreDistinctRows()
    {
        await SeedTenantAsync(TenantSeedProfiles.BoTaiChinh);
        await SeedTenantAsync(TenantSeedProfiles.BoNoiVu);

        await using var ctx = NewFilterFreeCtx();
        var ruleSets = await ctx.RuleSets
            .IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.Name == DemoRulesSeeder.DemoRuleSetName
                        && (r.TenantId == TenantSeedProfiles.BoTaiChinh.TenantId
                            || r.TenantId == TenantSeedProfiles.BoNoiVu.TenantId))
            .ToListAsync();

        ruleSets.Should().HaveCount(2, "one demo rule set per tenant, scoped by TenantId");
        ruleSets.Select(r => r.TenantId).Should().OnlyHaveUniqueItems("one per tenant");
        ruleSets.Select(r => r.Id).Should().OnlyHaveUniqueItems("distinct IDENTITY rows");
        ruleSets.Select(r => r.PublicId).Should().OnlyHaveUniqueItems("distinct external AKs");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Drives the seeder's static per-tenant path through a fresh filter-free context (mirrors startup).</summary>
    private async Task SeedTenantAsync(TenantSeedProfile profile)
    {
        using var scope = _app.Services.CreateScope();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
        await using var ctx = NewFilterFreeCtx();
        await DemoRulesSeeder.SeedTenantRulesAsync(
            ctx, profile, gen, NullLogger.Instance, CancellationToken.None);
    }

    private async Task<(int RuleSets, int Rules, int Tables)> CountAsync(TenantSeedProfile profile)
    {
        await using var ctx = NewFilterFreeCtx();
        var ruleSetIds = await ctx.RuleSets
            .IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.TenantId == profile.TenantId && r.Name == DemoRulesSeeder.DemoRuleSetName)
            .Select(r => r.Id)
            .ToListAsync();

        var rules = await ctx.Rules
            .IgnoreQueryFilters().AsNoTracking()
            .Where(r => ruleSetIds.Contains(r.RuleSetId))
            .CountAsync();

        var tables = await ctx.DecisionTables
            .IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.TenantId == profile.TenantId && ruleSetIds.Contains(d.RuleSetId))
            .CountAsync();

        return (ruleSetIds.Count, rules, tables);
    }

    /// <summary>Filter-free RulesDbContext (system seed context) — reads/writes span tenants like the seeder.</summary>
    private RulesDbContext NewFilterFreeCtx() =>
        new(_app.Services.GetRequiredService<DbContextOptions<RulesDbContext>>(),
            SystemSeederTenantContext.Instance);
}
