using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqtCoreFW.Tests.Integration.Infrastructure;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static AqtCoreFW.Tests.Integration.Workflow.ParallelBranchTestSeed;

namespace AqtCoreFW.Tests.Integration.Workflow;

/// <summary>
/// Phase 5 — first end-to-end parallel-branch runtime test through the HTTP surface.
///
/// Proves the full fork lifecycle drives the real <c>ExecuteTransitionCommandHandler</c> path
/// (the handler the parallel-runtime engine wired in P4), seeded via the real authoring save-graph
/// + activate endpoints (so P1/P2/P3 are exercised as a bonus):
///   activate → create instance → Submit (fork fires) → 2 child tasks pooled (candidate-index)
///   → claim → resolve Approved×2 → AND-join completes → continuation task staged → Close → final.
///
/// Negatives assert the engine's fail-CLOSED guards: resolve-before-claim (403), empty child pool
/// (fork refused 422), and exit-via-ordinary-transition while a branch is InProgress (forbidden).
///
/// The save/drive recipe lives in <see cref="ParallelBranchTestSeed"/> (shared with the concurrency suite).
///
/// Graph shape (mirrors the proven inline fan-out factory, plus a final Close):
///   Draft ──Submit──▶ Submitted [branching] ══fork══▶ {ReviewA, ReviewB}
///   Submitted ──Continue──▶ Synthesis [continuation]  (validation-reachability edge; the runtime
///       never executes it — RT1 blocks ordinary exits while forked, and the join reaches Synthesis
///       internally) ── Close ──▶ Closed [final]
/// </summary>
[Collection("Integration")]
public sealed class WorkflowParallelBranchRuntimeTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // Happy path
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Full AND-fork lifecycle: fork stages 2 pooled children, each claimed + resolved by a distinct
    /// pool user, the join completes only after BOTH resolve, the continuation task is staged + claimable,
    /// and the final Close lands the instance in the final state.
    /// </summary>
    [Fact]
    public async Task AndFork_ClaimResolveBoth_JoinCompletes_ContinuationClaimable_ThenClose()
    {
        var seeded = await SeedActivatedParallelDefinitionAsync(Factory);

        var tenantId = seeded.TenantId;
        var instancePublicId = await CreateInstanceAsync(seeded.AdminClient, seeded.DefinitionPublicId);

        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        using var clientA = CreateClientWithPermissions([PoolPerm], userId: userA.ToString(), tenantId: tenantId.ToString());
        using var clientB = CreateClientWithPermissions([PoolPerm], userId: userB.ToString(), tenantId: tenantId.ToString());

        // ── Submit fires the fork ──────────────────────────────────────────────────────────────────
        var submitResp = await clientA.PostAsJsonAsync(
            $"{InstancesUrl}/{instancePublicId}/transitions",
            new { TransitionId = seeded.SubmitTransitionId, Comment = (string?)null });
        var submitRaw = await submitResp.Content.ReadAsStringAsync();
        submitResp.StatusCode.Should().Be(HttpStatusCode.OK,
            $"Submit into the branching state must fire the fork. Body: {submitRaw}");

        // ── Assert: branch InProgress, 2 pooled child tasks, candidate-index rows (RT8) ─────────────
        var (instanceInternalId, branchingStateId, synthesisStateId) =
            await ResolveInstanceStateIdsAsync(instancePublicId, seeded);

        List<(long TaskId, long ChildStatusId)> children;
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

            var branchStatus = await ctx.InstanceBranchStatuses
                .IgnoreQueryFilters().AsNoTracking().Include(s => s.ChildStatuses)
                .FirstOrDefaultAsync(s => s.InstanceId == instanceInternalId);
            branchStatus.Should().NotBeNull("the fork must persist an InstanceBranchStatus");
            branchStatus!.Status.Should().Be("InProgress", "the branch is mid-flight after the fork");
            branchStatus.ChildStatuses.Should().HaveCount(2, "the AND fork stages one child status per child");

            var childTasks = await ctx.WorkflowTasks
                .IgnoreQueryFilters().AsNoTracking()
                .Where(t => t.WorkflowInstanceId == instanceInternalId && t.BranchChildStatusId != null)
                .ToListAsync();
            childTasks.Should().HaveCount(2, "one pooled approval task per branch child");
            childTasks.Should().OnlyContain(t => t.AssigneeId == null,
                "child tasks start unclaimed (pooled) — no default assignee");

            var taskIds = childTasks.Select(t => t.Id).ToList();
            var candidateRows = await ctx.WorkflowTaskCandidateIndex
                .IgnoreQueryFilters().AsNoTracking()
                .Where(c => taskIds.Contains(c.WorkflowTaskId))
                .ToListAsync();
            candidateRows.Should().NotBeEmpty(
                "RT8: a ByPermission child pool must write candidate-index rows so the task is claimable");
            candidateRows.Select(c => c.WorkflowTaskId).Distinct().Should().HaveCount(2,
                "every child task must have at least one candidate-index row");

            // Pair each child task with its child status via the persisted FK — the engine must link the
            // task to its branch child status (BranchChildStatusId) so claim (by task id) and resolve
            // (by child status id) refer to the same row. Order is irrelevant for an AND fork (both must
            // resolve), so children[0]/[1] need no deterministic sort.
            childTasks.Should().OnlyContain(t => t.BranchChildStatusId != null && t.BranchChildStatusId != 0,
                "each branch child task must carry the real (non-zero) child-status FK");
            children = childTasks
                .Select(t => (TaskId: t.Id, ChildStatusId: t.BranchChildStatusId!.Value))
                .ToList();
        }

        // ── Claim + resolve child #1 → join NOT complete yet (AND needs both) ───────────────────────
        await ClaimAsync(clientA, children[0].TaskId);
        await ResolveBranchChildAsync(clientA, instancePublicId, children[0].ChildStatusId, BranchResolutionType.Approved);

        await AssertCurrentStateAsync(instancePublicId, branchingStateId,
            "after only one of two AND children resolves, the instance stays in the branching state");

        // ── Claim + resolve child #2 → AND-join completes → advance to continuation ──────────────────
        await ClaimAsync(clientB, children[1].TaskId);
        await ResolveBranchChildAsync(clientB, instancePublicId, children[1].ChildStatusId, BranchResolutionType.Approved);

        await AssertCurrentStateAsync(instancePublicId, synthesisStateId,
            "once both AND children resolve, the join advances the instance to the continuation state");

        // ── RT3: the join stages a claimable continuation task (not just a state flip) ───────────────
        long continuationTaskId;
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

            var openContinuation = await ctx.WorkflowTasks
                .IgnoreQueryFilters().AsNoTracking()
                .Where(t => t.WorkflowInstanceId == instanceInternalId
                            && t.BranchChildStatusId == null
                            && t.Status == WorkflowTaskStatus.Open)
                .ToListAsync();
            openContinuation.Should().ContainSingle(
                "RT3: the join must stage exactly one open continuation task so work can proceed");
            continuationTaskId = openContinuation[0].Id;

            var candidateRows = await ctx.WorkflowTaskCandidateIndex
                .IgnoreQueryFilters().AsNoTracking()
                .CountAsync(c => c.WorkflowTaskId == continuationTaskId);
            candidateRows.Should().BeGreaterThan(0,
                "the continuation task must be pooled (candidate-index) so a handler can claim it");
        }

        // ── Close → final ──────────────────────────────────────────────────────────────────────────
        var closeResp = await clientA.PostAsJsonAsync(
            $"{InstancesUrl}/{instancePublicId}/transitions",
            new { TransitionId = seeded.CloseTransitionId, Comment = (string?)null });
        var closeRaw = await closeResp.Content.ReadAsStringAsync();
        closeResp.StatusCode.Should().Be(HttpStatusCode.OK,
            $"the continuation→final Close transition must succeed. Body: {closeRaw}");

        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var instance = await ctx.WorkflowInstances
                .IgnoreQueryFilters().AsNoTracking()
                .FirstAsync(i => i.PublicId == instancePublicId);
            var currentState = await ctx.Set<WorkflowState>()
                .AsNoTracking().FirstAsync(s => s.Id == instance.CurrentStateId);
            currentState.IsFinal.Should().BeTrue("Close lands the instance in the final state");
        }
    }

    /// <summary>P6: the branch-status read projection surfaces each pooled child's live WorkflowTask id
    /// (and a null assigneeId while unclaimed) so the FE can drive pool Claim from the panel.</summary>
    [Fact]
    public async Task BranchStatus_ExposesTaskId_ForPooledChildren()
    {
        var (seeded, instancePublicId, children) = await SeedForkedInstanceAsync(Factory);

        var user = Guid.NewGuid();
        using var client = CreateClientWithPermissions(
            ["WORKFLOW.INSTANCE.VIEW"], userId: user.ToString(), tenantId: seeded.TenantId.ToString());

        var resp = await client.GetAsync($"{InstancesUrl}/{instancePublicId}/branch-status");
        var raw = await resp.Content.ReadAsStringAsync();
        resp.StatusCode.Should().Be(HttpStatusCode.OK, $"branch-status must be readable. Body: {raw}");

        var childStatuses = JsonSerializer.Deserialize<JsonElement>(raw)
            .GetProperty("data")
            .EnumerateArray()
            .SelectMany(b => b.GetProperty("childStatuses").EnumerateArray())
            .ToList();

        childStatuses.Should().HaveCount(2, "the forked AND branch stages two children");

        var returnedTaskIds = childStatuses
            .Select(c => c.GetProperty("taskId").GetInt64())
            .ToList();
        returnedTaskIds.Should().BeEquivalentTo(children.Select(c => c.TaskId),
            "branch-status must surface each pooled child's live task id so the FE can claim it");

        childStatuses.Should().OnlyContain(
            c => c.GetProperty("assigneeId").ValueKind == JsonValueKind.Null,
            "pooled children are unclaimed → assigneeId stays null until someone claims");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // Negatives — engine fail-CLOSED guards
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>RT6: a pool member who has not claimed the child task cannot resolve it (only the
    /// assignee may). Before the claim, the task is unassigned, so the assignee check fails → 403.</summary>
    [Fact]
    public async Task ResolveBeforeClaim_IsForbidden()
    {
        var (seeded, instancePublicId, children) = await SeedForkedInstanceAsync(Factory);

        var user = Guid.NewGuid();
        using var client = CreateClientWithPermissions([PoolPerm], userId: user.ToString(), tenantId: seeded.TenantId.ToString());

        var resp = await client.PostAsJsonAsync(
            $"{InstancesUrl}/{instancePublicId}/branch-children/{children[0].ChildStatusId}/resolve",
            new { ResolutionType = (int)BranchResolutionType.Approved, Comment = (string?)null });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "resolving a branch child before claiming it must be rejected — only the assigned approver may resolve");
    }

    /// <summary>RT1: while a branch is InProgress, an ordinary transition off the branching state is
    /// blocked (fork-exit guard) — it would bypass the parallel approval entirely.</summary>
    [Fact]
    public async Task OrdinaryTransitionWhileBranchInProgress_IsForbidden()
    {
        var (seeded, instancePublicId, _) = await SeedForkedInstanceAsync(Factory);

        var user = Guid.NewGuid();
        using var client = CreateClientWithPermissions([PoolPerm], userId: user.ToString(), tenantId: seeded.TenantId.ToString());

        var resp = await client.PostAsJsonAsync(
            $"{InstancesUrl}/{instancePublicId}/transitions",
            new { TransitionId = seeded.ContinueTransitionId, Comment = (string?)null });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "an ordinary transition off the branching state must be blocked while the branch is InProgress");
    }

    /// <summary>RT4: a branch child without a claimable pool is rejected when the definition is activated
    /// (RB5). This is the guard that makes the runtime fork-entry's empty-pool fail-CLOSED unreachable
    /// through the UI — an AND-join could never complete for an unhandlable child, so an unpooled-child
    /// definition can never go live. (The draft graph saves; activation is the gate.)</summary>
    [Fact]
    public async Task ActivateParallelDef_BranchChildWithoutPool_IsRejected()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var adminClient = CreateAuthenticatedClient(
            userId: userId.ToString(), roles: ["SystemAdmin"], tenantId: tenantId.ToString());

        var createResp = await adminClient.PostAsJsonAsync(DefinitionsUrl, new
        {
            Name = "Parallel Branch Unpooled Child",
            Description = "RT4 negative",
            TenantId = tenantId.ToString(),
            CreatedBy = userId,
            EntityType = "Generic",
            States = StatesPayload(),
            Transitions = TransitionsPayload(withAssignment: false),
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var defPublicId = JsonSerializer.Deserialize<JsonElement>(await createResp.Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetGuid();

        // The draft graph saves (validation of the live gate happens at activation).
        var graphResp = await adminClient.PutAsJsonAsync($"{DefinitionsUrl}/{defPublicId}/graph", new
        {
            States = StatesPayload(),
            Transitions = TransitionsPayload(withAssignment: true),
            Branches = BranchesPayload(secondChildPool: ChildPoolKind.None),
        });
        graphResp.StatusCode.Should().Be(HttpStatusCode.OK, "the draft graph persists; the pool gate is at activation");

        var activateResp = await adminClient.PostAsync($"{DefinitionsUrl}/{defPublicId}/activate", null);
        var activateRaw = await activateResp.Content.ReadAsStringAsync();

        activateResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            $"activating a def whose branch child has no claimable pool must be rejected (RB5). Body: {activateRaw}");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // Assertion helpers (DB reads specific to this suite's state-flow assertions)
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<(long InstanceInternalId, long BranchingStateId, long SynthesisStateId)>
        ResolveInstanceStateIdsAsync(Guid instancePublicId, SeededParallelDefinition seeded)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var instance = await ctx.WorkflowInstances.AsNoTracking().FirstAsync(i => i.PublicId == instancePublicId);
        var states = await ctx.Set<WorkflowState>().AsNoTracking()
            .Where(s => s.DefinitionId == seeded.DefinitionInternalId).ToListAsync();
        return (instance.Id,
            states.First(s => s.Name == "Submitted").Id,
            states.First(s => s.Name == "Returned").Id);
    }

    private async Task AssertCurrentStateAsync(Guid instancePublicId, long expectedStateId, string because)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var instance = await ctx.WorkflowInstances.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(i => i.PublicId == instancePublicId);
        instance.CurrentStateId.Should().Be(expectedStateId, because);
    }
}
