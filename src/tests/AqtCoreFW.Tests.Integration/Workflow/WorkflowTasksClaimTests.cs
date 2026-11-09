using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Tests.Integration.Infrastructure;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Tests.Integration.Workflow;

/// <summary>
/// Phase 4-B: Claim integration tests — 4 TDD facts (RED→GREEN).
///
/// Facts:
///   F1 Claim happy:      pool-eligible user claims unclaimed pool task → 200.
///                        DB: AssigneeId=user, ClaimedAt!=null, Status=Open, audit Type=Claim.
///   F2 Claim already-claimed:  pool-eligible OTHER user tries to claim an already-claimed task
///                        → 409 Conflict (WF_ALREADY_CLAIMED / GOV_WFL_016).
///                        DB: AssigneeId UNCHANGED (loser did NOT overwrite winner).
///   F3 Concurrent-Claim race:  2 parallel POST /claim calls → exactly 1×200 + 1×409.
///                        DB post-race: AssigneeId ∈ {userA, userB} (not null, not third party).
///                        Audit: exactly 1 Type=Claim row (loser's tx rolled back).
///   F6 Pool-eligibility check: non-pool-member (no matching CandidateIndex row) calls
///                        POST /claim — caller HAS Workflow.Task.Process (passes policy gate)
///                        but task pool requires a DIFFERENT code → handler pool check → 403.
///                        Anti-tautology: not 401 (authenticated), not 422 (no validation error).
///
/// Seeding: minimal def+instance via API → WorkflowTask + WorkflowTaskCandidateIndex via EF.
/// </summary>
[Collection("Integration")]
public class WorkflowTasksClaimTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string DefinitionsUrl = "/api/v1/workflow/definitions";
    private const string InstancesUrl = "/api/v1/workflow/instances";
    private const string TasksUrl = "/api/v1/workflow/tasks";

    // Permission code used for the pool CandidateIndex rows in F1/F2/F3.
    // Callers get this code → they are pool-eligible.
    private const string PoolPermCode = PermCodes.Workflow.Task.Process;

    // A different code used in F6: pool requires this, but caller only has PoolPermCode.
    private const string UnrelatedPermCode = PermCodes.Workflow.Assign.Manage;

    // ─────────────────────────────────────────────────────────────────────
    // Seeding helpers
    // ─────────────────────────────────────────────────────────────────────

    private async Task<(Guid InstancePublicId, long InstanceInternalId, Guid TenantId)> SeedInstanceAsync()
    {
        var tenantId = Guid.NewGuid();
        var tenantIdStr = tenantId.ToString();
        using var adminClient = CreateAuthenticatedClient(roles: ["SystemAdmin"], tenantId: tenantIdStr);

        var defResp = await adminClient.PostAsJsonAsync(DefinitionsUrl, new
        {
            Name = "Claim Test Workflow",
            Description = "Seeded for WorkflowTasksClaimTests",
            TenantId = tenantIdStr,
            CreatedBy = Guid.NewGuid(),
            EntityType = "Generic",
            States = new[]
            {
                new { Name = "Draft", DisplayNameVi = "Nháp", DisplayNameEn = "Draft",
                      IsInitial = true, IsFinal = false, Color = "#808080", SortOrder = 1 },
                new { Name = "Approved", DisplayNameVi = "Đã duyệt", DisplayNameEn = "Approved",
                      IsInitial = false, IsFinal = true, Color = "#52c41a", SortOrder = 2 },
            },
            Transitions = new[]
            {
                new { FromStateName = "Draft", ToStateName = "Approved",
                      ActionName = "Approve", ActionLabelVi = "Phê duyệt", ActionLabelEn = "Approve",
                      ConditionsJson = (string?)null, SortOrder = 1 },
            },
        });
        defResp.StatusCode.Should().Be(HttpStatusCode.OK, "definition seed must succeed");
        var defBody = await defResp.Content.ReadFromJsonAsync<JsonElement>();
        var definitionPublicId = defBody.GetProperty("data").GetProperty("id").GetGuid();

        // Phase 5b empty-pool hard-block fix: stage a ByPermission([Workflow.Task.Process]) assignment
        // on Approve (Draft's outgoing) so initial-state pool resolves non-empty and POST /instances
        // succeeds. P6 S2 DRY refactor (2026-05-22): extracted to WorkflowSeedHelpers.
        await WorkflowSeedHelpers.SeedFirstStateAssignmentByPermissionAsync(
            Factory, definitionPublicId, PoolPermCode);

        var instResp = await adminClient.PostAsJsonAsync(InstancesUrl, new
        {
            EntityType = "Generic",
            EntityId = Guid.NewGuid(),
            DefinitionId = definitionPublicId,
        });
        instResp.StatusCode.Should().Be(HttpStatusCode.OK, "instance seed must succeed");
        var instBody = await instResp.Content.ReadFromJsonAsync<JsonElement>();
        var instancePublicId = instBody.GetProperty("data").GetProperty("id").GetGuid();

        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var instance = await ctx.WorkflowInstances.AsNoTracking()
            .FirstAsync(i => i.PublicId == instancePublicId);

        return (instancePublicId, instance.Id, tenantId);
    }

    /// <summary>
    /// Inserts an unclaimed pool WorkflowTask directly into DB. Returns task long PK.
    /// </summary>
    private async Task<long> SeedPoolTaskAsync(long instanceId, Guid tenantId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var task = WorkflowTask.Create(
            workflowInstanceId: instanceId,
            title: "Claim Test Pool Task",
            description: null,
            assigneeId: null,   // unclaimed pool task
            priority: WorkflowTaskPriority.Normal,
            dueDate: null,
            tenantId: tenantId,
            createdBy: Guid.NewGuid());

        ctx.WorkflowTasks.Add(task);
        await ctx.SaveChangesAsync();
        return task.Id;
    }

    /// <summary>
    /// Seeds a WorkflowTaskCandidateIndex row for the given task, allowing the
    /// <paramref name="principalRefId"/> (permission code) to claim it.
    /// </summary>
    private async Task SeedCandidateIndexAsync(
        long taskId,
        Guid tenantId,
        string principalRefId,
        int clauseId = 1)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var row = WorkflowTaskCandidateIndex.Create(
            workflowTaskId: taskId,
            clauseId: clauseId,
            combineMode: AssignmentCombineMode.Union,
            principalType: CandidatePrincipalType.Permission,
            principalRefId: principalRefId,
            tenantId: tenantId);

        ctx.WorkflowTaskCandidateIndex.Add(row);
        await ctx.SaveChangesAsync();
    }

    // ─────────────────────────────────────────────────────────────────────
    // F1: Claim happy path
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Claim_PoolEligibleUser_Returns200AndPersistsClaimState()
    {
        var (_, instanceInternalId, tenantId) = await SeedInstanceAsync();
        var taskId = await SeedPoolTaskAsync(instanceInternalId, tenantId);
        await SeedCandidateIndexAsync(taskId, tenantId, PoolPermCode);

        var userId = Guid.NewGuid().ToString();
        using var client = CreateClientWithPermissions(
            [PoolPermCode], userId: userId, tenantId: tenantId.ToString());

        var resp = await client.PostAsync($"{TasksUrl}/{taskId}/claim", null);

        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "pool-eligible user claiming an unclaimed task must succeed");

        // Anti-tautology: query DB post-call — assert state persisted correctly.
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var task = await ctx.WorkflowTasks.AsNoTracking()
            .FirstAsync(t => t.Id == taskId);

        task.AssigneeId.Should().Be(Guid.Parse(userId),
            "AssigneeId must be set to the claiming user");
        task.ClaimedAt.Should().NotBeNull("ClaimedAt must be set when task is claimed");
        task.Status.Should().Be(WorkflowTaskStatus.Open,
            "Claim only locks ownership; Status stays Open until Start() is called");

        var auditRow = await ctx.TaskAssignments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.WorkflowTaskId == taskId);

        auditRow.Should().NotBeNull("TaskAssignment audit row must be written on Claim");
        auditRow!.Type.Should().Be(TaskAssignmentType.Claim,
            "audit row must use Type=Claim");
        auditRow.AssignedToId.Should().Be(Guid.Parse(userId),
            "audit row must record the claiming user as AssignedToId");
    }

    // ─────────────────────────────────────────────────────────────────────
    // F2: Claim already-claimed task
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Claim_AlreadyClaimedTask_Returns409AndAssigneeUnchanged()
    {
        var (_, instanceInternalId, tenantId) = await SeedInstanceAsync();
        var taskId = await SeedPoolTaskAsync(instanceInternalId, tenantId);
        await SeedCandidateIndexAsync(taskId, tenantId, PoolPermCode);

        var originalClaimerId = Guid.NewGuid().ToString();

        // First claim — must succeed.
        using var firstClient = CreateClientWithPermissions(
            [PoolPermCode], userId: originalClaimerId, tenantId: tenantId.ToString());
        var firstResp = await firstClient.PostAsync($"{TasksUrl}/{taskId}/claim", null);
        firstResp.StatusCode.Should().Be(HttpStatusCode.OK, "first claim must succeed");

        // Second pool-eligible user attempts to claim the same task.
        var secondUserId = Guid.NewGuid().ToString();
        using var secondClient = CreateClientWithPermissions(
            [PoolPermCode], userId: secondUserId, tenantId: tenantId.ToString());
        var secondResp = await secondClient.PostAsync($"{TasksUrl}/{taskId}/claim", null);

        // Project convention: ConflictError → 409 (ApiControllerBase line 49).
        secondResp.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "claiming an already-claimed task must return 409 Conflict (GOV_WFL_016)");

        var body = await secondResp.Content.ReadAsStringAsync();
        body.Should().Contain(WorkflowErrorCodes.AlreadyClaimed,
            "response body must contain the GOV_WFL_016 error code");

        // Anti-tautology: AssigneeId MUST be unchanged — second user did NOT overwrite winner.
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var task = await ctx.WorkflowTasks.AsNoTracking()
            .FirstAsync(t => t.Id == taskId);

        task.AssigneeId.Should().Be(Guid.Parse(originalClaimerId),
            "AssigneeId must remain the original claimant — loser must NOT overwrite winner");
    }

    // ─────────────────────────────────────────────────────────────────────
    // F3: Concurrent Claim race
    // ─────────────────────────────────────────────────────────────────────
    //
    // ADVISORY (code-review 4-B H1): Task.WhenAll scheduling is non-deterministic. The 2nd
    // caller may exercise EITHER the EF DbUpdateConcurrencyException path (UpdateWithClaimGuardAsync
    // catch → GOV_WFL_016) OR the sequential domain InvalidOperationException path (task.Claim()
    // assert AssigneeId==null → caught at handler → GOV_WFL_016). Both produce identical 409+code,
    // so the USER-FACING contract is proven, but the EF rowversion mechanism itself is NOT
    // deterministically exercised here. F2 pins the sequential path; the deterministic EF path
    // test belongs in 4-C via ISaveChangesInterceptor forcing DbUpdateConcurrencyException on
    // SaveChangesAsync (Workflow.Infrastructure.Tests scope). Carry-forward acknowledged.

    [Fact]
    public async Task Claim_ConcurrentRace_ExactlyOneSucceedsAndAuditHasOneClaimRow()
    {
        var (_, instanceInternalId, tenantId) = await SeedInstanceAsync();
        var taskId = await SeedPoolTaskAsync(instanceInternalId, tenantId);
        await SeedCandidateIndexAsync(taskId, tenantId, PoolPermCode);

        var userAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();

        // Both users are pool-eligible (single CandidateIndex row with PoolPermCode covers both
        // since both callers hold that permission code).
        using var clientA = CreateClientWithPermissions(
            [PoolPermCode], userId: userAId.ToString(), tenantId: tenantId.ToString());
        using var clientB = CreateClientWithPermissions(
            [PoolPermCode], userId: userBId.ToString(), tenantId: tenantId.ToString());

        // Fire both simultaneously — Testcontainers SQL Server provides real DB-level concurrency.
        var (respA, respB) = await Task.WhenAll(
            clientA.PostAsync($"{TasksUrl}/{taskId}/claim", null),
            clientB.PostAsync($"{TasksUrl}/{taskId}/claim", null))
            .ContinueWith(t => (t.Result[0], t.Result[1]));

        var statusA = (int)respA.StatusCode;
        var statusB = (int)respB.StatusCode;

        // Exactly one 200, one 409.
        new[] { statusA, statusB }.Should().Contain(200,
            "exactly one concurrent claimant must succeed");
        new[] { statusA, statusB }.Should().Contain(409,
            "exactly one concurrent claimant must lose with 409 Conflict");

        // Loser must have error code GOV_WFL_016.
        var loserResp = statusA == 409 ? respA : respB;
        var loserBody = await loserResp.Content.ReadAsStringAsync();
        loserBody.Should().Contain(WorkflowErrorCodes.AlreadyClaimed,
            "loser response body must contain GOV_WFL_016");

        // Anti-tautology DB assertions post-race.
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var task = await ctx.WorkflowTasks.AsNoTracking()
            .FirstAsync(t => t.Id == taskId);

        task.AssigneeId.Should().NotBeNull(
            "task must be assigned to one of the two racing users");
        new[] { userAId, userBId }.Should().Contain(task.AssigneeId!.Value,
            "AssigneeId must be one of the two racing users — not null, not a third party");
        task.Status.Should().Be(WorkflowTaskStatus.Open,
            "Claim does not change status to InProgress");
        task.ClaimedAt.Should().NotBeNull("ClaimedAt must be set by the winner");

        // Exactly ONE audit row of Type=Claim (loser's tx rolled back atomically).
        var claimAuditCount = await ctx.TaskAssignments.AsNoTracking()
            .CountAsync(a => a.WorkflowTaskId == taskId && a.Type == TaskAssignmentType.Claim);

        claimAuditCount.Should().Be(1,
            "exactly one Claim audit row must exist — loser's SaveChanges rolled back atomically");
    }

    // ─────────────────────────────────────────────────────────────────────
    // F6: Pool-eligibility check — non-pool-member → 403
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Claim_NonPoolMember_Returns403EvenWithTaskProcessPermission()
    {
        var (_, instanceInternalId, tenantId) = await SeedInstanceAsync();
        var taskId = await SeedPoolTaskAsync(instanceInternalId, tenantId);

        // CandidateIndex row requires UnrelatedPermCode — caller does NOT hold this code.
        await SeedCandidateIndexAsync(taskId, tenantId, UnrelatedPermCode);

        // Caller has PoolPermCode = passes controller [Authorize(Policy = Workflow.Task.Process)]
        // but does NOT have UnrelatedPermCode — pool-eligibility check must fire and deny.
        using var client = CreateClientWithPermissions(
            [PoolPermCode], tenantId: tenantId.ToString());

        // Anti-tautology baseline: unauthenticated → 401 (proves route exists and 403 below
        // comes from policy/pool check, not a global-401 or misconfigured filter).
        using var anonClient = Factory.CreateClient();
        var anonResp = await anonClient.PostAsync($"{TasksUrl}/{taskId}/claim", null);
        anonResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "anonymous must get 401 — proves route exists; authenticated 403 below is pool check");

        var resp = await client.PostAsync($"{TasksUrl}/{taskId}/claim", null);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "caller with Task.Process permission but NOT in task pool must get 403 " +
            "(handler pool-eligibility check fires, not the coarse policy gate)");
    }
}
