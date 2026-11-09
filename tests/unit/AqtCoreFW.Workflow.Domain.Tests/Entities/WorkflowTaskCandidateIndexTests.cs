using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Domain.Tests.Entities;

/// <summary>
/// Unit tests for WorkflowTaskCandidateIndex (Phase-1, plan 260519-0421 v2).
/// Tenant-scoped, write-once read-model row projecting one resolved candidate predicate
/// of a pool WorkflowTask. TenantId is a mandatory match column (defense-in-depth,
/// not RLS-only — RT2-8). T1 entity: Id=long.
/// </summary>
public sealed class WorkflowTaskCandidateIndexTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private const long TaskId = 7L;

    [Fact]
    public void Create_SetsAllProjectionColumns()
    {
        var row = WorkflowTaskCandidateIndex.Create(
            workflowTaskId: TaskId,
            clauseId: 3,
            combineMode: AssignmentCombineMode.Union,
            principalType: CandidatePrincipalType.Permission,
            principalRefId: "WF.Task.Process",
            tenantId: TenantId);

        row.WorkflowTaskId.Should().Be(TaskId);
        row.ClauseId.Should().Be(3);
        row.CombineMode.Should().Be(AssignmentCombineMode.Union);
        row.PrincipalType.Should().Be(CandidatePrincipalType.Permission);
        row.PrincipalRefId.Should().Be("WF.Task.Process");
        row.TenantId.Should().Be(TenantId);
    }

    [Fact]
    public void Create_RequiresPrincipalRefId()
    {
        var act = () => WorkflowTaskCandidateIndex.Create(
            TaskId, 1, AssignmentCombineMode.Union,
            CandidatePrincipalType.Group, principalRefId: "  ", tenantId: TenantId);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_RejectsEmptyTenant()
    {
        // Runtime read-model row MUST carry the real instance tenant (RT2-8) — never Guid.Empty.
        var act = () => WorkflowTaskCandidateIndex.Create(
            TaskId, 1, AssignmentCombineMode.Union,
            CandidatePrincipalType.Permission, "WF.Task.Process", tenantId: Guid.Empty);

        act.Should().Throw<ArgumentException>();
    }
}
