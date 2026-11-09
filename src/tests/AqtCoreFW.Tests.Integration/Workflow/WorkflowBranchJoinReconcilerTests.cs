using AqtCoreFW.Tests.Integration.Infrastructure;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Jobs;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static AqtCoreFW.Tests.Integration.Workflow.ParallelBranchTestSeed;

namespace AqtCoreFW.Tests.Integration.Workflow;

/// <summary>
/// The branch-join reconciler backstop. A join whose condition is satisfied but whose
/// instance never advanced (a crash between the child-resolution commit and the join transition, or a
/// genuine transient conflict surfaced by winner-election) leaves the branch InProgress + the instance
/// parked at the branching state. <see cref="BranchJoinReconcilerJob"/> finds and finishes it by
/// delegating to <see cref="AqtCoreFW.Workflow.Application.Services.IBranchJoinCompletionService"/>.
///
/// Strands are manufactured deterministically by resolving children directly on the entity (skipping the
/// resolve handler that would itself complete the join) — this reproduces the exact post-crash DB shape
/// without racing anything. Shares the proven save/drive recipe via <see cref="ParallelBranchTestSeed"/>.
///
/// This suite is ALSO the empirical gate for the one real risk in this job: it loads + mutates the
/// instance through the Application repository path from a background scope that has NO request tenant
/// context. A green here proves that path is not blocked by a tenant query-filter / RLS.
/// </summary>
[Collection("Integration")]
public sealed class WorkflowBranchJoinReconcilerTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // Recovered: a satisfied-but-unadvanced join is completed by the reconciler.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StrandedJoin_AllChildrenResolved_ReconcilerCompletesJoin()
    {
        var (seeded, instancePublicId, _) = await SeedForkedInstanceAsync(Factory);
        var (instanceInternalId, branchStatusId) = await IdsAsync(instancePublicId);

        // Manufacture the crash-after-resolve strand: both children Approved, but the join transition was
        // never applied → branch stays InProgress, instance parked at the branching state.
        await ResolveAllChildrenDirectAsync(branchStatusId);

        await RunReconcilerJobAsync();

        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var continuationStateId = await StateIdAsync(ctx, seeded.DefinitionInternalId, "Returned");
        var instance = await ctx.WorkflowInstances.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(i => i.Id == instanceInternalId);
        instance.CurrentStateId.Should().Be(continuationStateId,
            "the reconciler must advance the stranded instance to the continuation state");

        var branch = await ctx.InstanceBranchStatuses.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(s => s.Id == branchStatusId);
        branch.Status.Should().Be(WorkflowInstanceBranchStatus.Statuses.Resolved,
            "the branch must be Resolved once the reconciler completes the join");

        var openContinuation = await ctx.WorkflowTasks.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(t => t.WorkflowInstanceId == instanceInternalId
                             && t.BranchChildStatusId == null
                             && t.Status == WorkflowTaskStatus.Open);
        openContinuation.Should().Be(1,
            "exactly one continuation task is staged — the reconciler completes the join once");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // Permanent config strand: empty continuation pool fails identically every run → NOT advanced,
    // throttle armed, idempotent across cycles (no spam, no exception).
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PermanentConfigStrand_EmptyContinuation_NotAdvanced_AndThrottled()
    {
        // Continuation state ("Returned") fed by an empty-refs outgoing ("Close") → its pool resolves
        // empty at join time → CompleteJoinAsync fails CLOSED (NoHandlerResolved) on every attempt.
        var seeded = await SeedActivatedParallelDefinitionAsync(
            Factory,
            TransitionsPayload(withAssignment: true, emptyRefsContinuationOutgoing: true),
            BranchesPayload());
        var (instancePublicId, _) = await ForkInstanceAsync(Factory, seeded);
        var (instanceInternalId, branchStatusId) = await IdsAsync(instancePublicId);
        var branchingStateId = await BranchingStateIdAsync(seeded.DefinitionInternalId);

        await ResolveAllChildrenDirectAsync(branchStatusId);

        await RunReconcilerJobAsync();

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var instance = await ctx.WorkflowInstances.IgnoreQueryFilters().AsNoTracking()
                .FirstAsync(i => i.Id == instanceInternalId);
            instance.CurrentStateId.Should().Be(branchingStateId,
                "a handler-less continuation cannot be completed — the instance stays parked, NOT advanced");

            var branch = await ctx.InstanceBranchStatuses.IgnoreQueryFilters().AsNoTracking()
                .FirstAsync(s => s.Id == branchStatusId);
            branch.Status.Should().Be(WorkflowInstanceBranchStatus.Statuses.InProgress,
                "the branch is left InProgress — the reconciler must not resolve a join it cannot complete");
        }

        // The throttle key (IMemoryCache singleton) is armed → the permanent-strand path ran and the
        // 5-minute cadence will not re-log this strand within the window.
        var cache = Factory.Services.GetRequiredService<IMemoryCache>();
        cache.TryGetValue($"branch-join-reconciler:permanent-strand:{branchStatusId}", out _)
            .Should().BeTrue("the permanent-config-strand log must be throttled after the first encounter");

        // Second cycle: still a no-op, still no exception (idempotent under repeated permanent failure).
        await RunReconcilerJobAsync();

        await using var verifyScope = Factory.Services.CreateAsyncScope();
        var verifyCtx = verifyScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var after = await verifyCtx.WorkflowInstances.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(i => i.Id == instanceInternalId);
        after.CurrentStateId.Should().Be(branchingStateId, "still parked after a second reconciler cycle");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // Non-candidate: an unmet join condition (AND with one child still pending) is left untouched.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IncompleteJoin_OneChildPending_LeftUntouched()
    {
        var (seeded, instancePublicId, _) = await SeedForkedInstanceAsync(Factory);
        var (instanceInternalId, branchStatusId) = await IdsAsync(instancePublicId);
        var branchingStateId = await BranchingStateIdAsync(seeded.DefinitionInternalId);

        // Resolve only ONE child of the AND branch → CheckJoinCondition is false (needs all approved).
        await ResolveOneChildDirectAsync(branchStatusId);

        await RunReconcilerJobAsync();

        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var instance = await ctx.WorkflowInstances.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(i => i.Id == instanceInternalId);
        instance.CurrentStateId.Should().Be(branchingStateId,
            "an unmet AND join must not be advanced — the reconciler only finishes satisfied joins");

        var branch = await ctx.InstanceBranchStatuses.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(s => s.Id == branchStatusId);
        branch.Status.Should().Be(WorkflowInstanceBranchStatus.Statuses.InProgress,
            "the branch stays InProgress while a child is still pending");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // Healthy completed instance: the reconciler is a no-op — it never re-advances or double-applies.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HealthyCompletedJoin_ReconcilerIsNoOp_NoDoubleApply()
    {
        var (seeded, instancePublicId, children) = await SeedForkedInstanceAsync(Factory);

        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        using var clientA = CreateClientWithPermissions([PoolPerm], userId: userA.ToString(), tenantId: seeded.TenantId.ToString());
        using var clientB = CreateClientWithPermissions([PoolPerm], userId: userB.ToString(), tenantId: seeded.TenantId.ToString());

        // Full happy path through the real handlers: the join completes synchronously, instance advances.
        await ClaimAsync(clientA, children[0].TaskId);
        await ResolveBranchChildAsync(clientA, instancePublicId, children[0].ChildStatusId, BranchResolutionType.Approved);
        await ClaimAsync(clientB, children[1].TaskId);
        await ResolveBranchChildAsync(clientB, instancePublicId, children[1].ChildStatusId, BranchResolutionType.Approved);

        var (instanceInternalId, _) = await IdsAsync(instancePublicId);
        long continuationStateId;
        await using (var preScope = Factory.Services.CreateAsyncScope())
        {
            var preCtx = preScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            continuationStateId = await StateIdAsync(preCtx, seeded.DefinitionInternalId, "Returned");
        }

        await RunReconcilerJobAsync();

        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var instance = await ctx.WorkflowInstances.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(i => i.Id == instanceInternalId);
        instance.CurrentStateId.Should().Be(continuationStateId,
            "a healthy completed instance stays at the continuation state — the reconciler must not touch it");

        var openContinuation = await ctx.WorkflowTasks.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(t => t.WorkflowInstanceId == instanceInternalId
                             && t.BranchChildStatusId == null
                             && t.Status == WorkflowTaskStatus.Open);
        openContinuation.Should().Be(1,
            "no double-apply: a healthy instance already at the continuation state is excluded by the scan predicate");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    private static Task<long> StateIdAsync(WorkflowDbContext ctx, long definitionInternalId, string stateName) =>
        ctx.Set<WorkflowState>().AsNoTracking()
            .Where(s => s.DefinitionId == definitionInternalId && s.Name == stateName)
            .Select(s => s.Id).FirstAsync();

    private async Task<long> BranchingStateIdAsync(long definitionInternalId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        return await StateIdAsync(ctx, definitionInternalId, "Submitted");
    }

    private async Task<(long InstanceInternalId, long BranchStatusId)> IdsAsync(Guid instancePublicId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var instance = await ctx.WorkflowInstances.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(i => i.PublicId == instancePublicId);
        var branch = await ctx.InstanceBranchStatuses.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(s => s.InstanceId == instance.Id);
        return (instance.Id, branch.Id);
    }

    // Resolve EVERY pending child directly on the entity (skips the resolve handler that would itself
    // complete the join) → the branch reaches all-resolved while staying InProgress = the strand.
    private async Task ResolveAllChildrenDirectAsync(long branchStatusId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var branch = await ctx.InstanceBranchStatuses.IgnoreQueryFilters()
            .Include(s => s.ChildStatuses).FirstAsync(s => s.Id == branchStatusId);
        foreach (var child in branch.ChildStatuses.Where(c => !c.ResolutionType.HasValue))
            child.Resolve(BranchResolutionType.Approved, Guid.NewGuid(), "stranded for reconciler test");
        await ctx.SaveChangesAsync();
    }

    private async Task ResolveOneChildDirectAsync(long branchStatusId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var branch = await ctx.InstanceBranchStatuses.IgnoreQueryFilters()
            .Include(s => s.ChildStatuses).FirstAsync(s => s.Id == branchStatusId);
        var child = branch.ChildStatuses.OrderBy(c => c.Id).First(c => !c.ResolutionType.HasValue);
        child.Resolve(BranchResolutionType.Approved, Guid.NewGuid(), "single resolve for incomplete-join test");
        await ctx.SaveChangesAsync();
    }

    private async Task RunReconcilerJobAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var job = scope.ServiceProvider.GetRequiredService<BranchJoinReconcilerJob>();
        await job.ExecuteAsync();
    }
}
