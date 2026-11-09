using System.Net;
using System.Net.Http.Json;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Workflow.Integration.Tests;

/// <summary>
/// E2E Save → Activate happy-path coverage for plan 260523-1241 (Option A handler
/// assignment payload). Closes the test gap behind H1 merge gate established in
/// Phase 03 — these tests run against REAL EF + Testcontainers SQL Server, so they
/// exercise the validator-handler-repo-Activate gate chain end-to-end (5b-C
/// INVARIANT — unit mocks cannot catch EF IDENTITY/FK structural bugs).
///
/// Facts:
///   I_SaveAct_A1 — happy path: 3 states + 2 transitions w/ ByPermission clauses →
///                 200 + DB rows present (states/transitions/assignments/clauses)
///                 → POST /activate → 200 + IsActive=1.
///   I_SaveAct_A2 — R2 block: 1 transition has NO assignment → 422 GOV_WFL_019,
///                 atomicity preserved (states/transitions absent).
///   I_SaveAct_A3 — R3 canonical: ByPermission RefsJson lowercase → 200 + DB row
///                 stores uppercase canonical form.
///   I_SaveAct_A4 — R4 reject: clause Mode=PreviousHandler → 422, atomicity.
///   I_SaveAct_A5 — Q2 replace-all: save again with NEW assignment → old rows
///                 deleted, only new rows persisted.
///
/// Auth: WebAppFixture's TestSystemAdminPermissionBypassHandler grants effective
/// SystemAdmin so PUT /graph (gated on Workflow.Def.Manage) is allowed.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class WorkflowDefinitionSaveGraphWithAssignmentTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    public WorkflowDefinitionSaveGraphWithAssignmentTests(WebAppFixture app)
    {
        _app = app;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Guid> SeedEmptyDefinitionAsync()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var def = WorkflowDefinition.Create(
            gen: gen,
            name: $"plan-260523-1241-savegraph-assignment {Guid.NewGuid():N}",
            description: "Empty-graph definition for assignment-payload integration tests",
            tenantId: TenantId,
            createdBy: ActorId,
            definitionKey: $"sg-asgn-{Guid.NewGuid():N}",
            entityType: "Generic");

        db.WorkflowDefinitions.Add(def);
        await db.SaveChangesAsync();
        return def.PublicId;
    }

    private static object ThreeStateGraph(object[] transitions) => new
    {
        states = new object[]
        {
            new { name = "Draft",    displayNameVi = "Nháp",     displayNameEn = "Draft",     isInitial = true,  isFinal = false, color = "#9E9E9E", sortOrder = 1 },
            new { name = "UnderReview", displayNameVi = "Xem xét",  displayNameEn = "In Review", isInitial = false, isFinal = false, color = "#2196F3", sortOrder = 2 },
            new { name = "Approved", displayNameVi = "Đã duyệt", displayNameEn = "Approved",  isInitial = false, isFinal = true,  color = "#4CAF50", sortOrder = 3 },
        },
        transitions,
    };

    private static object InitiatorAssignment() => new
    {
        combineMode = 0,
        allowAssignorOverride = false,
        clauses = new[] { new { mode = 1, refsJson = (string?)null } },
    };

    private static object ByPermissionAssignment(string permCodesJson) => new
    {
        combineMode = 0,
        allowAssignorOverride = false,
        clauses = new[] { new { mode = 3, refsJson = (string?)permCodesJson } },
    };

    // ── I-SaveAct-A1: happy path ─────────────────────────────────────────────

    [Fact]
    public async Task I_SaveAct_A1_SaveAndActivate_HappyPath_PersistsAllRowsAnd200OnActivate()
    {
        var publicId = await SeedEmptyDefinitionAsync();
        var client = _app.CreateAuthenticatedClient(ActorId, TenantId, "SystemAdmin");

        var body = ThreeStateGraph(new object[]
        {
            new
            {
                fromStateName = "Draft", toStateName = "UnderReview",
                actionName = "Submit", actionLabelVi = "Nộp", actionLabelEn = "Submit",
                conditionsJson = (string?)null, sortOrder = 1, isRecall = false,
                assignment = ByPermissionAssignment("""["WORKFLOW.TASK.PROCESS"]"""),
            },
            new
            {
                fromStateName = "UnderReview", toStateName = "Approved",
                actionName = "Approve", actionLabelVi = "Duyệt", actionLabelEn = "Approve",
                conditionsJson = (string?)null, sortOrder = 2, isRecall = false,
                assignment = ByPermissionAssignment("""["WORKFLOW.DEF.MANAGE"]"""),
            },
        });

        var saveResp = await client.PutAsJsonAsync($"/api/v1/workflow/definitions/{publicId}/graph", body);
        saveResp.StatusCode.Should().Be(HttpStatusCode.OK, "happy path with valid Assignment satisfies R2/R3/R4");

        // Assert assignment + clause rows persisted.
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var saved = await db.WorkflowDefinitions
                .Include(d => d.States)
                .Include(d => d.Transitions)
                .AsSplitQuery().AsNoTracking()
                .FirstAsync(d => d.PublicId == publicId);
            saved.States.Should().HaveCount(3);
            saved.Transitions.Should().HaveCount(2);

            var transitionIds = saved.Transitions.Select(t => t.Id).ToList();
            var assignmentRows = await db.Set<WorkflowTransitionAssignment>()
                .Where(a => transitionIds.Contains(a.WorkflowTransitionId))
                .Include(a => a.Clauses)
                .AsNoTracking()
                .ToListAsync();
            assignmentRows.Should().HaveCount(2, "every non-recall transition must persist one assignment row");
            assignmentRows.SelectMany(a => a.Clauses).Should().HaveCount(2, "one clause per assignment");
        }

        var activateResp = await client.PostAsync($"/api/v1/workflow/definitions/{publicId}/activate", content: null);
        // ToggleDefinitionActiveCommand returns void Result → ApiControllerBase emits 204 NoContent
        // (no echo body). Tight-bound to 204 (not BeOneOf 200|204) so a future change adding an echo
        // body trips the test — M1 fold per code-review 260523-1800.
        activateResp.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "Activate path returns 204 once graph has R2-valid assignments (no longer 422 GOV_WFL_019)");

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var def = await db.WorkflowDefinitions.AsNoTracking().FirstAsync(d => d.PublicId == publicId);
            def.IsActive.Should().BeTrue("Activate must flip IsActive=true on success");
        }
    }

    // ── I-SaveAct-A2: R2 block + atomicity ───────────────────────────────────

    [Fact]
    public async Task I_SaveAct_A2_OneTransitionMissingAssignment_Returns422_NoRowsPersisted()
    {
        var publicId = await SeedEmptyDefinitionAsync();
        var client = _app.CreateAuthenticatedClient(ActorId, TenantId, "SystemAdmin");

        var body = ThreeStateGraph(new object[]
        {
            new
            {
                fromStateName = "Draft", toStateName = "UnderReview",
                actionName = "Submit", actionLabelVi = "Nộp", actionLabelEn = "Submit",
                conditionsJson = (string?)null, sortOrder = 1, isRecall = false,
                assignment = InitiatorAssignment(),
            },
            new
            {
                fromStateName = "UnderReview", toStateName = "Approved",
                actionName = "Approve", actionLabelVi = "Duyệt", actionLabelEn = "Approve",
                conditionsJson = (string?)null, sortOrder = 2, isRecall = false,
                // R2 violation — missing assignment.
            },
        });

        var resp = await client.PutAsJsonAsync($"/api/v1/workflow/definitions/{publicId}/graph", body);
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "R2-strict at Save rejects missing assignment");

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var saved = await db.WorkflowDefinitions
            .Include(d => d.States)
            .Include(d => d.Transitions)
            .AsSplitQuery().AsNoTracking()
            .FirstAsync(d => d.PublicId == publicId);
        saved.States.Should().BeEmpty("atomicity — pre-R2 validator rejects before any persistence");
        saved.Transitions.Should().BeEmpty();
    }

    // ── I-SaveAct-A3: R3 canonical writeback ─────────────────────────────────

    [Fact]
    public async Task I_SaveAct_A3_ByPermissionLowercase_PersistsCanonicalUppercase()
    {
        var publicId = await SeedEmptyDefinitionAsync();
        var client = _app.CreateAuthenticatedClient(ActorId, TenantId, "SystemAdmin");

        var body = ThreeStateGraph(new object[]
        {
            new
            {
                fromStateName = "Draft", toStateName = "UnderReview",
                actionName = "Submit", actionLabelVi = "Nộp", actionLabelEn = "Submit",
                conditionsJson = (string?)null, sortOrder = 1, isRecall = false,
                assignment = ByPermissionAssignment("""["workflow.task.process"]"""),
            },
            new
            {
                fromStateName = "UnderReview", toStateName = "Approved",
                actionName = "Approve", actionLabelVi = "Duyệt", actionLabelEn = "Approve",
                conditionsJson = (string?)null, sortOrder = 2, isRecall = false,
                assignment = InitiatorAssignment(),
            },
        });

        var resp = await client.PutAsJsonAsync($"/api/v1/workflow/definitions/{publicId}/graph", body);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, "R3 canonicalizes lowercase PermCodes pre-persist");

        // Scope query to THIS test's definition — SqlServerCollection fixture is shared across
        // tests, so prior test data leaks if we filter only by clause Mode. Pull transition IDs
        // first, then filter assignments by FK.
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var def = await db.WorkflowDefinitions
            .Include(d => d.Transitions)
            .AsNoTracking()
            .FirstAsync(d => d.PublicId == publicId);
        var transitionIds = def.Transitions.Select(t => t.Id).ToList();

        var clauses = await db.Set<WorkflowTransitionAssignment>()
            .Where(a => transitionIds.Contains(a.WorkflowTransitionId))
            .Include(a => a.Clauses)
            .AsNoTracking()
            .SelectMany(a => a.Clauses)
            .Where(c => c.Mode == Workflow.Domain.Enums.AssignmentClauseMode.ByPermission)
            .ToListAsync();

        clauses.Should().HaveCount(1, "this test's def has exactly one ByPermission clause (on Submit)");
        clauses[0].RefsJson.Should().Be("""["WORKFLOW.TASK.PROCESS"]""",
            "R3 writeback persists Trim+ToUpperInvariant canonical form");
    }

    // ── I-SaveAct-A4: R4 reject + atomicity ──────────────────────────────────
    // RT2-15 Phase 01 + 03 B1 lifted PreviousHandler (mode 2) + ByRole (mode 4) from
    // _notSupportedModes. ByGroup (5) + ByOrgUnit (6) remain unsupported until Phases
    // 02 + 04 ship matching producers. Assertion swapped accordingly to preserve R4
    // reject + transactional atomicity coverage on a still-unsupported mode.

    [Fact]
    public async Task I_SaveAct_A4_ByGroupClause_Returns422_NoRowsPersisted()
    {
        var publicId = await SeedEmptyDefinitionAsync();
        var client = _app.CreateAuthenticatedClient(ActorId, TenantId, "SystemAdmin");

        var body = ThreeStateGraph(new object[]
        {
            new
            {
                fromStateName = "Draft", toStateName = "UnderReview",
                actionName = "Submit", actionLabelVi = "Nộp", actionLabelEn = "Submit",
                conditionsJson = (string?)null, sortOrder = 1, isRecall = false,
                assignment = new
                {
                    combineMode = 0,
                    allowAssignorOverride = false,
                    clauses = new[] { new { mode = 5, refsJson = """["GROUP_TIEP_NHAN"]""" } }, // ByGroup — NotSupported R4 (Phase 02 will lift)
                },
            },
            new
            {
                fromStateName = "UnderReview", toStateName = "Approved",
                actionName = "Approve", actionLabelVi = "Duyệt", actionLabelEn = "Approve",
                conditionsJson = (string?)null, sortOrder = 2, isRecall = false,
                assignment = InitiatorAssignment(),
            },
        });

        var resp = await client.PutAsJsonAsync($"/api/v1/workflow/definitions/{publicId}/graph", body);
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "R4 rejects ByGroup clause at Save");

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var saved = await db.WorkflowDefinitions
            .Include(d => d.States)
            .Include(d => d.Transitions)
            .AsSplitQuery().AsNoTracking()
            .FirstAsync(d => d.PublicId == publicId);
        saved.States.Should().BeEmpty();
        saved.Transitions.Should().BeEmpty();
    }

    // ── I-SaveAct-A6: T3-WF-RECALL-ASSIGNMENT-Q Option A — silent-strip DB proof ─

    [Fact]
    public async Task I_SaveAct_A6_RecallTransitionWithAssignment_PersistsNoAssignmentRow()
    {
        var publicId = await SeedEmptyDefinitionAsync();
        var client = _app.CreateAuthenticatedClient(ActorId, TenantId, "SystemAdmin");

        // 3-state graph; Recall transition (Approved→Draft) sends a NON-NULL assignment
        // payload — handler MUST silent-strip; DB MUST show zero assignment rows for
        // the recall transition while Submit's assignment persists normally.
        var body = ThreeStateGraph(new object[]
        {
            new
            {
                fromStateName = "Draft", toStateName = "UnderReview",
                actionName = "Submit", actionLabelVi = "Nộp", actionLabelEn = "Submit",
                conditionsJson = (string?)null, sortOrder = 1, isRecall = false,
                assignment = InitiatorAssignment(),
            },
            new
            {
                fromStateName = "UnderReview", toStateName = "Approved",
                actionName = "Approve", actionLabelVi = "Duyệt", actionLabelEn = "Approve",
                conditionsJson = (string?)null, sortOrder = 2, isRecall = false,
                assignment = InitiatorAssignment(),
            },
            new
            {
                fromStateName = "Approved", toStateName = "Draft",
                actionName = "RecallToDraft", actionLabelVi = "Thu hồi", actionLabelEn = "Recall",
                conditionsJson = (string?)null, sortOrder = 3, isRecall = true,
                assignment = ByPermissionAssignment("""["WORKFLOW.TASK.PROCESS"]"""), // Will be stripped
            },
        });

        var resp = await client.PutAsJsonAsync($"/api/v1/workflow/definitions/{publicId}/graph", body);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, "silent-strip does not surface as validation error");

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var def = await db.WorkflowDefinitions
            .Include(d => d.Transitions)
            .AsNoTracking()
            .FirstAsync(d => d.PublicId == publicId);

        var recallTransition = def.Transitions.Single(t => t.ActionName == "RecallToDraft");
        var assignmentForRecall = await db.Set<WorkflowTransitionAssignment>()
            .Where(a => a.WorkflowTransitionId == recallTransition.Id)
            .AsNoTracking()
            .ToListAsync();

        assignmentForRecall.Should().BeEmpty(
            "T3-WF-RECALL Option A silent-strip — recall transitions never persist assignment rows");

        // Sanity: non-recall transitions DO persist assignments (regression guard).
        var nonRecallTransitionIds = def.Transitions
            .Where(t => !t.IsRecall)
            .Select(t => t.Id)
            .ToList();
        var nonRecallAssignmentCount = await db.Set<WorkflowTransitionAssignment>()
            .Where(a => nonRecallTransitionIds.Contains(a.WorkflowTransitionId))
            .AsNoTracking()
            .CountAsync();
        nonRecallAssignmentCount.Should().Be(2,
            "non-recall transitions (Submit + Approve) still persist their assignments");
    }

    // ── I-SaveAct-A5: Q2 replace-all semantic ────────────────────────────────

    [Fact]
    public async Task I_SaveAct_A5_ReSaveWithDifferentAssignment_DeletesOldAndPersistsNew()
    {
        var publicId = await SeedEmptyDefinitionAsync();
        var client = _app.CreateAuthenticatedClient(ActorId, TenantId, "SystemAdmin");

        // Save 1: ByPermission ["WORKFLOW.TASK.PROCESS"]
        var body1 = ThreeStateGraph(new object[]
        {
            new
            {
                fromStateName = "Draft", toStateName = "UnderReview",
                actionName = "Submit", actionLabelVi = "Nộp", actionLabelEn = "Submit",
                conditionsJson = (string?)null, sortOrder = 1, isRecall = false,
                assignment = ByPermissionAssignment("""["WORKFLOW.TASK.PROCESS"]"""),
            },
            new
            {
                fromStateName = "UnderReview", toStateName = "Approved",
                actionName = "Approve", actionLabelVi = "Duyệt", actionLabelEn = "Approve",
                conditionsJson = (string?)null, sortOrder = 2, isRecall = false,
                assignment = InitiatorAssignment(),
            },
        });
        (await client.PutAsJsonAsync($"/api/v1/workflow/definitions/{publicId}/graph", body1))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Save 2 (same def): ByPermission ["WORKFLOW.DEF.MANAGE"]
        var body2 = ThreeStateGraph(new object[]
        {
            new
            {
                fromStateName = "Draft", toStateName = "UnderReview",
                actionName = "Submit", actionLabelVi = "Nộp", actionLabelEn = "Submit",
                conditionsJson = (string?)null, sortOrder = 1, isRecall = false,
                assignment = ByPermissionAssignment("""["WORKFLOW.DEF.MANAGE"]"""),
            },
            new
            {
                fromStateName = "UnderReview", toStateName = "Approved",
                actionName = "Approve", actionLabelVi = "Duyệt", actionLabelEn = "Approve",
                conditionsJson = (string?)null, sortOrder = 2, isRecall = false,
                assignment = InitiatorAssignment(),
            },
        });
        (await client.PutAsJsonAsync($"/api/v1/workflow/definitions/{publicId}/graph", body2))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var saved = await db.WorkflowDefinitions
            .Include(d => d.Transitions)
            .AsNoTracking()
            .FirstAsync(d => d.PublicId == publicId);
        var transitionIds = saved.Transitions.Select(t => t.Id).ToList();

        var allClauses = await db.Set<WorkflowTransitionAssignment>()
            .Where(a => transitionIds.Contains(a.WorkflowTransitionId))
            .Include(a => a.Clauses)
            .AsNoTracking()
            .SelectMany(a => a.Clauses)
            .ToListAsync();

        // Old ["WORKFLOW.TASK.PROCESS"] clause must NOT survive — Q2 replace-all DELETE.
        // Exact-match (not Contains) to prevent false-positive against future audit perm-codes
        // sharing the same prefix (e.g. "WORKFLOW.TASK.PROCESSING", "WORKFLOW.DEF.MANAGE_PUBLIC").
        allClauses.Should().NotContain(c => c.RefsJson == """["WORKFLOW.TASK.PROCESS"]""");
        allClauses.Should().Contain(c => c.RefsJson == """["WORKFLOW.DEF.MANAGE"]""",
            "second save must persist the new ByPermission codes");
    }
}
