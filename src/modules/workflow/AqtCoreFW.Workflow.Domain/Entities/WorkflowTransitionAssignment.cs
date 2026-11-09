using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Domain.Entities;

/// <summary>
/// Per-transition handler-assignment rule (plan 260519-0421 v2, Model B).
///
/// RT2-9: SEPARATE entity (NOT EF owned-type) keyed by <see cref="WorkflowTransitionId"/>
/// so the resolver (P2) can query rules by transition without materializing the parent.
/// RT2-8: this is CONFIG of the workflow-definition graph — it inherits the definition's
/// tenancy (global TenantId=Guid.Empty or tenant-owned) and carries NO pol_*_Tenant RLS.
/// Tenant isolation lives only on the runtime WorkflowTask / WorkflowTaskCandidateIndex.
///
/// RT3-S4: UNRELATED to the pre-existing <see cref="WorkflowAssignmentRule"/>
/// (tenant+entityType → definition routing). Different concern; do not confuse/reuse.
///
/// T1 entity: non-generic Entity (long Id DB-assigned). DefinitionId/transition FKs are long.
/// </summary>
public sealed class WorkflowTransitionAssignment : Entity
{
    private readonly List<WorkflowTransitionAssignmentClause> _clauses = [];

    public long WorkflowTransitionId { get; private set; }

    /// <summary>Intra-assignment clause combination. Cross-transition is always Union (resolver).</summary>
    public AssignmentCombineMode CombineMode { get; private set; }

    /// <summary>
    /// Per-transition override-authz predicate (RT3-H1 — NET-NEW column, validated default FALSE).
    /// When false (or absent), an assignor's NextHandlerUserId override is denied and normal
    /// resolution proceeds (P3). Opt-in per transition; matches the fail-CLOSED posture.
    /// </summary>
    public bool AllowAssignorOverride { get; private set; }

    public IReadOnlyList<WorkflowTransitionAssignmentClause> Clauses => _clauses.AsReadOnly();

    private WorkflowTransitionAssignment() { } // EF Core

    public static WorkflowTransitionAssignment Create(
        long workflowTransitionId,
        AssignmentCombineMode combineMode = AssignmentCombineMode.Union,
        bool allowAssignorOverride = false)
    {
        if (workflowTransitionId <= 0)
            throw new ArgumentException(
                "WorkflowTransitionId must be a persisted positive id.", nameof(workflowTransitionId));

        return new WorkflowTransitionAssignment
        {
            WorkflowTransitionId = workflowTransitionId,
            CombineMode = combineMode,
            AllowAssignorOverride = allowAssignorOverride
        };
    }

    /// <summary>
    /// Appends a resolution clause. RefsJson is null for ref-less modes
    /// (Initiator / PreviousHandler); otherwise a JSON array of ids/codes.
    /// </summary>
    public WorkflowTransitionAssignmentClause AddClause(AssignmentClauseMode mode, string? refsJson)
    {
        var clause = WorkflowTransitionAssignmentClause.Create(mode, refsJson);
        _clauses.Add(clause);
        return clause;
    }
}
