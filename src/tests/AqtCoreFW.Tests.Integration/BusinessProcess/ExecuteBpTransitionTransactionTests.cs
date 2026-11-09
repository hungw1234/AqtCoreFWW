using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqtCoreFW.Tests.Integration.Infrastructure;
using AqtCoreFW.Tests.Integration.Workflow;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Tests.Integration.BusinessProcess;

/// <summary>
/// Integration tests proving the cross-module distributed-transaction elimination on the
/// ExecuteBusinessProcessTransition path. The handler opens an <c>ICrossModuleUnitOfWork</c>
/// and runs the transition inside it: WorkflowDbContext writes PLUS eager Identity permission
/// reads (via the ambient Dapper connection) — a write-and-read pattern across what used to be
/// two physical connections. Before the fix this promoted to a distributed transaction and 500'd
/// on .NET 10 (ImplicitDistributedTransactions=false) where MSDTC is unavailable.
///
/// These tests deliberately use NO transition form: the transition alone already drives the
/// multi-connection scenario (Workflow write + Identity Dapper reads under one UoW), which is the
/// DTC-relevant path. Form+workflow atomicity on the BP-start path is covered by
/// <see cref="StartBusinessProcessRunTransactionTests"/>.
///
/// Invariant: No test sets TransactionManager.ImplicitDistributedTransactions = true.
/// </summary>
[Collection("Integration")]
public class ExecuteBpTransitionTransactionTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string BaseUrl = "/api/v1/business-processes";
    private const string WorkflowBaseUrl = "/api/v1/workflow/definitions";

    /// <summary>
    /// Happy path: execute a transition on a BP-Run. The handler runs WorkflowDbContext writes +
    /// eager Identity reads under one shared local SQL transaction. Asserts 200 — pre-fix this 500'd
    /// with "Implicit distributed transactions have not been enabled".
    /// </summary>
    [Fact]
    public async Task ExecuteBusinessProcessTransition_Returns200_NoPromotion()
    {
        var (adminClient, bpId) = await SeedActivatedBusinessProcessAsync();

        var instanceId = await StartRunAsync(adminClient, bpId);

        var transitionResp = await adminClient.PostAsJsonAsync(
            $"{BaseUrl}/runs/{instanceId}/transitions",
            new { ActionName = "Review", Comment = (string?)null });

        var transitionRaw = await transitionResp.Content.ReadAsStringAsync();
        transitionResp.StatusCode.Should().Be(HttpStatusCode.OK,
            $"transition should succeed under the shared UoW (DTC bug would 500). Body: {transitionRaw}");

        var transitionBody = JsonSerializer.Deserialize<JsonElement>(transitionRaw);
        transitionBody.GetProperty("data").TryGetProperty("newStateInternalId", out _)
            .Should().BeTrue("a successful transition returns the new state");
    }

    /// <summary>
    /// Regression guard for enlist-on-first-touch: the transition path's FIRST WorkflowDbContext
    /// touch is a READ (GetTenantForInstanceAsync / instance load) that precedes any write. If the
    /// context were enlisted only on writes, that read would open a separate connection and the
    /// following write would promote to DTC. Asserts no 500 "Implicit distributed transactions".
    /// </summary>
    [Fact]
    public async Task Transition_WorkflowReadThenWrite_NoPromotion()
    {
        var (adminClient, bpId) = await SeedActivatedBusinessProcessAsync();

        var instanceId = await StartRunAsync(adminClient, bpId);

        var transitionResp = await adminClient.PostAsJsonAsync(
            $"{BaseUrl}/runs/{instanceId}/transitions",
            new { ActionName = "Review", Comment = (string?)null });

        transitionResp.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError,
            "transition must not 500 with a distributed-transaction error; the read-then-write " +
            "path must enlist WorkflowDbContext on first touch (read) so the write shares the connection");
    }

    /// <summary>
    /// Advancing a run must complete the task(s) of the state being left, not accumulate them.
    /// Before the source-task-completion fix, the Draft-state task stayed Open after the Draft→UnderReview
    /// transition, so the same run surfaced twice in the inbox (once as the stale Draft task, once as the
    /// new UnderReview task). Without the fix this asserts 2 Open / 0 Done; with it, 1 Open / 1 Done.
    /// </summary>
    [Fact]
    public async Task ExecuteBusinessProcessTransition_CompletesSourceStateTask_LeavesSingleOpenTask()
    {
        var (adminClient, bpId) = await SeedActivatedBusinessProcessAsync();
        var instanceId = await StartRunAsync(adminClient, bpId);

        // Run starts at Draft with exactly one Open first-task. Advance Draft→UnderReview ("Review").
        var transitionResp = await adminClient.PostAsJsonAsync(
            $"{BaseUrl}/runs/{instanceId}/transitions",
            new { ActionName = "Review", Comment = (string?)null });
        var raw = await transitionResp.Content.ReadAsStringAsync();
        transitionResp.StatusCode.Should().Be(HttpStatusCode.OK, $"transition must succeed. Body: {raw}");

        // Ground truth via .IgnoreQueryFilters().AsNoTracking() (4-C-A model-cache defense).
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var instance = await ctx.WorkflowInstances
            .IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(i => i.PublicId == instanceId);

        // Exclude branch-child tasks — those are owned by the parallel-branch lifecycle.
        var tasks = await ctx.WorkflowTasks
            .IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.WorkflowInstanceId == instance.Id && t.BranchChildStatusId == null)
            .ToListAsync();

        tasks.Where(t => t.Status == WorkflowTaskStatus.Open).Should().HaveCount(1,
            "advancing the run must leave exactly one Open task (the new UnderReview state's), " +
            "not also keep the Draft-state task Open");

        var completed = tasks.Where(t => t.Status == WorkflowTaskStatus.Done).ToList();
        completed.Should().HaveCount(1, "the source-state (Draft) task must be completed by the transition");
        completed[0].CompletedBy.Should().NotBeNull("a completed source task records who advanced the run");
        completed[0].CompletedAt.Should().NotBeNull("a completed source task records when it was completed");
    }

    /// <summary>
    /// Success-path coverage for a BP-run transition whose target is a parallel-branch fork-entry state:
    /// the fork (branch status + one child task per branch child) is staged and committed under the
    /// cross-module unit of work, the run advances to the branching state, and the path does not promote
    /// to DTC (no 500). The general BP transition suite excludes branch tasks; this is the fork-aware
    /// companion. NOTE: it asserts the commit/success path only — true rollback atomicity of the fork
    /// (fork artifacts absent if a later write in the same UoW fails) would need fault injection and is an
    /// untested companion gap, mirroring how form+workflow start-path atomicity is scoped separately.
    /// </summary>
    [Fact]
    public async Task ExecuteBusinessProcessTransition_IntoBranchingState_StagesForkUnderCrossModuleUow()
    {
        var (adminClient, bpId, _) = await SeedActivatedBpWithParallelBranchAsync();
        var instanceId = await StartRunAsync(adminClient, bpId);

        // Submit (Draft→Submitted) is the fork-entry. The handler runs it under ICrossModuleUnitOfWork.
        var transitionResp = await adminClient.PostAsJsonAsync(
            $"{BaseUrl}/runs/{instanceId}/transitions",
            new { ActionName = "Submit", Comment = (string?)null });
        var raw = await transitionResp.Content.ReadAsStringAsync();
        transitionResp.StatusCode.Should().Be(HttpStatusCode.OK,
            $"a BP transition into a fork-entry state must succeed and stage the fork under the shared UoW. Body: {raw}");

        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var instance = await ctx.WorkflowInstances.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(i => i.PublicId == instanceId);

        // The fork branch status committed atomically with the BP transition (uow.CommitAsync).
        var branch = await ctx.InstanceBranchStatuses.IgnoreQueryFilters().AsNoTracking()
            .Include(s => s.ChildStatuses).FirstOrDefaultAsync(s => s.InstanceId == instance.Id);
        branch.Should().NotBeNull("the fork-entry must persist an InstanceBranchStatus inside the cross-module tx");
        branch!.Status.Should().Be(WorkflowInstanceBranchStatus.Statuses.InProgress,
            "a freshly-forked branch is InProgress until its children resolve");
        branch.ChildStatuses.Should().HaveCount(2, "the AND fork stages one status per branch child");

        // Both child tasks were staged + committed (claimable pool work items), proving the fan-out
        // enlisted in the BP UoW rather than being lost on a separate connection.
        var childTasks = await ctx.WorkflowTasks.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.WorkflowInstanceId == instance.Id && t.BranchChildStatusId != null)
            .ToListAsync();
        childTasks.Should().HaveCount(2, "each branch child gets a claimable task, committed with the transition");

        // The instance advanced to the branching state (the fork-entry target), scoped to this run's def.
        var branchingStateId = await ctx.Set<WorkflowState>().AsNoTracking()
            .Where(s => s.DefinitionId == instance.DefinitionId && s.Name == "Submitted")
            .Select(s => s.Id).FirstAsync();
        instance.CurrentStateId.Should().Be(branchingStateId,
            "the BP transition advanced the run into the branching state");
    }

    // ----- HELPERS -----

    /// <summary>
    /// Creates a parallel-branch workflow (Draft→Submitted[branching]→fork{UnderReview,Assigned},
    /// continuation Returned→Closed) via the real authoring endpoints, pins it to a new BP, and activates
    /// the BP (which runs branch-aware graph validation). Reuses the proven parallel-branch graph payloads.
    /// Returns an authenticated client + the BP Guid PublicId + the workflow def Guid PublicId.
    /// </summary>
    private async Task<(HttpClient AdminClient, Guid BpId, Guid WorkflowDefId)> SeedActivatedBpWithParallelBranchAsync()
    {
        var tenantId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        var adminClient = CreateAuthenticatedClient(
            userId: userId.ToString(), roles: ["SystemAdmin"], tenantId: tenantId);

        // 1. Create the workflow def shell (states + transitions, no assignments yet).
        var createResp = await adminClient.PostAsJsonAsync(WorkflowBaseUrl, new
        {
            Name = "BP Parallel-Branch Workflow",
            Description = "BP run forking into a parallel branch",
            TenantId = tenantId,
            CreatedBy = userId,
            EntityType = "Generic",
            States = ParallelBranchTestSeed.StatesPayload(),
            Transitions = ParallelBranchTestSeed.TransitionsPayload(withAssignment: false),
        });
        var createRaw = await createResp.Content.ReadAsStringAsync();
        createResp.StatusCode.Should().Be(HttpStatusCode.OK, $"workflow def create must succeed. Body: {createRaw}");
        var workflowDefId = JsonSerializer.Deserialize<JsonElement>(createRaw)
            .GetProperty("data").GetProperty("id").GetGuid();

        // 2. Save the full graph (transitions WITH assignment + the parallel branch with child pools).
        var graphResp = await adminClient.PutAsJsonAsync($"{WorkflowBaseUrl}/{workflowDefId}/graph", new
        {
            States = ParallelBranchTestSeed.StatesPayload(),
            Transitions = ParallelBranchTestSeed.TransitionsPayload(withAssignment: true),
            Branches = ParallelBranchTestSeed.BranchesPayload(),
        });
        var graphRaw = await graphResp.Content.ReadAsStringAsync();
        graphResp.StatusCode.Should().Be(HttpStatusCode.OK, $"save-graph with branches must succeed. Body: {graphRaw}");

        // 3. Create the BP pinning the def.
        var bpCreateResp = await adminClient.PostAsJsonAsync(BaseUrl, new
        {
            Code = $"TEST-BP-FORK-{Guid.NewGuid():N}".ToUpperInvariant(),
            Name = "Test BP Fork Transition",
            TenantId = tenantId,
            WorkflowDefinitionPublicId = workflowDefId,
        });
        var bpCreateRaw = await bpCreateResp.Content.ReadAsStringAsync();
        bpCreateResp.StatusCode.Should().Be(HttpStatusCode.OK, $"BP creation must succeed. Body: {bpCreateRaw}");
        var bpId = JsonSerializer.Deserialize<JsonElement>(bpCreateRaw).GetProperty("data").GetGuid();

        // 4. Activate the BP — runs branch-aware graph validation on the pinned def.
        var activateResp = await adminClient.PostAsync($"{BaseUrl}/{bpId}/activate", null);
        var activateRaw = await activateResp.Content.ReadAsStringAsync();
        activateResp.StatusCode.Should().Be(HttpStatusCode.NoContent, $"BP activation must succeed. Body: {activateRaw}");

        return (adminClient, bpId, workflowDefId);
    }

    /// <summary>
    /// Creates a 3-state workflow (Draft→UnderReview→Approved), seeds the initial-state transition
    /// assignment so instance creation resolves a handler pool, creates a BP pinning it, and
    /// activates the BP. Returns an authenticated client + the BP Guid PublicId.
    /// </summary>
    private async Task<(HttpClient AdminClient, Guid BpId)> SeedActivatedBusinessProcessAsync()
    {
        var tenantId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        var adminClient = CreateAuthenticatedClient(
            userId: userId.ToString(),
            roles: ["SystemAdmin"],
            tenantId: tenantId);

        var workflowDef = await CreateWorkflowDefinitionWithTwoStatesAsync(adminClient, tenantId, userId);
        var workflowDefId = workflowDef.GetProperty("id").GetGuid();

        // Both transitions need an assignment — activate runs graph validation that requires every
        // transition to resolve a handler pool. The helper finds the transition by ActionName.
        await WorkflowSeedHelpers.SeedFirstStateAssignmentByPermissionAsync(
            Factory, workflowDefId, PermCodes.Workflow.Task.Process, actionName: "Review");
        await WorkflowSeedHelpers.SeedFirstStateAssignmentByPermissionAsync(
            Factory, workflowDefId, PermCodes.Workflow.Task.Process, actionName: "Complete");

        // Unique code per run — the BP code is unique-per-tenant and the fixture DB persists across tests.
        var bpCreateResp = await adminClient.PostAsJsonAsync(BaseUrl, new
        {
            Code = $"TEST-BP-{Guid.NewGuid():N}".ToUpperInvariant(),
            Name = "Test BP Transition",
            TenantId = tenantId,
            WorkflowDefinitionPublicId = workflowDefId,
        });
        var bpCreateRaw = await bpCreateResp.Content.ReadAsStringAsync();
        bpCreateResp.StatusCode.Should().Be(HttpStatusCode.OK, $"BP creation must succeed. Body: {bpCreateRaw}");
        var bpId = JsonSerializer.Deserialize<JsonElement>(bpCreateRaw).GetProperty("data").GetGuid();

        var activateResp = await adminClient.PostAsync($"{BaseUrl}/{bpId}/activate", null);
        var activateRaw = await activateResp.Content.ReadAsStringAsync();
        activateResp.StatusCode.Should().Be(HttpStatusCode.NoContent, $"BP activation must succeed. Body: {activateRaw}");

        return (adminClient, bpId);
    }

    /// <summary>Starts a BP run (no intake form) and returns the WorkflowInstance Guid PublicId.</summary>
    private async Task<Guid> StartRunAsync(HttpClient adminClient, Guid bpId)
    {
        var startResp = await adminClient.PostAsJsonAsync($"{BaseUrl}/{bpId}/runs", new
        {
            EntityType = "Generic",
            EntityId = Guid.NewGuid(),
            IntakeFormData = (Dictionary<string, object?>?)null,
            ConsentGiven = false,
        });
        var startRaw = await startResp.Content.ReadAsStringAsync();
        startResp.StatusCode.Should().Be(HttpStatusCode.OK, $"BP run start must succeed. Body: {startRaw}");
        return JsonSerializer.Deserialize<JsonElement>(startRaw)
            .GetProperty("data").GetProperty("workflowInstanceId").GetGuid();
    }

    private async Task<JsonElement> CreateWorkflowDefinitionWithTwoStatesAsync(
        HttpClient client, string tenantId, Guid userId)
    {
        var response = await client.PostAsJsonAsync(WorkflowBaseUrl, new
        {
            Name = "Multi-State Workflow",
            Description = "Workflow with two transitions for transition tests",
            TenantId = tenantId,
            CreatedBy = userId,
            EntityType = "Generic",
            States = new[]
            {
                new
                {
                    Name = "Draft",
                    DisplayNameVi = "Nháp",
                    DisplayNameEn = "Draft",
                    IsInitial = true,
                    IsFinal = false,
                    Color = "#2196F3",
                    SortOrder = 1,
                },
                new
                {
                    Name = "UnderReview",
                    DisplayNameVi = "Đang kiểm tra",
                    DisplayNameEn = "Under Review",
                    IsInitial = false,
                    IsFinal = false,
                    Color = "#FF9800",
                    SortOrder = 2,
                },
                new
                {
                    Name = "Approved",
                    DisplayNameVi = "Đã duyệt",
                    DisplayNameEn = "Approved",
                    IsInitial = false,
                    IsFinal = true,
                    Color = "#4CAF50",
                    SortOrder = 3,
                },
            },
            Transitions = new[]
            {
                new
                {
                    FromStateName = "Draft",
                    ToStateName = "UnderReview",
                    ActionName = "Review",
                    ActionLabelVi = "Xem xét",
                    ActionLabelEn = "Review",
                    ConditionsJson = (string?)null,
                    SortOrder = 1,
                },
                new
                {
                    FromStateName = "UnderReview",
                    ToStateName = "Approved",
                    ActionName = "Complete",
                    ActionLabelVi = "Hoàn thành",
                    ActionLabelEn = "Complete",
                    ConditionsJson = (string?)null,
                    SortOrder = 2,
                },
            },
        });

        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"workflow creation must succeed. Body: {raw}");
        return JsonSerializer.Deserialize<JsonElement>(raw).GetProperty("data");
    }
}
