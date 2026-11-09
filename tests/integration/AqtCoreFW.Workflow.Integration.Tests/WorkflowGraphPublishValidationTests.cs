using System.Net;
using System.Net.Http.Json;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Workflow.Integration.Tests;

/// <summary>
/// Phase 5c design-time validation integration tests.
///
/// I5: PUT /definitions/{id}/graph with a dead-end state → HTTP 422, R1 violation, definition unchanged.
/// I6: POST /definitions/{id}/activate with unknown ByPermission code → HTTP 422, R3 violation, IsActive stays false.
/// I7: POST /definitions/{id}/activate with a ByOrgUnit clause → HTTP 204, definition activated (all clause modes supported post-RT2-15 Phase 04).
///
/// Auth: SystemAdmin role bypasses PermissionRequirement for Workflow.Def.Manage.
/// 4-C-A INVARIANT: post-action DB reads use .IgnoreQueryFilters().AsNoTracking().
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class WorkflowGraphPublishValidationTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    public WorkflowGraphPublishValidationTests(WebAppFixture app) => _app = app;

    // ── I5: Save graph with dead-end state → R1 fails, 422, definition not changed ──

    [Fact]
    public async Task I5_SaveGraph_WithDeadEndState_Returns422_AndDefinitionUnchanged()
    {
        // Arrange: seed a definition with an initial state so it exists.
        var publicId = await SeedEmptyDefinitionAsync("i5");
        var client = _app.CreateAuthenticatedClient(ActorId, TenantId, "SystemAdmin");

        // Graph: StateA (initial) and StateB (non-final) with NO human outgoing from StateB.
        // StateA → StateB is only a recall transition → StateB is a dead-end for human transitions.
        // Also: StateA has only a recall transition outgoing → dead-end too.
        var body = new
        {
            states = new object[]
            {
                new { name = "StateA", displayNameVi = "A", displayNameEn = "A", isInitial = true, isFinal = false, color = "#FFF", sortOrder = 1 },
                new { name = "StateB", displayNameVi = "B", displayNameEn = "B", isInitial = false, isFinal = false, color = "#FFF", sortOrder = 2 },
            },
            transitions = new object[]
            {
                // Only recall transition → no human outgoing for StateA or StateB
                new { fromStateName = "StateA", toStateName = "StateB", actionName = "RecallOnly",
                    actionLabelVi = "Thu hồi", actionLabelEn = "Recall", conditionsJson = (string?)null, sortOrder = 1, isRecall = true },
            },
        };

        // Act
        var response = await client.PutAsJsonAsync($"/api/v1/workflow/definitions/{publicId}/graph", body);

        // Assert: HTTP 422 (ValidationError → GOV_WFL_019)
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "dead-end state violates R1 → should return 422");

        // 4-C-A: post-action DB read via IgnoreQueryFilters + AsNoTracking
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var saved = await db.WorkflowDefinitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(d => d.States)
            .Include(d => d.Transitions)
            .AsSplitQuery()
            .FirstAsync(d => d.PublicId == publicId);

        // Definition state unchanged: still has 0 states (empty graph from seed).
        saved.States.Should().BeEmpty(
            "R1 validation failed before ReplaceGraphAsync — definition must be unchanged");
    }

    // ── I6: Activate with unknown ByPermission code → R3 fails, 422, IsActive=false ──

    [Fact]
    public async Task I6_Activate_WithUnknownPermissionCode_Returns422_AndDefinitionNotActivated()
    {
        // Arrange: seed a 2-state graph with a transition assigned a bogus permission code.
        var publicId = await SeedDefinitionWithBogusPermissionCodeAsync("i6");
        var client = _app.CreateAuthenticatedClient(ActorId, TenantId, "SystemAdmin");

        // Act
        var response = await client.PostAsync($"/api/v1/workflow/definitions/{publicId}/activate", null);

        // Assert: HTTP 422
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "unknown permission code violates R3 → should return 422");

        // 4-C-A: verify IsActive is still false
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var def = await db.WorkflowDefinitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(d => d.PublicId == publicId);

        def.IsActive.Should().BeFalse(
            "R3 validation failed — definition must not be activated");
    }

    // ── I8: Activate with mixed-case (valid) PermCode → 200, canonical uppercase persisted ──
    // P6-DEFER-7 closure: end-to-end DB-roundtrip proof that R3 canonical-at-write
    // (validator's SetCanonicalRefsJson path) actually persists through the activation
    // SaveChanges. I6 proves rejection of unknown codes; I8 proves the happy-path
    // normalization writes back uppercase before commit.

    [Fact]
    public async Task I8_Activate_WithMixedCasePermissionCode_Returns200_AndPersistsCanonicalUppercase()
    {
        // Arrange: seed a 2-state graph with a VALID PermCode in lowercase form.
        // R3 case-insensitive HashSet match passes; validator must rewrite RefsJson
        // to the canonical uppercase form before SaveChanges fires on the activation path.
        var publicId = await SeedDefinitionWithMixedCasePermissionCodeAsync("i8");
        var client = _app.CreateAuthenticatedClient(ActorId, TenantId, "SystemAdmin");

        // Act
        var response = await client.PostAsync($"/api/v1/workflow/definitions/{publicId}/activate", null);

        // Assert: activation succeeds — valid PermCode resolves under R3 case-insensitive lookup.
        // Activate endpoint returns 204 NoContent on success (no response body).
        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "valid PermCode (case-insensitive R3) → activation should succeed (204)");

        // 4-C-A: post-action DB read via IgnoreQueryFilters + AsNoTracking.
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var def = await db.WorkflowDefinitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(d => d.Transitions)
            .FirstAsync(d => d.PublicId == publicId);

        def.IsActive.Should().BeTrue(
            "R3 passed → ToggleDefinitionActive must transition IsActive to true");

        // DB-roundtrip canonical persistence proof: read back the assignment clause
        // and assert RefsJson is the canonical uppercase form, not the seeded lowercase.
        // This is the bytes-on-disk evidence that SetCanonicalRefsJson + SaveChanges fired.
        var transitionId = def.Transitions.Single().Id;
        var clause = await db.WorkflowTransitionAssignments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(a => a.Clauses)
            .Where(a => a.WorkflowTransitionId == transitionId)
            .SelectMany(a => a.Clauses)
            .FirstAsync();

        clause.RefsJson.Should().Be("""["WORKFLOW.TASK.PROCESS"]""",
            "canonical-at-write: validator MUST uppercase the seeded lowercase code " +
            "and persist via SaveChanges on the happy R3 path (Option A — P6-DEFER-7 closure)");
    }

    // ── I7: Activate with a ByOrgUnit clause → R4 passes, 204, IsActive=true ──
    // RT2-15 lifted every deferred clause mode: PreviousHandler (Phase 01), ByGroup (Phase 02),
    // ByRole (Phase 03 B1), ByOrgUnit (Phase 04, 2026-05-26). The validator's NotSupported set is
    // now empty, so a structurally-valid ByOrgUnit graph activates successfully.
    // Flipped 2026-05-26 (RT2-15 Phase 04-FU): was asserting 422/R4-NotSupported against ByOrgUnit
    // — that expectation went stale the moment Phase 04 (68287c52) shipped ByOrgUnit support.
    // Now locks the positive activate path for the last-lifted mode (no unit-level activate coverage).

    [Fact]
    public async Task I7_Activate_WithByOrgUnitClause_Returns204_AndDefinitionActivated()
    {
        // Arrange: seed a structurally-valid 2-state graph whose Submit transition carries a ByOrgUnit clause.
        var publicId = await SeedDefinitionWithByOrgUnitClauseAsync("i7");
        var client = _app.CreateAuthenticatedClient(ActorId, TenantId, "SystemAdmin");

        // Act
        var response = await client.PostAsync($"/api/v1/workflow/definitions/{publicId}/activate", null);

        // Assert: HTTP 204 — ByOrgUnit is a supported clause mode post-Phase 04; R4 raises no violation.
        response.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "ByOrgUnit clause mode is supported post-RT2-15 Phase 04 → activate must succeed");

        // Definition must now be activated (IsActive flips true on the happy validate path).
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var def = await db.WorkflowDefinitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(d => d.PublicId == publicId);

        def.IsActive.Should().BeTrue(
            "all clause-mode validation passed — definition must be activated");
    }

    // ── Seeding helpers ──────────────────────────────────────────────────────

    private async Task<Guid> SeedEmptyDefinitionAsync(string slug)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var def = WorkflowDefinition.Create(gen,
            name: $"5c-{slug}-{Guid.NewGuid():N}",
            description: "Phase 5c integration test",
            tenantId: TenantId,
            createdBy: ActorId,
            definitionKey: $"5c-{slug}-{Guid.NewGuid():N}",
            entityType: "Generic");

        db.WorkflowDefinitions.Add(def);
        await db.SaveChangesAsync();
        return def.PublicId;
    }

    /// <summary>
    /// Seeds: definition + Draft (initial) → Approved (final) with a "Submit" transition
    /// assigned a ByPermission clause containing "COMPLETELY.BOGUS.CODE".
    /// This triggers R3 failure on activate.
    /// </summary>
    private async Task<Guid> SeedDefinitionWithBogusPermissionCodeAsync(string slug)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var def = WorkflowDefinition.Create(gen,
            name: $"5c-{slug}-{Guid.NewGuid():N}",
            description: "Phase 5c R3 test",
            tenantId: TenantId,
            createdBy: ActorId,
            definitionKey: $"5c-{slug}-{Guid.NewGuid():N}",
            entityType: "Generic");

        db.WorkflowDefinitions.Add(def);
        await db.SaveChangesAsync();

        var draft = WorkflowState.Create(def.Id, "Draft", "Nháp", "Draft", true, false, "#FFF", 1);
        var approved = WorkflowState.Create(def.Id, "Approved", "Đã duyệt", "Approved", false, true, "#4CAF50", 2);
        db.Set<WorkflowState>().AddRange(draft, approved);
        await db.SaveChangesAsync();

        var submit = WorkflowTransition.Create(
            def.Id, draft.Id, approved.Id, "Submit", "Nộp", "Submit", null, 1);
        db.Set<WorkflowTransition>().Add(submit);
        await db.SaveChangesAsync();

        // Assignment with bogus PermCode → R3 will reject.
        var assignment = WorkflowTransitionAssignment.Create(submit.Id, AssignmentCombineMode.Union);
        assignment.AddClause(AssignmentClauseMode.ByPermission, """["COMPLETELY.BOGUS.CODE"]""");
        db.WorkflowTransitionAssignments.Add(assignment);
        await db.SaveChangesAsync();

        return def.PublicId;
    }

    /// <summary>
    /// P6-DEFER-7 seed: definition + Draft → Approved with "Submit" transition
    /// assigned a ByPermission clause containing a VALID PermCode in lowercase
    /// (`"workflow.task.process"`). R3 passes via case-insensitive HashSet lookup;
    /// validator writes back canonical uppercase via SetCanonicalRefsJson + SaveChanges.
    /// </summary>
    private async Task<Guid> SeedDefinitionWithMixedCasePermissionCodeAsync(string slug)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var def = WorkflowDefinition.Create(gen,
            name: $"5c-{slug}-{Guid.NewGuid():N}",
            description: "Phase 5c R3 canonical-at-write roundtrip test (DEFER-7)",
            tenantId: TenantId,
            createdBy: ActorId,
            definitionKey: $"5c-{slug}-{Guid.NewGuid():N}",
            entityType: "Generic");

        db.WorkflowDefinitions.Add(def);
        await db.SaveChangesAsync();

        var draft = WorkflowState.Create(def.Id, "Draft", "Nháp", "Draft", true, false, "#FFF", 1);
        var approved = WorkflowState.Create(def.Id, "Approved", "Đã duyệt", "Approved", false, true, "#4CAF50", 2);
        db.Set<WorkflowState>().AddRange(draft, approved);
        await db.SaveChangesAsync();

        var submit = WorkflowTransition.Create(
            def.Id, draft.Id, approved.Id, "Submit", "Nộp", "Submit", null, 1);
        db.Set<WorkflowTransition>().Add(submit);
        await db.SaveChangesAsync();

        // Assignment with VALID PermCode in LOWERCASE — R3 passes; canonicalization writes uppercase back.
        var assignment = WorkflowTransitionAssignment.Create(submit.Id, AssignmentCombineMode.Union);
        assignment.AddClause(AssignmentClauseMode.ByPermission, """["workflow.task.process"]""");
        db.WorkflowTransitionAssignments.Add(assignment);
        await db.SaveChangesAsync();

        return def.PublicId;
    }

    /// <summary>
    /// Seeds: definition + Draft (initial) → Approved (final) with a "Submit" transition
    /// assigned a ByOrgUnit clause. Post-RT2-15 Phase 04 every clause mode is supported, so the
    /// resulting graph activates cleanly (used by I7 to lock the positive activate path).
    /// </summary>
    private async Task<Guid> SeedDefinitionWithByOrgUnitClauseAsync(string slug)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var def = WorkflowDefinition.Create(gen,
            name: $"5c-{slug}-{Guid.NewGuid():N}",
            description: "RT2-15 Phase 04-FU activate test (ByOrgUnit — supported clause mode)",
            tenantId: TenantId,
            createdBy: ActorId,
            definitionKey: $"5c-{slug}-{Guid.NewGuid():N}",
            entityType: "Generic");

        db.WorkflowDefinitions.Add(def);
        await db.SaveChangesAsync();

        var draft = WorkflowState.Create(def.Id, "Draft", "Nháp", "Draft", true, false, "#FFF", 1);
        var approved = WorkflowState.Create(def.Id, "Approved", "Đã duyệt", "Approved", false, true, "#4CAF50", 2);
        db.Set<WorkflowState>().AddRange(draft, approved);
        await db.SaveChangesAsync();

        var submit = WorkflowTransition.Create(
            def.Id, draft.Id, approved.Id, "Submit", "Nộp", "Submit", null, 1);
        db.Set<WorkflowTransition>().Add(submit);
        await db.SaveChangesAsync();

        // ByOrgUnit clause → supported post-Phase 04; structural-only validation, activates cleanly.
        var assignment = WorkflowTransitionAssignment.Create(submit.Id, AssignmentCombineMode.Union);
        assignment.AddClause(AssignmentClauseMode.ByOrgUnit, """["ORG_TIEP_NHAN"]""");
        db.WorkflowTransitionAssignments.Add(assignment);
        await db.SaveChangesAsync();

        return def.PublicId;
    }
}
