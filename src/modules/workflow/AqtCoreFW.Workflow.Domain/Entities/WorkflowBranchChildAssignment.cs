using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Domain.Entities;

/// <summary>
/// Per-branch-child handler-assignment rule — the parallel-fork analogue of
/// <see cref="WorkflowTransitionAssignment"/>. A branch child has no outgoing transition, so it
/// carries its own resolution rule (clauses) keyed by <see cref="BranchChildId"/>; the fork fan-out
/// resolves these clauses into candidate predicates so the child task becomes pooled / claimable.
///
/// Mirrors the transition-assignment shape deliberately: the resolver consumes both through one
/// clause-shape code path (Mode + RefsJson). Config of the workflow-definition graph — inherits the
/// definition's tenancy (global TenantId=Guid.Empty or tenant-owned) and carries NO pol_*_Tenant RLS;
/// tenant isolation lives only on the runtime WorkflowTask / WorkflowTaskCandidateIndex.
///
/// T1 entity: non-generic Entity (long Id DB-assigned). BranchChildId is the FK (long).
/// </summary>
public sealed class WorkflowBranchChildAssignment : Entity
{
    private readonly List<WorkflowBranchChildAssignmentClause> _clauses = [];

    public long BranchChildId { get; private set; }

    /// <summary>Intra-assignment clause combination. Cross-rule is always Union (resolver).</summary>
    public AssignmentCombineMode CombineMode { get; private set; }

    /// <summary>
    /// Per-child override-authz predicate (parity with transition assignment, validated default FALSE).
    /// When false (or absent), an assignor's override of the resolved pool is denied. Opt-in per child;
    /// matches the fail-CLOSED posture. Consumed by the assign/reassign guard (later phase).
    /// </summary>
    public bool AllowAssignorOverride { get; private set; }

    public IReadOnlyList<WorkflowBranchChildAssignmentClause> Clauses => _clauses.AsReadOnly();

    private WorkflowBranchChildAssignment() { } // EF Core

    public static WorkflowBranchChildAssignment Create(
        long branchChildId,
        AssignmentCombineMode combineMode = AssignmentCombineMode.Union,
        bool allowAssignorOverride = false)
    {
        if (branchChildId < 0)
            throw new ArgumentException(
                "BranchChildId must be non-negative.", nameof(branchChildId));

        return new WorkflowBranchChildAssignment
        {
            BranchChildId = branchChildId,
            CombineMode = combineMode,
            AllowAssignorOverride = allowAssignorOverride
        };
    }

    /// <summary>
    /// Appends a resolution clause. RefsJson is null for ref-less modes (Initiator / PreviousHandler);
    /// otherwise a JSON array of ids/codes.
    /// </summary>
    public WorkflowBranchChildAssignmentClause AddClause(AssignmentClauseMode mode, string? refsJson)
    {
        var clause = WorkflowBranchChildAssignmentClause.Create(mode, refsJson);
        _clauses.Add(clause);
        return clause;
    }
}
