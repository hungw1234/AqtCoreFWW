using System.Net;
using System.Net.Http.Json;
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
/// Negative-path hardening for the parallel-branch runtime — misconfigured pools that pass design-time
/// validation (RB5 / R2 check only that a clause EXISTS, not that it resolves to ≥1 user) but diverge at
/// runtime. Shares the proven save/drive recipe via <see cref="ParallelBranchTestSeed"/>.
///
/// Each scenario is verified against ACTUAL engine behavior, not the original hypotheses — which assumed a
/// 0-member role yields an empty pool. It does not: the resolver pushes a role/permission predicate
/// regardless of membership, so the empty-pool guards are reachable only via an empty refs array:
///   • empty-refs child pool        → fork-entry fails CLOSED (422), instance never forks.
///   • empty-refs continuation pool → join fails CLOSED (422); the already-committed child resolutions
///     leave the branch InProgress (a strand whose terminal recovery is the deferred reconciler's job).
///   • unheld-role child pool       → REJECTED at activation by the RB5 live-claimant check: a ByRole pool
///     that resolves to no active user can never be claimed, so the def is not allowed to go live (closing
///     the previously-reachable AND-join deadlock at config-time rather than at runtime).
/// </summary>
[Collection("Integration")]
public sealed class WorkflowParallelBranchNegativeTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    // ── empty-refs child pool → fork-entry 422, instance not forked ──────────────────────────────────

    [Fact]
    public async Task ForkChildPoolWithEmptyRefs_IsRejectedAtForkEntry()
    {
        var seeded = await SeedActivatedParallelDefinitionAsync(
            Factory,
            TransitionsPayload(withAssignment: true),
            BranchesPayload(secondChildPool: ChildPoolKind.EmptyRefs));

        var instancePublicId = await CreateInstanceAsync(seeded.AdminClient, seeded.DefinitionPublicId);

        var submitter = Guid.NewGuid();
        using var client = CreateClientWithPermissions(
            [PoolPerm], userId: submitter.ToString(), tenantId: seeded.TenantId.ToString());

        var resp = await client.PostAsJsonAsync(
            $"{InstancesUrl}/{instancePublicId}/transitions",
            new { TransitionId = seeded.SubmitTransitionId, Comment = (string?)null });
        var raw = await resp.Content.ReadAsStringAsync();

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            $"a branch child whose pool resolves to nobody must fail the fork-entry gate CLOSED. Body: {raw}");

        // The fork-entry gate returns before any state change or staging — the instance never entered the
        // fork: no branch status row, still at the initial state.
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var instance = await ctx.WorkflowInstances.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(i => i.PublicId == instancePublicId);
        var draftStateId = await StateIdAsync(ctx, seeded.DefinitionInternalId, "Draft");
        instance.CurrentStateId.Should().Be(draftStateId,
            "a rejected fork-entry must not advance the instance out of the initial state");

        var branchExists = await ctx.InstanceBranchStatuses.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(s => s.InstanceId == instance.Id);
        branchExists.Should().BeFalse("no branch status may be staged when fork-entry fails CLOSED");
    }

    // ── empty-refs continuation pool → join 422 + strand ────────────────────────────────────────────

    [Fact]
    public async Task JoinWithEmptyContinuationPool_FailsClosed_AndStrandsBranch()
    {
        var seeded = await SeedActivatedParallelDefinitionAsync(
            Factory,
            TransitionsPayload(withAssignment: true, emptyRefsContinuationOutgoing: true),
            BranchesPayload());

        var (instancePublicId, children) = await ForkInstanceAsync(Factory, seeded);

        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        using var clientA = CreateClientWithPermissions([PoolPerm], userId: userA.ToString(), tenantId: seeded.TenantId.ToString());
        using var clientB = CreateClientWithPermissions([PoolPerm], userId: userB.ToString(), tenantId: seeded.TenantId.ToString());

        // First child resolves cleanly (join not yet satisfied — 1 of 2).
        await ClaimAsync(clientA, children[0].TaskId);
        await ResolveBranchChildAsync(clientA, instancePublicId, children[0].ChildStatusId, BranchResolutionType.Approved);

        // The LAST resolve satisfies the AND-join → CompleteJoinAsync resolves the continuation pool, which
        // is empty → fail-CLOSED 422 BEFORE the atomic join window (no MarkResolved, no continuation task).
        await ClaimAsync(clientB, children[1].TaskId);
        var lastResolve = await clientB.PostAsJsonAsync(
            $"{InstancesUrl}/{instancePublicId}/branch-children/{children[1].ChildStatusId}/resolve",
            new { ResolutionType = (int)BranchResolutionType.Approved, Comment = (string?)null });
        var raw = await lastResolve.Content.ReadAsStringAsync();
        lastResolve.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            $"a join whose continuation pool resolves to nobody must fail CLOSED. Body: {raw}");

        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var instance = await ctx.WorkflowInstances.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(i => i.PublicId == instancePublicId);

        // Both children committed their resolutions (the resolve handler commits each child separately for
        // sibling visibility), but the join never marked the branch Resolved → the branch is STRANDED:
        // InProgress with every child resolved, instance not advanced. Terminal recovery of this strand is
        // the deferred reconciler's responsibility — this test pins the present fail-CLOSED behavior.
        var branch = await ctx.InstanceBranchStatuses.IgnoreQueryFilters().AsNoTracking()
            .Include(s => s.ChildStatuses).FirstAsync(s => s.InstanceId == instance.Id);
        branch.Status.Should().Be(WorkflowInstanceBranchStatus.Statuses.InProgress,
            "the join did not complete, so the branch must not be marked Resolved");
        branch.ChildStatuses.Should().OnlyContain(cs => cs.ResolutionType == BranchResolutionType.Approved,
            "both children committed their resolutions before the join was attempted");

        var branchingStateId = await StateIdAsync(ctx, seeded.DefinitionInternalId, "Submitted");
        instance.CurrentStateId.Should().Be(branchingStateId,
            "a failed join must not advance the instance to the continuation state");

        var openContinuation = await ctx.WorkflowTasks.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(t => t.WorkflowInstanceId == instance.Id
                             && t.BranchChildStatusId == null
                             && t.Status == WorkflowTaskStatus.Open);
        openContinuation.Should().Be(0, "no continuation task may be staged when the join fails CLOSED");
    }

    // ── unheld-role child pool → REJECTED at activation by the RB5 live-claimant check ───────────────

    [Fact]
    public async Task ForkChildPoolWithUnheldRole_IsRejectedAtActivation()
    {
        // Save (but do not activate) a def whose second child is pooled on a role no active user holds.
        var saved = await SeedSavedParallelDefinitionAsync(
            Factory,
            TransitionsPayload(withAssignment: true),
            BranchesPayload(secondChildPool: ChildPoolKind.UnheldRole));

        // Activation must FAIL: the RB5 live-claimant check resolves the ByRole pool against live identity
        // (no AspNetUserRoles row holds it) → 0 claimants → the child would fork an unclaimable task whose
        // AND-join could never complete, so the def is not allowed to go live.
        var activateResp = await saved.AdminClient.PostAsync(
            $"{DefinitionsUrl}/{saved.DefinitionPublicId}/activate", null);
        var raw = await activateResp.Content.ReadAsStringAsync();

        activateResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            $"a branch child pooled on a role no active user holds must be rejected at activation. Body: {raw}");

        // Discriminating signal (vacuity guard): the failure is the RB5 unclaimable-role-pool reason that
        // names the offending role code — not an unrelated activation error.
        raw.Should().Contain("RB5", "the activation failure must be the branch-child pool rule");
        raw.Should().Contain(UnheldRoleCode, "the violation must identify the unresolvable role pool");
        raw.Should().MatchRegex("(?i)no active user|unclaimable",
            "the violation must state the pool resolves to no claimant");

        // The def must stay inactive — no partial activation.
        await using var scope = Factory.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var def = await ctx.WorkflowDefinitions.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(d => d.PublicId == saved.DefinitionPublicId);
        def.IsActive.Should().BeFalse("a definition that fails the RB5 claimant check must remain inactive");
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    private static Task<long> StateIdAsync(WorkflowDbContext ctx, long definitionInternalId, string stateName) =>
        ctx.Set<WorkflowState>().AsNoTracking()
            .Where(s => s.DefinitionId == definitionInternalId && s.Name == stateName)
            .Select(s => s.Id).FirstAsync();
}
