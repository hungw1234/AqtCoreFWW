using System.Net;
using System.Text.Json;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Seeding;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using AqtCoreFW.Workflow.Infrastructure.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AqtCoreFW.Workflow.Integration.Tests;

/// <summary>
/// Integration coverage for <see cref="DemoWorkflowSeeder"/> (option A: seed-correctness +
/// activatability). The seeder leaves definitions in Draft; the operator activates them in the UI,
/// which re-runs the R1-R4 graph validator. T4 drives that activate path through the real endpoint to
/// prove the seeded graph + auto-hierarchy ByRole matrix passes validation (i.e. is activatable).
/// Full instance→inbox routing is left to manual smoke (the read-side ByRole-by-code path is already
/// proven by the precursor's real-SQL candidate-index tests).
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class DemoWorkflowSeederTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public DemoWorkflowSeederTests(WebAppFixture app) => _app = app;

    // Expected ByRole matrix (action → role-code suffixes), mirrors DemoWorkflowSeeder.RoleMatrix.
    private static readonly (string Action, string[] Suffixes)[] ExpectedMatrix =
    [
        ("Submit",  ["SPECIALIST", "OFFICER", "STAFF"]),
        ("Review",  ["DIVISION_HEAD"]),
        ("Approve", ["DEPT_DIRECTOR"]),
        ("Reject",  ["DEPT_DIRECTOR"]),
        ("Close",   ["DEPT_DIRECTOR"]),
    ];

    // ── T1: seeds one Draft definition per tenant with the full 6-state / 6-transition graph ──

    [Fact]
    public async Task T1_Seeds_OneDraftDefinition_PerTenant_WithSixStatesAndTransitions()
    {
        await SeedTenantAsync(TenantSeedProfiles.BoTaiChinh);
        await SeedTenantAsync(TenantSeedProfiles.BoNoiVu);

        foreach (var profile in TenantSeedProfiles.All)
        {
            await using var ctx = NewFilterFreeCtx();
            var def = await ctx.WorkflowDefinitions
                .IgnoreQueryFilters().AsNoTracking()
                .Include(d => d.States)
                .Include(d => d.Transitions)
                .AsSplitQuery()
                .SingleAsync(d => d.TenantId == profile.TenantId
                                  && d.DefinitionKey == DemoWorkflowSeeder.DemoDefinitionKey);

            def.IsActive.Should().BeFalse($"{profile.Code} demo def must be seeded in Draft for operator activation");
            def.Name.Should().Be("Quy trình xử lý hồ sơ (demo)", "Vietnamese diacritics must round-trip (NVARCHAR)");
            def.States.Should().HaveCount(6);
            def.States.Should().ContainSingle(s => s.IsInitial).Which.Name.Should().Be("Draft");
            def.States.Should().ContainSingle(s => s.IsFinal).Which.Name.Should().Be("Closed");
            def.States.Single(s => s.IsInitial).DisplayNameVi.Should().Be("Nháp", "diacritics round-trip");
            def.Transitions.Should().HaveCount(6);
        }
    }

    // ── T2: every human transition carries a ByRole clause with the per-tenant prefixed CODES ──

    [Fact]
    public async Task T2_EveryTransition_HasByRoleAssignment_WithTenantPrefixedCodes()
    {
        await SeedTenantAsync(TenantSeedProfiles.BoTaiChinh);

        var profile = TenantSeedProfiles.BoTaiChinh;
        await using var ctx = NewFilterFreeCtx();

        var def = await ctx.WorkflowDefinitions
            .IgnoreQueryFilters().AsNoTracking()
            .Include(d => d.Transitions)
            .SingleAsync(d => d.TenantId == profile.TenantId
                              && d.DefinitionKey == DemoWorkflowSeeder.DemoDefinitionKey);

        var transitionIds = def.Transitions.Select(t => t.Id).ToList();
        var assignments = await ctx.WorkflowTransitionAssignments
            .IgnoreQueryFilters().AsNoTracking()
            .Include(a => a.Clauses)
            .Where(a => transitionIds.Contains(a.WorkflowTransitionId))
            .ToListAsync();

        // Every transition must have exactly one assignment with exactly one ByRole clause.
        assignments.Should().HaveCount(6, "all six human transitions need an assignment (R2)");

        foreach (var transition in def.Transitions)
        {
            var assignment = assignments.SingleOrDefault(a => a.WorkflowTransitionId == transition.Id);
            assignment.Should().NotBeNull($"transition '{transition.ActionName}' must have an assignment");

            var clause = assignment!.Clauses.Should().ContainSingle().Subject;
            clause.Mode.Should().Be(AssignmentClauseMode.ByRole);

            var expected = ExpectedMatrix.Single(m => m.Action == transition.ActionName)
                .Suffixes.Select(s => profile.RolePrefix + s).ToArray();
            var actual = JsonSerializer.Deserialize<string[]>(clause.RefsJson!);

            actual.Should().BeEquivalentTo(expected,
                $"transition '{transition.ActionName}' must route to the prefixed role codes (CODE, canonical UPPER)");
        }
    }

    // ── T3: re-running the seeder is a no-op (idempotent per TenantId + DefinitionKey) ──

    [Fact]
    public async Task T3_SecondRun_IsNoOp_NoDuplicateDefinitionsOrAssignments()
    {
        var profile = TenantSeedProfiles.BoNoiVu;

        await SeedTenantAsync(profile);
        var (defsBefore, assignmentsBefore) = await CountAsync(profile);

        // Faithful second run through a fresh context (mirrors a second startup).
        await SeedTenantAsync(profile);
        var (defsAfter, assignmentsAfter) = await CountAsync(profile);

        defsBefore.Should().Be(1);
        defsAfter.Should().Be(1, "idempotency: a second seed run must not create a duplicate definition");
        assignmentsAfter.Should().Be(assignmentsBefore, "no duplicate assignments on re-run");
    }

    // ── T4: the seeded Draft graph activates cleanly via the real endpoint (R1-R4 pass) ──

    [Fact]
    public async Task T4_SeededDefinition_ActivatesViaEndpoint_AsTenant()
    {
        // Dedicated throwaway tenant so activating this def never pollutes the Draft assertion in
        // T1 (which checks the shared BoTaiChinh/BoNoiVu defs — the seeder is idempotent, so an
        // activation there would persist and fail T1 under batch ordering).
        var profile = MakeThrowawayProfile(Guid.NewGuid(), "WFT4_");
        await SeedTenantAsync(profile);

        Guid publicId;
        await using (var ctx = NewFilterFreeCtx())
        {
            var def = await ctx.WorkflowDefinitions
                .IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(d => d.TenantId == profile.TenantId
                                  && d.DefinitionKey == DemoWorkflowSeeder.DemoDefinitionKey);
            def.IsActive.Should().BeFalse("precondition: seeder leaves the definition in Draft");
            publicId = def.PublicId;
        }

        // Activate must run AS the owning tenant — the command rejects a tenant mismatch with NotFound.
        var client = _app.CreateAuthenticatedClient(Guid.NewGuid(), profile.TenantId, "SystemAdmin");
        var response = await client.PostAsync($"/api/v1/workflow/definitions/{publicId}/activate", null);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "the seeded auto-hierarchy ByRole graph is structurally valid → R1-R4 pass → 204");

        await using var verifyCtx = NewFilterFreeCtx();
        var activated = await verifyCtx.WorkflowDefinitions
            .IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(d => d.PublicId == publicId);
        activated.IsActive.Should().BeTrue("a valid graph activates → IsActive flips true");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Drives the seeder's static per-tenant path through a fresh filter-free context (mirrors startup).</summary>
    private async Task SeedTenantAsync(TenantSeedProfile profile)
    {
        using var scope = _app.Services.CreateScope();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();
        await using var ctx = NewFilterFreeCtx();
        await DemoWorkflowSeeder.SeedTenantDefinitionAsync(
            ctx, profile, gen, NullLogger.Instance, CancellationToken.None);
    }

    private async Task<(int Defs, int Assignments)> CountAsync(TenantSeedProfile profile)
    {
        await using var ctx = NewFilterFreeCtx();
        var defs = await ctx.WorkflowDefinitions
            .IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.TenantId == profile.TenantId && d.DefinitionKey == DemoWorkflowSeeder.DemoDefinitionKey)
            .Select(d => d.Id)
            .ToListAsync();

        var assignments = await ctx.WorkflowTransitionAssignments
            .IgnoreQueryFilters().AsNoTracking()
            .Where(a => ctx.WorkflowTransitions
                .Where(t => defs.Contains(t.DefinitionId))
                .Select(t => t.Id)
                .Contains(a.WorkflowTransitionId))
            .CountAsync();

        return (defs.Count, assignments);
    }

    /// <summary>Filter-free WorkflowDbContext (system seed context) — reads/writes span tenants like the seeder.</summary>
    private WorkflowDbContext NewFilterFreeCtx() =>
        new(_app.Services.GetRequiredService<DbContextOptions<WorkflowDbContext>>(),
            SystemSeederTenantContext.Instance);

    /// <summary>
    /// Builds a one-off profile for isolation tests. Only TenantId / Name / RolePrefix are read by the
    /// seeder; the remaining required fields carry inert placeholders.
    /// </summary>
    private static TenantSeedProfile MakeThrowawayProfile(Guid tenantId, string rolePrefix) => new()
    {
        TenantId = tenantId,
        Code = $"throwaway-{tenantId:N}",
        Name = "Throwaway tenant",
        RolePrefix = rolePrefix,
        UserCodePrefix = "TW-",
        LeadEmailPrefix = "tw.lead.",
        DevEmailPrefix = "tw.dev.",
        FillEmailPrefix = "tw",
        EmailDomain = "@throwaway.test",
        UserCount = 0,
        MaxLevel = 1,
        SeedVersion = "test",
    };
}
