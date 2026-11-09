using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Workflow.Domain.Entities;

/// <summary>
/// Represents a single sub-branch within a WorkflowParallelBranch definition.
/// Each child targets a specific state and may have per-resolution routing.
/// T1 entity: non-generic Entity (long Id DB-assigned).
///   ParallelBranchId: long, TargetStateId: long (FKs to T1 entities).
///   DefaultAssigneeId: Guid? (FK exception — user ref).
/// </summary>
public sealed class WorkflowBranchChild : Entity
{
    private readonly List<WorkflowBranchResolution> _resolutions = [];

    public long ParallelBranchId { get; private set; }

    /// <summary>The workflow state this child branch routes to.</summary>
    public long TargetStateId { get; private set; }

    /// <summary>Machine name for the branch (e.g. "legal-review").</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Vietnamese display label shown in UI.</summary>
    public string DisplayNameVi { get; private set; } = string.Empty;

    /// <summary>Optional department scope for assignment resolution.</summary>
    public string? DepartmentId { get; private set; }

    /// <summary>Default user to assign approval tasks for this branch child. Null = unassigned.</summary>
    public Guid? DefaultAssigneeId { get; private set; }

    public int SortOrder { get; private set; }

    /// <summary>
    /// Optional per-child handler-assignment rule (clauses ByRole/ByPermission/...). When set, the fork
    /// fan-out resolves it into candidate predicates so this child's task becomes pooled / claimable.
    /// Null = no pool rule (falls back to <see cref="DefaultAssigneeId"/> if present).
    /// </summary>
    public WorkflowBranchChildAssignment? Assignment { get; private set; }

    public IReadOnlyList<WorkflowBranchResolution> Resolutions => _resolutions.AsReadOnly();

    private WorkflowBranchChild() { } // EF Core

    public static WorkflowBranchChild Create(
        long parallelBranchId,
        long targetStateId,
        string name,
        string displayNameVi,
        string? departmentId,
        int sortOrder,
        Guid? defaultAssigneeId = null)
    {
        return new WorkflowBranchChild
        {
            ParallelBranchId = parallelBranchId,
            TargetStateId = targetStateId,
            Name = name,
            DisplayNameVi = displayNameVi,
            DepartmentId = departmentId,
            DefaultAssigneeId = defaultAssigneeId,
            SortOrder = sortOrder
        };
    }

    /// <summary>Updates the default assignee for this branch child.</summary>
    public void SetDefaultAssignee(Guid? assigneeId) => DefaultAssigneeId = assigneeId;

    /// <summary>Attaches (or replaces) the per-child handler-assignment rule.</summary>
    public void SetAssignment(WorkflowBranchChildAssignment? assignment) => Assignment = assignment;

    public void AddResolution(WorkflowBranchResolution resolution) => _resolutions.Add(resolution);
}
