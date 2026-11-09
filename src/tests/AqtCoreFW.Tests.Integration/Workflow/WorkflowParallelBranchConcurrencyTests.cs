using System.Net;
using System.Net.Http.Json;
using AqtCoreFW.Tests.Integration.Infrastructure;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Jobs;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static AqtCoreFW.Tests.Integration.Workflow.ParallelBranchTestSeed;

namespace AqtCoreFW.Tests.Integration.Workflow;

/// <summary>
/// P5-FOLLOWUP H2 — concurrency + SLA-vs-join hardening for the parallel-branch runtime engine.
/// Shares the proven save/drive recipe with <see cref="WorkflowParallelBranchRuntimeTests"/> via
/// <see cref="ParallelBranchTestSeed"/>.
///
/// H2a (concurrent join) drives both sibling resolves through real parallel HTTP. NOTE: Task.WhenAll
/// scheduling is non-deterministic, so the two resolves may interleave such that only ONE reaches
/// IBranchJoinCompletionService — the RowVersion winner-election is therefore exercised opportunistically,
/// not deterministically (mirrors the caveat on WorkflowTasksClaimTests' concurrent-claim race; a
/// deterministic mechanism test belongs in the Infrastructure.Tests scope via a SaveChanges-barrier
/// interceptor, carried forward). What IS asserted holds under EVERY interleaving: the join applies
/// exactly once (one continuation task, instance advanced once) and no resolver 500s/strands.
///
/// H2b proves the SLA timeout job and the join do not corrupt each other, tested deterministically by
/// ordering (job runs after a known terminal state) rather than racing them.
/// </summary>
[Collection("Integration")]
public sealed class WorkflowParallelBranchConcurrencyTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    // A timeout long enough to never fire on its own during the test, but armed so the SLA job CAN act
    // once CreatedAt is backdated past the threshold. (The runtime suite uses 0 = no timeout.)
    private const int ArmedTimeoutMinutes = 30;

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // H2a — concurrent 2-sibling resolve → join applies exactly once
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConcurrentResolveBothChildren_JoinAppliesExactlyOnce()
    {
        var (seeded, instancePublicId, children) = await SeedForkedInstanceAsync(Factory);

        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        using var clientA = CreateClientWithPermissions([PoolPerm], userId: userA.ToString(), tenantId: seeded.TenantId.ToString());
        using var clientB = CreateClientWithPermissions([PoolPerm], userId: userB.ToString(), tenantId: seeded.TenantId.ToString());

        // Each pool user claims a distinct child (sequential — claim is not the race under test).
        await ClaimAsync(clientA, children[0].TaskId);
        await ClaimAsync(clientB, children[1].TaskId);

        // Fire BOTH resolves simultaneously — Testcontainers SQL Server provides real DB concurrency.
        // The loser's CompleteJoinAsync must resolve to a clean no-op (204), not a 500/conflict: the
        // instance repo translates DbUpdateConcurrencyException → Result.Fail, and the winner-election
        // re-read then sees the branch already Resolved → Result.Ok. A red here is a real engine gap.
        var responses = await Task.WhenAll(
            clientA.PostAsJsonAsync($"{InstancesUrl}/{instancePublicId}/branch-children/{children[0].ChildStatusId}/resolve",
                new { ResolutionType = (int)BranchResolutionType.Approved, Comment = (string?)null }),
            clientB.PostAsJsonAsync($"{InstancesUrl}/{instancePublicId}/branch-children/{children[1].ChildStatusId}/resolve",
                new { ResolutionType = (int)BranchResolutionType.Approved, Comment = (string?)null }));

        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()));
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.NoContent,
            $"both concurrent resolves must succeed (loser's join is a no-op, not a 500/conflict). Bodies: {string.Join(" | ", bodies)}");

        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var instance = await ctx.WorkflowInstances.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(i => i.PublicId == instancePublicId);
        var synthesisStateId = await StateIdAsync(ctx, seeded.DefinitionInternalId, "Returned");
        instance.CurrentStateId.Should().Be(synthesisStateId,
            "the join must advance the instance to the continuation state exactly once");

        var branch = await ctx.InstanceBranchStatuses.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(s => s.InstanceId == instance.Id);
        branch.Status.Should().Be(WorkflowInstanceBranchStatus.Statuses.Resolved,
            "the branch must be Resolved after both children resolve");

        // Double-apply guard: the loser's staged continuation task must have been rolled back
        // (ClearChanges) so EXACTLY one open continuation task exists — not two.
        var openContinuation = await ctx.WorkflowTasks.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(t => t.WorkflowInstanceId == instance.Id
                             && t.BranchChildStatusId == null
                             && t.Status == WorkflowTaskStatus.Open);
        openContinuation.Should().Be(1,
            "winner-election must apply the join once: exactly one continuation task, never a duplicate");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // H2b-1 — SLA timeout job is a no-op against an already-joined (Resolved) branch
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TimeoutJob_AfterJoinResolved_IsNoOp()
    {
        var (seeded, instancePublicId, children) = await SeedForkedInstanceAsync(Factory, ArmedTimeoutMinutes);

        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        using var clientA = CreateClientWithPermissions([PoolPerm], userId: userA.ToString(), tenantId: seeded.TenantId.ToString());
        using var clientB = CreateClientWithPermissions([PoolPerm], userId: userB.ToString(), tenantId: seeded.TenantId.ToString());

        await ClaimAsync(clientA, children[0].TaskId);
        await ResolveBranchChildAsync(clientA, instancePublicId, children[0].ChildStatusId, BranchResolutionType.Approved);
        await ClaimAsync(clientB, children[1].TaskId);
        await ResolveBranchChildAsync(clientB, instancePublicId, children[1].ChildStatusId, BranchResolutionType.Approved);

        long instanceInternalId;
        long branchStatusId;
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var instance = await ctx.WorkflowInstances.IgnoreQueryFilters().AsNoTracking().FirstAsync(i => i.PublicId == instancePublicId);
            instanceInternalId = instance.Id;
            var branch = await ctx.InstanceBranchStatuses.IgnoreQueryFilters().AsNoTracking().FirstAsync(s => s.InstanceId == instanceInternalId);
            branchStatusId = branch.Id;
            branch.Status.Should().Be(WorkflowInstanceBranchStatus.Statuses.Resolved, "precondition: the join completed");
        }

        // Backdate CreatedAt past the timeout threshold so the ONLY thing keeping the job off this branch
        // is the Status='InProgress' filter (not the timeout window). Then run the job.
        await BackdateBranchCreatedAtAsync(branchStatusId);
        await RunTimeoutJobAsync();

        await using var verifyScope = Factory.Services.CreateAsyncScope();
        var verifyCtx = verifyScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var afterBranch = await verifyCtx.InstanceBranchStatuses.IgnoreQueryFilters().AsNoTracking()
            .Include(s => s.ChildStatuses).FirstAsync(s => s.Id == branchStatusId);
        afterBranch.Status.Should().Be(WorkflowInstanceBranchStatus.Statuses.Resolved,
            "the timeout job must NOT touch an already-Resolved branch (SQL Status filter excludes it)");
        afterBranch.ChildStatuses.Should().OnlyContain(cs => cs.ResolutionType == BranchResolutionType.Approved,
            "children stay Approved — the job must not flip a resolved child to Timeout");

        var afterInstance = await verifyCtx.WorkflowInstances.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(i => i.Id == instanceInternalId);
        var synthesisStateId = await StateIdAsync(verifyCtx, seeded.DefinitionInternalId, "Returned");
        afterInstance.CurrentStateId.Should().Be(synthesisStateId, "the instance stays at the continuation state");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // H2b-2 — resolving a child after the branch timed out is rejected and does not advance
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// After the SLA job times a branch out, a late resolve on one of its children is rejected (404 —
    /// the resolve handler loads the active branch via an InProgress-only filter, so a TimedOut branch
    /// is not found) and the instance is NOT advanced. NOTE: a timed-out branch currently STRANDS the
    /// instance at the branching state (no continuation is staged) — completing the timeout path is the
    /// H1 reconciler's job (carry-forward), so this test asserts the present fail-safe behavior, not a
    /// fully-handled terminal state.
    /// </summary>
    [Fact]
    public async Task ResolveAfterTimeout_IsRejected_NoAdvance()
    {
        var (seeded, instancePublicId, children) = await SeedForkedInstanceAsync(Factory, ArmedTimeoutMinutes);

        var userA = Guid.NewGuid();
        using var clientA = CreateClientWithPermissions([PoolPerm], userId: userA.ToString(), tenantId: seeded.TenantId.ToString());

        // Claim one child but do NOT resolve it — leave the branch InProgress so the job can time it out.
        await ClaimAsync(clientA, children[0].TaskId);

        long instanceInternalId;
        long branchStatusId;
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var instance = await ctx.WorkflowInstances.IgnoreQueryFilters().AsNoTracking().FirstAsync(i => i.PublicId == instancePublicId);
            instanceInternalId = instance.Id;
            branchStatusId = (await ctx.InstanceBranchStatuses.IgnoreQueryFilters().AsNoTracking().FirstAsync(s => s.InstanceId == instanceInternalId)).Id;
        }

        await BackdateBranchCreatedAtAsync(branchStatusId);
        await RunTimeoutJobAsync();

        // Late resolve on the now-timed-out child.
        var resp = await clientA.PostAsJsonAsync(
            $"{InstancesUrl}/{instancePublicId}/branch-children/{children[0].ChildStatusId}/resolve",
            new { ResolutionType = (int)BranchResolutionType.Approved, Comment = (string?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "resolving a child of a timed-out branch must be rejected — no active (InProgress) branch is found");

        await using var verifyScope = Factory.Services.CreateAsyncScope();
        var verifyCtx = verifyScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var branch = await verifyCtx.InstanceBranchStatuses.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(s => s.Id == branchStatusId);
        branch.Status.Should().Be(WorkflowInstanceBranchStatus.Statuses.TimedOut,
            "the branch stays TimedOut — a late resolve must not revive it");

        var instanceAfter = await verifyCtx.WorkflowInstances.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(i => i.Id == instanceInternalId);
        var branchingStateId = await StateIdAsync(verifyCtx, seeded.DefinitionInternalId, "Submitted");
        instanceAfter.CurrentStateId.Should().Be(branchingStateId,
            "the instance is not advanced by a rejected resolve (stranded at the branching state pending H1)");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    private static Task<long> StateIdAsync(WorkflowDbContext ctx, long definitionInternalId, string stateName) =>
        ctx.Set<WorkflowState>().AsNoTracking()
            .Where(s => s.DefinitionId == definitionInternalId && s.Name == stateName)
            .Select(s => s.Id).FirstAsync();

    private async Task BackdateBranchCreatedAtAsync(long branchStatusId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE workflow.WorkflowInstanceBranchStatuses SET CreatedAt = DATEADD(MINUTE, -120, GETUTCDATE()) WHERE Id = {branchStatusId}");
    }

    // The job runs a DB-WIDE scan (every InProgress + armed-timeout + expired branch), not just this
    // test's instance. Safe under [Collection("Integration")] serialization only while no sibling test
    // leaves an InProgress branch with TimeoutMinutes>0 and a naturally-old CreatedAt at job time.
    private async Task RunTimeoutJobAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var job = scope.ServiceProvider.GetRequiredService<BranchTimeoutCheckerJob>();
        await job.ExecuteAsync();
    }
}
