using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Domain.Entities;

/// <summary>
/// Write-once read-model row projecting ONE resolved candidate predicate of a pool
/// <see cref="WorkflowTask"/> (plan 260519-0421 v2, P1). Built by the resolver snapshot
/// (P2) and queried by the inbox (P4): "tasks I'm eligible for".
///
/// RT2-8: this IS tenant-scoped runtime data — carries the real instance TenantId and
/// gets a pol_*_Tenant RLS policy. <see cref="TenantId"/> is ALSO a mandatory app-level
/// match column (defense-in-depth; low-cardinality Group/OrgUnit refs collide across
/// tenants, so RLS alone is insufficient — v1-F7).
/// T1 entity: non-generic Entity (long Id DB-assigned).
/// </summary>
public sealed class WorkflowTaskCandidateIndex : Entity, ITenantScoped
{
    public long WorkflowTaskId { get; private set; }

    /// <summary>
    /// One-way nav-prop to parent WorkflowTask (DCAND-a, plan 260521-1008).
    /// Child→parent direction only — NO reverse ICollection on WorkflowTask (preserves
    /// rowversion safety invariant; see WorkflowTask.cs:43-48 remarks on bug-33d4973f).
    /// Phase 4 Inbox EXISTS query uses this nav to join without JSON-only fallback.
    /// </summary>
    public WorkflowTask? WorkflowTask { get; private set; }

    /// <summary>Source clause id (resolver provenance; supports future Intersect anti-join).</summary>
    public int ClauseId { get; private set; }

    public AssignmentCombineMode CombineMode { get; private set; }
    public CandidatePrincipalType PrincipalType { get; private set; }

    /// <summary>Canonical principal id/code (P2 CandidatePrincipalSerializer). Max 64 chars.</summary>
    public string PrincipalRefId { get; private set; } = string.Empty;

    public Guid TenantId { get; private set; }

    private WorkflowTaskCandidateIndex() { } // EF Core

    public static WorkflowTaskCandidateIndex Create(
        long workflowTaskId,
        int clauseId,
        AssignmentCombineMode combineMode,
        CandidatePrincipalType principalType,
        string principalRefId,
        Guid tenantId)
    {
        if (workflowTaskId <= 0)
            throw new ArgumentException(
                "WorkflowTaskId must be a persisted positive id.", nameof(workflowTaskId));
        if (string.IsNullOrWhiteSpace(principalRefId))
            throw new ArgumentException("PrincipalRefId is required.", nameof(principalRefId));
        if (tenantId == Guid.Empty)
            throw new ArgumentException(
                "Runtime candidate-index row must carry the real instance tenant, not Guid.Empty.",
                nameof(tenantId));

        return new WorkflowTaskCandidateIndex
        {
            WorkflowTaskId = workflowTaskId,
            ClauseId = clauseId,
            CombineMode = combineMode,
            PrincipalType = principalType,
            PrincipalRefId = principalRefId.Trim(),
            TenantId = tenantId
        };
    }

    /// <summary>
    /// Staging factory for use BEFORE <see cref="WorkflowTask"/> is persisted (Id=0 until flush).
    /// Sets <see cref="WorkflowTask"/> nav-prop rather than <see cref="WorkflowTaskId"/> so that
    /// EF relationship-fixup propagates the DB IDENTITY-generated parent Id to the FK column
    /// at SaveChanges time. Both the parent task and this row MUST be in the same SaveChanges
    /// (S7 single-SaveChanges invariant) — never persist a row created here without the parent
    /// task in the same flush boundary.
    /// Applies identical guards and <c>.Trim()</c> canonicalization as <see cref="Create"/>.
    /// </summary>
    public static WorkflowTaskCandidateIndex CreateForStaging(
        WorkflowTask task,
        int clauseId,
        AssignmentCombineMode combineMode,
        CandidatePrincipalType principalType,
        string principalRefId,
        Guid tenantId)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (string.IsNullOrWhiteSpace(principalRefId))
            throw new ArgumentException("PrincipalRefId is required.", nameof(principalRefId));
        if (tenantId == Guid.Empty)
            throw new ArgumentException(
                "Runtime candidate-index row must carry the real instance tenant, not Guid.Empty.",
                nameof(tenantId));

        return new WorkflowTaskCandidateIndex
        {
            WorkflowTask = task,
            ClauseId = clauseId,
            CombineMode = combineMode,
            PrincipalType = principalType,
            PrincipalRefId = principalRefId.Trim(),
            TenantId = tenantId
        };
    }
}
