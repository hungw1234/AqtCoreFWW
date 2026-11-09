using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Domain.Validation;

namespace AqtCoreFW.Workflow.Application.Validation;

/// <summary>
/// Pure-function workflow graph validator. No DB access, no external deps.
/// Two entry points per publish surface (both now run the full R1-R4 ruleset post Phase 03 plan 260523-1241):
///   <see cref="ValidateForSaveAsync"/>  — R1 + R2 + R3 + R4 against in-memory specs (Save handler, pre-persist).
///   <see cref="ValidateForActivateAsync"/> — R1-R4 against the persisted entity graph (Activate + BP Activate).
/// </summary>
public interface IWorkflowGraphValidator
{
    /// <summary>
    /// Pre-persist R1+R2+R3+R4 check against the in-memory graph (states + transition specs) built by
    /// SaveDefinitionGraphCommandHandler. Only human-actionable transitions (!IsRecall) count; AutoTimeout
    /// transitions are excluded.
    ///
    /// R1: every non-final state has ≥1 human-actionable outgoing spec.
    /// R2: every non-recall spec out of a non-final state has <see cref="NewTransitionSpec.Assignment"/> != null.
    /// R3: ByPermission clause codes exist in PermCodes.All (OrdinalIgnoreCase + Trim); validator
    ///     rewrites <see cref="AssignmentClauseSpec.RefsJson"/> in place to canonical (Trim + ToUpperInvariant)
    ///     so repo Phase 3 persists canonical form. Caller persists on happy path only (Option A).
    ///     ByGroup codes are validated against live Identity data scoped to <paramref name="tenantId"/>
    ///     (R-extended Q2: cross-tenant isolation per (Code, TenantId) UNIQUE-index semantics).
    /// R4: rejects <see cref="AssignmentCombineMode.Intersect"/> + NotSupported clause modes per Q6 strict-at-Save.
    ///
    /// Parallel-branch rules (RB1-RB4) validate the name-keyed <paramref name="branchSpecs"/> against
    /// the in-memory state-name set:
    ///   RB1: branching / continuation / child-target / resolution-resulting state names all exist.
    ///   RB2: NOfM ⇒ RequiredCount in [1, child count].
    ///   RB3: at most one branch per branching state (mirrors UX_…_BranchingStateId).
    ///   RB4: branching != continuation; ≥1 child.
    /// </summary>
    Task<WorkflowGraphValidationReport> ValidateForSaveAsync(
        IReadOnlyList<WorkflowState> states,
        IReadOnlyList<NewTransitionSpec> transitionSpecs,
        IReadOnlyList<NewBranchSpec> branchSpecs,
        Guid tenantId,
        CancellationToken ct = default);

    /// <summary>
    /// Full R1-R4 check on a persisted graph.
    /// R1: every non-final state has ≥1 human-actionable (!IsRecall, non-AutoTimeout) outgoing transition.
    /// R2: every outgoing human transition has an assignment rule (not null).
    /// R3: ByPermission clause codes exist in PermCodes.All (OrdinalIgnoreCase + Trim).
    ///     ByGroup codes: canonicalization only (no Q2 hop — codes were validated at Save time).
    /// R4: no NotSupported clause modes (PreviousHandler/ByRole/ByGroup/ByOrgUnit);
    ///     no Intersect combine mode.
    /// <paramref name="assignmentByTransitionId"/>: keyed by WorkflowTransition.Id;
    /// value = OutgoingAssignment (transition + its assignment rule, null if unconfigured).
    /// <paramref name="tenantId"/>: definition's home tenant. Passed for future use / defensive checks;
    /// no Q2 identity hop fires at Activate (pre-validated at Save).
    /// <paramref name="branches"/>: persisted parallel branches (children + assignment + clauses) of the
    /// definition. Null/empty ⇒ no parallel branches ⇒ R1 behaves exactly as before. When present:
    /// branching + fork-only child-target states are exempt from R1, and RB5 requires every child to carry
    /// a claimable pool (ByPermission/ByRole). Callers load these via
    /// <c>GetParallelBranchesForValidationAsync</c>.
    /// </summary>
    Task<WorkflowGraphValidationReport> ValidateForActivateAsync(
        IReadOnlyList<WorkflowState> states,
        IReadOnlyList<WorkflowTransition> transitions,
        IReadOnlyDictionary<long, OutgoingAssignment?> assignmentByTransitionId,
        Guid tenantId,
        IReadOnlyList<WorkflowParallelBranch>? branches = null,
        CancellationToken ct = default);
}
