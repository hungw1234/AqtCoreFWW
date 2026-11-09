using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Domain.Tests.Entities;

/// <summary>
/// Unit tests for WorkflowTransitionAssignment + WorkflowTransitionAssignmentClause
/// (Phase-1, plan 260519-0421 v2). Separate entity (RT2-9, NOT EF owned-type) keyed by
/// WorkflowTransitionId so the resolver can query-by-transition.
/// Invariants under test:
///   - CombineMode default = Union (0)
///   - AllowAssignorOverride default = FALSE (validated decision — override opt-in, fail-CLOSED)
///   - Clause refs round-trip; multi-clause assignments preserve order
/// T1 entity: Id=long (DB-assigned — 0 in unit tests).
/// </summary>
public sealed class WorkflowTransitionAssignmentTests
{
    private const long TransitionId = 42L;

    [Fact]
    public void Create_DefaultsCombineModeToUnion()
    {
        var a = WorkflowTransitionAssignment.Create(TransitionId);

        a.CombineMode.Should().Be(AssignmentCombineMode.Union);
    }

    [Fact]
    public void Create_DefaultsAllowAssignorOverrideToFalse()
    {
        // RT3-H1 / validation log: net-new column, validated default FALSE (fail-CLOSED posture).
        var a = WorkflowTransitionAssignment.Create(TransitionId);

        a.AllowAssignorOverride.Should().BeFalse();
    }

    [Fact]
    public void Create_SetsWorkflowTransitionId()
    {
        var a = WorkflowTransitionAssignment.Create(TransitionId);

        a.WorkflowTransitionId.Should().Be(TransitionId);
    }

    [Fact]
    public void Create_WithIntersectAndOverride_PersistsExplicitValues()
    {
        var a = WorkflowTransitionAssignment.Create(
            TransitionId, AssignmentCombineMode.Intersect, allowAssignorOverride: true);

        a.CombineMode.Should().Be(AssignmentCombineMode.Intersect);
        a.AllowAssignorOverride.Should().BeTrue();
    }

    [Fact]
    public void Create_HasNoClausesInitially()
    {
        var a = WorkflowTransitionAssignment.Create(TransitionId);

        a.Clauses.Should().BeEmpty();
    }

    [Fact]
    public void AddClause_AppendsClauseWithModeAndRefs()
    {
        var a = WorkflowTransitionAssignment.Create(TransitionId);

        a.AddClause(AssignmentClauseMode.ByPermission, "[\"WF.Task.Process\"]");

        a.Clauses.Should().HaveCount(1);
        a.Clauses.Single().Mode.Should().Be(AssignmentClauseMode.ByPermission);
        a.Clauses.Single().RefsJson.Should().Be("[\"WF.Task.Process\"]");
    }

    [Fact]
    public void AddClause_AllowsNullRefsForInitiatorAndPreviousHandler()
    {
        var a = WorkflowTransitionAssignment.Create(TransitionId);

        a.AddClause(AssignmentClauseMode.Initiator, null);
        a.AddClause(AssignmentClauseMode.PreviousHandler, null);

        a.Clauses.Should().HaveCount(2);
        a.Clauses.All(c => c.RefsJson is null).Should().BeTrue();
    }

    [Fact]
    public void AddClause_PreservesInsertionOrder()
    {
        var a = WorkflowTransitionAssignment.Create(TransitionId);

        a.AddClause(AssignmentClauseMode.SpecificUser, "[\"u1\"]");
        a.AddClause(AssignmentClauseMode.ByPermission, "[\"c1\"]");

        a.Clauses.Select(c => c.Mode)
            .Should().ContainInOrder(AssignmentClauseMode.SpecificUser, AssignmentClauseMode.ByPermission);
    }
}
