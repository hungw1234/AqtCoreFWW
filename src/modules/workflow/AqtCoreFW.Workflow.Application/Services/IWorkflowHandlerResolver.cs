using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Application.Services;

/// <summary>
/// One outgoing transition of the target state paired with its assignment rule
/// (null = no rule configured for that transition). The caller (P3) loads the
/// assignment with its clauses eagerly (EF <c>.Include(a =&gt; a.Clauses)</c>) —
/// see phase-02 addendum §1: P1 config is field-access with no lazy proxies, so an
/// un-Included <c>Clauses</c> is silently empty.
/// </summary>
public sealed record OutgoingAssignment(
    WorkflowTransition Transition,
    WorkflowTransitionAssignment? Assignment);

/// <summary>
/// Instance-scoped resolution context. <see cref="TenantId"/> is the running
/// instance's tenant (RT2-8 — assignment config is untenanted; tenancy comes from
/// the instance). <see cref="InitiatorUserId"/> is <c>WorkflowInstance.CreatedBy</c>
/// for the <c>Initiator</c> clause mode. <see cref="WorkflowInstanceId"/> is the
/// internal long-PK of <c>WorkflowInstance</c> — used by clause mode
/// <c>PreviousHandler</c> (RT2-15 Phase 01) to look up the last completed task on
/// the instance. Default <c>0</c> means "no instance context" (the producer treats
/// this as an empty pool — falls through to fail-CLOSED gate).
/// </summary>
public sealed record WorkflowResolutionContext(
    Guid TenantId,
    Guid InitiatorUserId,
    long WorkflowInstanceId = 0L);

/// <summary>
/// Pure (zero-dependency, Workflow-module-only) Model B handler resolver:
/// <c>Pool(targetState) = ⋃ resolved rules of its outgoing transitions</c>, excluding
/// the auto-timeout transition and any recall transition. Performs NO data access —
/// the caller pre-loads the outgoing/assignment graph (phase-02 addendum §1).
/// </summary>
public interface IWorkflowHandlerResolver
{
    /// <param name="targetState">State being entered; its <c>AutoTransitionOnTimeoutId</c> is excluded from the pool.</param>
    /// <param name="outgoing">Outgoing transitions of <paramref name="targetState"/> + their assignment rules.</param>
    /// <param name="context">Instance tenant + initiator.</param>
    /// <exception cref="NotSupportedException">
    /// A deferred clause mode (PreviousHandler/ByRole/ByGroup/ByOrgUnit), Intersect combine,
    /// or a multi-user / mixed user+predicate result that has no MVP producer (addendum §3).
    /// </exception>
    Task<HandlerResolution> ResolveAsync(
        WorkflowState targetState,
        IReadOnlyList<OutgoingAssignment> outgoing,
        WorkflowResolutionContext context,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves a SINGLE assignment's clauses → a <see cref="HandlerResolution"/>, decoupled from any
    /// concrete clause entity (caller projects its clauses to <c>(Mode, RefsJson)</c>). This is the entry
    /// point for a parallel-branch child (no outgoing transition of its own): the fork fan-out resolves
    /// each child's clauses into the candidate pool. Shares the exact clause-mode semantics of
    /// <see cref="ResolveAsync"/>.
    /// </summary>
    /// <param name="clauses">The assignment's clauses as (mode, refsJson) shapes, in author order.</param>
    /// <param name="combineMode">The assignment's combine mode (Intersect is not supported — throws).</param>
    /// <param name="context">Instance tenant + initiator + instance id (for PreviousHandler).</param>
    /// <exception cref="NotSupportedException">
    /// Intersect combine, an unknown clause mode, or a multi-user / mixed user+predicate result that has
    /// no MVP producer (addendum §3) — identical posture to <see cref="ResolveAsync"/>.
    /// </exception>
    Task<HandlerResolution> ResolveAssignmentAsync(
        IReadOnlyList<(AssignmentClauseMode Mode, string? RefsJson)> clauses,
        AssignmentCombineMode combineMode,
        WorkflowResolutionContext context,
        CancellationToken ct = default);
}
