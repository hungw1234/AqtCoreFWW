using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Domain.Entities;

/// <summary>
/// One resolution clause of a <see cref="WorkflowTransitionAssignment"/>.
/// RefsJson semantics by Mode (string refs canonicalized Trim+ToUpperInvariant by the graph validator):
///   SpecificUser  → ["{userId:D}"]
///   ByPermission  → ["PERM.CODE", ...]
///   ByRole/Group/OrgUnit → ["CODE", ...]   (CODE, not id — e.g. "BTC_MINISTER"; all supported post-RT2-15)
///   Initiator / PreviousHandler → null     (ref-less)
/// T1 entity: non-generic Entity (long Id DB-assigned). AssignmentId is the FK (long).
/// </summary>
public sealed class WorkflowTransitionAssignmentClause : Entity
{
    public long AssignmentId { get; private set; }
    public AssignmentClauseMode Mode { get; private set; }
    public string? RefsJson { get; private set; }

    private WorkflowTransitionAssignmentClause() { } // EF Core

    internal static WorkflowTransitionAssignmentClause Create(AssignmentClauseMode mode, string? refsJson)
    {
        return new WorkflowTransitionAssignmentClause
        {
            Mode = mode,
            RefsJson = string.IsNullOrWhiteSpace(refsJson) ? null : refsJson
        };
    }

    /// <summary>
    /// Overwrites RefsJson with the canonical (trimmed + upper-cased) form produced by the graph
    /// validator. Called by WorkflowGraphValidator during R3 canonicalization writeback.
    /// Uses the same null-guard convention as Create: null/whitespace → null.
    /// </summary>
    public void SetCanonicalRefsJson(string? canonicalRefsJson)
    {
        RefsJson = string.IsNullOrWhiteSpace(canonicalRefsJson) ? null : canonicalRefsJson;
    }
}
