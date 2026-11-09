using AqtCoreFW.Workflow.Domain.Entities;

namespace AqtCoreFW.Workflow.Domain.Tests.Entities;

/// <summary>
/// Unit tests for WorkflowTask pool-claim lifecycle (Phase-1, plan 260519-0421 v2).
/// Pool task = Status Open + AssigneeId null until a pool-eligible member claims it.
/// Claim (RT2-5): tracked-read + assert AssigneeId == null → throw (WF_ALREADY_CLAIMED);
/// RowVersion is the DB race-tiebreak (asserted at the EF layer in integration, not here).
/// AssigneeId is ALREADY Guid? (H1 — semantic-shift, NOT a type change).
/// </summary>
public sealed class WorkflowTaskClaimTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private const long InstanceId = 1L;
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Claimant = Guid.NewGuid();

    private static WorkflowTask PoolTask() =>
        WorkflowTask.Create(
            InstanceId, "Pool Task", null,
            assigneeId: null,                       // pool task: unassigned until Claim
            WorkflowTaskPriority.Normal, null, TenantId, Actor);

    // --- Claim() ---

    [Fact]
    public void Claim_AssignsTaskToClaimant()
    {
        var task = PoolTask();

        task.Claim(Claimant, Claimant);

        task.AssigneeId.Should().Be(Claimant);
    }

    [Fact]
    public void Claim_SetsClaimedAt()
    {
        var task = PoolTask();

        task.Claim(Claimant, Claimant);

        task.ClaimedAt.Should().NotBeNull();
    }

    [Fact]
    public void Claim_LeavesStatusOpen()
    {
        // User decision 2026-05-19: Claim only locks ownership; Start() later does
        // Open→InProgress so "claimed" stays distinguishable from "in progress".
        var task = PoolTask();

        task.Claim(Claimant, Claimant);

        task.Status.Should().Be(WorkflowTaskStatus.Open);
    }

    [Fact]
    public void Claim_WhenAlreadyClaimed_Throws()
    {
        var task = PoolTask();
        task.Claim(Claimant, Claimant);

        var act = () => task.Claim(Guid.NewGuid(), Guid.NewGuid());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already claimed*");
    }

    [Fact]
    public void Claim_WhenPreAssigned_Throws_LoserNeverOverwritesAssignee()
    {
        var preAssigned = Guid.NewGuid();
        var task = WorkflowTask.Create(
            InstanceId, "Assigned Task", null, preAssigned,
            WorkflowTaskPriority.Normal, null, TenantId, Actor);

        var act = () => task.Claim(Claimant, Claimant);

        act.Should().Throw<InvalidOperationException>();
        task.AssigneeId.Should().Be(preAssigned, "loser must never overwrite the assignee");
    }

    // --- Unclaim() ---

    [Fact]
    public void Unclaim_ReturnsTaskToPool()
    {
        var task = PoolTask();
        task.Claim(Claimant, Claimant);

        task.Unclaim(Claimant);

        task.AssigneeId.Should().BeNull();
        task.Status.Should().Be(WorkflowTaskStatus.Open);
        task.ClaimedAt.Should().BeNull();
    }

    [Fact]
    public void Unclaim_WhenNotClaimed_IsIdempotentNoOp()
    {
        var task = PoolTask();

        var act = () => task.Unclaim(Actor);

        act.Should().NotThrow();
        task.AssigneeId.Should().BeNull();
        task.Status.Should().Be(WorkflowTaskStatus.Open);
    }

    [Fact]
    public void Claim_ThenUnclaim_ThenClaim_AllowsReClaimByAnother()
    {
        var task = PoolTask();
        var other = Guid.NewGuid();

        task.Claim(Claimant, Claimant);
        task.Unclaim(Claimant);
        task.Claim(other, other);

        task.AssigneeId.Should().Be(other);
        task.Status.Should().Be(WorkflowTaskStatus.Open);
    }

    // --- SetCandidate() ---

    [Fact]
    public void SetCandidate_StoresSnapshotJson()
    {
        var task = PoolTask();
        const string snapshot = "[{\"t\":\"Permission\",\"r\":\"WF.Task.Process\"}]";

        task.SetCandidate(snapshot);

        task.CandidateRefsJson.Should().Be(snapshot);
    }

    [Fact]
    public void SetCandidate_AcceptsNullForConcreteAssignee()
    {
        var task = PoolTask();

        var act = () => task.SetCandidate(null);

        act.Should().NotThrow();
        task.CandidateRefsJson.Should().BeNull();
    }
}
