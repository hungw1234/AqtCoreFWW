using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Domain.Entities;

/// <summary>
/// One resolution clause of a <see cref="WorkflowBranchChildAssignment"/> — the parallel-fork analogue
/// of <see cref="WorkflowTransitionAssignmentClause"/>. RefsJson semantics by Mode:
///   SpecificUser  → ["{userId:D}"]
///   ByPermission  → ["PERM.CODE", ...]
///   ByRole/Group/OrgUnit → ["CODE", ...]   (CODE, not id — e.g. "BTC_MINISTER")
///   Initiator / PreviousHandler → null     (ref-less)
///
/// CANONICAL-AT-REST: the graph validator canonicalizes branch-child clauses at Save (Trim +
/// ToUpperInvariant via SetCanonicalRefsJson) and restricts the mode to the read-side-claimable set
/// (ByPermission / ByRole); other modes are rejected before persist. Consumers may rely on canonical
/// codes at rest (the resolver pool matches canonical refs).
///
/// T1 entity: non-generic Entity (long Id DB-assigned). AssignmentId is the FK (long).
/// </summary>
public sealed class WorkflowBranchChildAssignmentClause : Entity
{
    public long AssignmentId { get; private set; }
    public AssignmentClauseMode Mode { get; private set; }
    public string? RefsJson { get; private set; }

    private WorkflowBranchChildAssignmentClause() { } // EF Core

    internal static WorkflowBranchChildAssignmentClause Create(AssignmentClauseMode mode, string? refsJson)
    {
        return new WorkflowBranchChildAssignmentClause
        {
            Mode = mode,
            RefsJson = string.IsNullOrWhiteSpace(refsJson) ? null : refsJson
        };
    }

    /// <summary>
    /// Overwrites RefsJson with the canonical (trimmed + upper-cased) form produced by the graph
    /// validator. Uses the same null-guard convention as Create: null/whitespace → null.
    /// </summary>
    public void SetCanonicalRefsJson(string? canonicalRefsJson)
    {
        RefsJson = string.IsNullOrWhiteSpace(canonicalRefsJson) ? null : canonicalRefsJson;
    }
}
