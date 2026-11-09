using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Domain.Validation;
using System.Text.Json;

namespace AqtCoreFW.Workflow.Application.Validation;

/// <summary>
/// Workflow graph validator (Phase 5c). Applies R1-R4 rules at Save and Activate time.
/// PermCodes.All lookup: static lazy HashSet constructed once per process.
/// <para>
/// RT2-15 Phase 02: now depends on <see cref="IIdentityModuleClient"/> for Q2 Save-time
/// ByGroup code existence check (<c>ResolveActiveUserGroupsByCodesAsync</c>). The validator
/// is therefore <c>AddScoped</c> (not Singleton) from this phase onward.
/// </para>
/// <para>
/// RT2-15 Phase 04: ByOrgUnit validation is structural canonicalization only (Trim+ToUpperInvariant).
/// No Q2 OrgUnit hop at Save time — code existence is enforced at runtime via predicate-pool
/// (resolver calls <c>GetUserOrgUnitCodesAsync</c>; empty result → fail-CLOSED 422).
/// Mirrors ByRole structural-only pattern (role codes are stable ASCII; existence not checked at Save).
/// </para>
/// </summary>
public sealed class WorkflowGraphValidator(
    IIdentityModuleClient identityClient) : IWorkflowGraphValidator
{
    private readonly IIdentityModuleClient _identityClient = identityClient;

    // Static lazy HashSet for O(1) ByPermission code lookup.
    // OrdinalIgnoreCase so "workflow.task.process" matches "WORKFLOW.TASK.PROCESS".
    private static readonly HashSet<string> _permCodeSet = new(
        PermCodes.All, StringComparer.OrdinalIgnoreCase);

    // NotSupported clause modes per RT2-15 (deferred until FE config-UI producer exists).
    // PreviousHandler (mode 2) shipped 2026-05-24 in Phase 01 — lifted from this set.
    // ByRole (mode 4) shipped 2026-05-24 in Phase 03 (B1 partial) — read-side claim/inbox
    // extension shipped 2026-05-25 v2.137p110 (T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION).
    // ByGroup (mode 5) shipped 2026-05-25 in Phase 02 — Save Q2 validation + predicate-pool
    // producer. Read-side gap closed 2026-05-25 v2.137p113 (T3-WF-CANDIDATEINDEX-READSIDE-EXT-GROUP).
    // ByOrgUnit (mode 6) shipped 2026-05-26 in Phase 04 — temporal assignment producer + read-side.
    // All clause modes now supported; set is empty.
    private static readonly HashSet<AssignmentClauseMode> _notSupportedModes = [];

    /// <inheritdoc/>
    public async Task<WorkflowGraphValidationReport> ValidateForSaveAsync(
        IReadOnlyList<WorkflowState> states,
        IReadOnlyList<NewTransitionSpec> transitionSpecs,
        IReadOnlyList<NewBranchSpec> branchSpecs,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var violations = new List<GraphRuleViolation>();

        // R1: every non-final state must have ≥1 human-actionable outgoing transition.
        // In the Save (pre-persist) path, "human-actionable" = !IsRecall.
        // AutoTimeout transitions exist only after IDs are assigned (they reference a transition ID);
        // at Save time they are name-keyed specs — filter by IsRecall only.
        var stateByName = states.ToDictionary(s => s.Name, StringComparer.Ordinal);

        // Build per-state outgoing count of human-actionable transitions using specs (name-keyed).
        var humanOutgoing = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var state in states)
            humanOutgoing[state.Name] = 0;

        foreach (var spec in transitionSpecs)
        {
            if (!spec.IsRecall)
            {
                if (humanOutgoing.ContainsKey(spec.FromStateName))
                    humanOutgoing[spec.FromStateName]++;
            }
        }

        // Branch-aware R1 exemptions, name-keyed (symmetric with the Activate path so a parallel def the
        // Activate validator accepts can also be authored through Save). Empty when no branchSpecs.
        //  - A BRANCHING state's only outgoing is the fork → always exempt (it cannot mask a dead-end:
        //    its outgoing IS the fork, never a normal transition).
        //  - A CHILD-TARGET state exits via its branch resolutions, not a transition → exempt, BUT only
        //    when not also a normal (non-recall) transition target (else a normal-flow dead-end is masked).
        //  - The CONTINUATION state is NOT exempt — it still needs an outgoing Close transition.
        var branchingStateNames = branchSpecs.Select(b => b.BranchingStateName).ToHashSet(StringComparer.Ordinal);
        var childTargetStateNames = branchSpecs
            .SelectMany(b => b.Children ?? [])
            .Select(c => c.TargetStateName)
            .ToHashSet(StringComparer.Ordinal);
        var normalToStateNames = transitionSpecs
            .Where(s => !s.IsRecall)
            .Select(s => s.ToStateName)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var state in states)
        {
            if (state.IsFinal) continue; // Final states are exempt from R1.
            if (!humanOutgoing.TryGetValue(state.Name, out var count) || count == 0)
            {
                if (branchingStateNames.Contains(state.Name))
                    continue;
                if (childTargetStateNames.Contains(state.Name) && !normalToStateNames.Contains(state.Name))
                    continue;

                violations.Add(new GraphRuleViolation(
                    Rule: "R1",
                    StateName: state.Name,
                    TransitionActionName: null,
                    Detail: $"Non-final state '{state.Name}' has no human-actionable outgoing transition."));
            }
        }

        // Phase 03 (plan 260523-1241) — R2 + R3 + R4 against spec.Assignment per Q1/Q6 strict-at-Save.
        // R2: every non-recall transition out of a non-final state must carry an AssignmentSpec.
        // R3: ByPermission codes canonicalize (Trim + ToUpperInvariant); unknown codes → violation.
        // R4: Intersect combine mode + NotSupported clause modes rejected (RT2-15 / Q6).
        //
        // RT2-15 Phase 02 Q2: collect all ByGroup codes across the graph for a single batched
        // ResolveActiveUserGroupsByCodesAsync call (one round-trip regardless of clause count).
        var byGroupCodesByClause = new List<(AssignmentClauseSpec Clause, string FromStateName, string ActionName, List<string> Codes)>();
        foreach (var spec in transitionSpecs)
        {
            if (spec.IsRecall) continue; // R2-R4 apply to human-actionable only.

            // Skip if FromState is final (defensive — R1 layer already flags missing outgoing on non-finals).
            if (stateByName.TryGetValue(spec.FromStateName, out var fromState) && fromState.IsFinal)
                continue;

            if (spec.Assignment is null)
            {
                violations.Add(new GraphRuleViolation(
                    Rule: "R2",
                    StateName: spec.FromStateName,
                    TransitionActionName: spec.ActionName,
                    Detail: $"Transition '{spec.ActionName}' from state '{spec.FromStateName}' has no assignment rule."));
                continue; // Can't check R3/R4 without an assignment payload.
            }

            // R4: Intersect combine mode is NotSupported at MVP (RT2-15).
            if (spec.Assignment.CombineMode == AssignmentCombineMode.Intersect)
            {
                violations.Add(new GraphRuleViolation(
                    Rule: "R4",
                    StateName: spec.FromStateName,
                    TransitionActionName: spec.ActionName,
                    Detail: $"Transition '{spec.ActionName}': CombineMode=Intersect is not supported at MVP (RT2-15)."));
            }

            // R3 + R4 per-clause checks.
            foreach (var clause in spec.Assignment.Clauses)
            {
                // R4: NotSupported clause modes.
                if (_notSupportedModes.Contains(clause.Mode))
                {
                    violations.Add(new GraphRuleViolation(
                        Rule: "R4",
                        StateName: spec.FromStateName,
                        TransitionActionName: spec.ActionName,
                        Detail: $"Transition '{spec.ActionName}': clause mode '{clause.Mode}' is not supported at MVP (RT2-15)."));
                    continue;
                }

                // R3: ByPermission codes must exist in PermCodes.All + canonicalize spec in place.
                if (clause.Mode == AssignmentClauseMode.ByPermission && clause.RefsJson is not null)
                {
                    CheckByPermissionCodesAndCanonicalize(clause, spec.FromStateName, spec.ActionName, violations);
                }

                // ByRole role codes canonicalize Trim+ToUpperInvariant Save-time (codes are stable
                // ASCII, e.g. "BTC_MINISTER"; structural-only). Mirror ByPermission's writeback + fail-loud semantic.
                if (clause.Mode == AssignmentClauseMode.ByRole && clause.RefsJson is not null)
                {
                    CanonicalizeRoleCodesInPlace(clause, spec.FromStateName, spec.ActionName, violations);
                }

                // RT2-15 Phase 02: ByGroup codes canonicalize Trim+ToUpperInvariant Save-time.
                // Codes are collected for a single batched Q2 identity hop below (after R1-R4 sync pass).
                if (clause.Mode == AssignmentClauseMode.ByGroup && clause.RefsJson is not null)
                {
                    var canonicalCodes = CanonicalizeGroupCodesInPlace(clause, spec.FromStateName, spec.ActionName, violations);
                    if (canonicalCodes is { Count: > 0 })
                        byGroupCodesByClause.Add((clause, spec.FromStateName, spec.ActionName, canonicalCodes));
                }

                // RT2-15 Phase 04: ByOrgUnit codes canonicalize Trim+ToUpperInvariant Save-time.
                // Structural only — no Q2 existence hop (mirrors ByRole pattern).
                if (clause.Mode == AssignmentClauseMode.ByOrgUnit && clause.RefsJson is not null)
                {
                    CanonicalizeOrgUnitCodesInPlace(clause, spec.FromStateName, spec.ActionName, violations);
                }
            }
        }

        // R-extended Q2 (RT2-15 Phase 02): single batched identity hop after all sync R1-R4 checks.
        // Validates ByGroup codes against live Identity data (active groups only).
        // Returns subset of inputs that resolved → set-difference = unknown codes.
        // Per 260522-E: Metadata carries ONLY unknown codes (not known ones — anti-tautology).
        if (byGroupCodesByClause.Count > 0)
        {
            // Deduplicate across all clauses for a single round-trip.
            var allCodes = byGroupCodesByClause
                .SelectMany(x => x.Codes)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var resolved = await _identityClient.ResolveActiveUserGroupsByCodesAsync(allCodes, tenantId, ct);

            foreach (var (clause, fromStateName, actionName, codes) in byGroupCodesByClause)
            {
                var unknown = codes.Where(c => !resolved.Contains(c)).ToList();
                if (unknown.Count > 0)
                {
                    violations.Add(new GraphRuleViolation(
                        Rule: "R3",
                        StateName: fromStateName,
                        TransitionActionName: actionName,
                        Detail: $"Transition '{actionName}': ByGroup unknown or inactive group code(s): {string.Join(", ", unknown)}.",
                        Metadata: new Dictionary<string, object>
                        {
                            ["UnknownGroupCodes"] = (IReadOnlyList<string>)unknown,
                        }));
                }
            }
        }

        // Parallel-branch authoring rules (RB1-RB4). Pure, name-keyed — validate against the same
        // in-memory state-name set, before the replace-all persists anything.
        ValidateBranchSpecs(branchSpecs, stateByName, violations);

        var report = violations.Count == 0
            ? WorkflowGraphValidationReport.Ok()
            : WorkflowGraphValidationReport.Fail(violations);

        return report;
    }

    /// <summary>
    /// RB1-RB4 for the name-keyed parallel-branch specs. Emits one <see cref="GraphRuleViolation"/>
    /// per breach, anchored to the branching state name so the FE canvas can highlight the fork.
    /// </summary>
    private static void ValidateBranchSpecs(
        IReadOnlyList<NewBranchSpec> branchSpecs,
        IReadOnlyDictionary<string, WorkflowState> stateByName,
        List<GraphRuleViolation> violations)
    {
        var seenBranchingStates = new HashSet<string>(StringComparer.Ordinal);

        foreach (var branch in branchSpecs)
        {
            var anchor = branch.BranchingStateName;

            // RB3: at most one branch per branching state (mirrors UX_…_BranchingStateId; pre-guard
            // gives a clean 422 instead of a DB unique-index 500 on re-save).
            if (!seenBranchingStates.Add(branch.BranchingStateName))
            {
                violations.Add(new GraphRuleViolation(
                    Rule: "RB3",
                    StateName: anchor,
                    TransitionActionName: null,
                    Detail: $"State '{branch.BranchingStateName}' has more than one parallel branch (only one fork per state)."));
            }

            // RB1: branching + continuation state names must exist.
            if (!stateByName.ContainsKey(branch.BranchingStateName))
                violations.Add(BranchViolation("RB1", anchor,
                    $"Branch references unknown branching state '{branch.BranchingStateName}'."));
            if (!stateByName.ContainsKey(branch.ContinuationStateName))
                violations.Add(BranchViolation("RB1", anchor,
                    $"Branch references unknown continuation state '{branch.ContinuationStateName}'."));

            // RB4: branching != continuation; ≥1 child.
            if (string.Equals(branch.BranchingStateName, branch.ContinuationStateName, StringComparison.Ordinal))
                violations.Add(BranchViolation("RB4", anchor,
                    $"Branch on '{branch.BranchingStateName}' must continue to a different state."));
            var childCount = branch.Children?.Count ?? 0;
            if (childCount == 0)
                violations.Add(BranchViolation("RB4", anchor,
                    $"Branch on '{branch.BranchingStateName}' must have at least one child."));

            // RB2: NOfM ⇒ RequiredCount in [1, child count].
            if (branch.BranchType == ParallelBranchType.NOfM)
            {
                if (branch.RequiredCount is not > 0)
                    violations.Add(BranchViolation("RB2", anchor,
                        $"Branch on '{branch.BranchingStateName}' is NOfM but RequiredCount is not a positive number."));
                else if (childCount > 0 && branch.RequiredCount > childCount)
                    violations.Add(BranchViolation("RB2", anchor,
                        $"Branch on '{branch.BranchingStateName}' has RequiredCount {branch.RequiredCount} greater than its {childCount} child branch(es)."));
            }

            // RB1: each child target + resolution resulting state name must exist.
            foreach (var child in branch.Children ?? [])
            {
                if (!stateByName.ContainsKey(child.TargetStateName))
                    violations.Add(BranchViolation("RB1", anchor,
                        $"Branch child '{child.Name}' references unknown target state '{child.TargetStateName}'."));

                foreach (var resolution in child.Resolutions ?? [])
                {
                    if (resolution.ResultingStateName is not null
                        && !stateByName.ContainsKey(resolution.ResultingStateName))
                    {
                        violations.Add(BranchViolation("RB1", anchor,
                            $"Branch child '{child.Name}' resolution references unknown resulting state '{resolution.ResultingStateName}'."));
                    }
                }

                // RB5: validate + canonicalize this child's assignment pool (mode restriction).
                ValidateBranchChildAssignmentForSave(child, anchor, violations);
            }
        }
    }

    private static GraphRuleViolation BranchViolation(string rule, string stateName, string detail) =>
        new(Rule: rule, StateName: stateName, TransitionActionName: null, Detail: detail);

    /// <summary>
    /// RB5 (Save): a branch child's handler pool is restricted to the read-side-claimable clause modes
    /// (ByPermission, ByRole) and canonicalized in place. ByGroup/ByOrgUnit have no shipped claim/inbox
    /// read-side, and SpecificUser/Initiator/PreviousHandler do not form a claimable fan-out pool — any
    /// of these would strand the forked child task (an AND-join would never complete). A child WITHOUT an
    /// assignment is allowed at Save (draft authoring); presence is enforced at Activate.
    /// </summary>
    private static void ValidateBranchChildAssignmentForSave(
        NewBranchChildSpec child,
        string anchor,
        List<GraphRuleViolation> violations)
    {
        var assignment = child.Assignment;
        if (assignment is null) return;

        // Intersect has no resolver producer (mirrors transition R4) — reject up front.
        if (assignment.CombineMode == AssignmentCombineMode.Intersect)
            violations.Add(BranchViolation("RB5", anchor,
                $"Branch child '{child.Name}': CombineMode=Intersect is not supported."));

        foreach (var clause in assignment.Clauses)
        {
            if (clause.Mode is not (AssignmentClauseMode.ByPermission or AssignmentClauseMode.ByRole))
            {
                violations.Add(BranchViolation("RB5", anchor,
                    $"Branch child '{child.Name}': clause mode '{clause.Mode}' is not a claimable parallel-branch pool (only ByPermission and ByRole are read-side claimable)."));
                continue;
            }

            // Reuse the transition canonicalizers (same AssignmentClauseSpec shape) — Trim+ToUpperInvariant
            // writeback + ByPermission existence check. Persisted canonical so the resolver reads canonical-at-rest.
            if (clause.Mode == AssignmentClauseMode.ByPermission && clause.RefsJson is not null)
                CheckByPermissionCodesAndCanonicalize(clause, anchor, child.Name, violations);
            else if (clause.Mode == AssignmentClauseMode.ByRole && clause.RefsJson is not null)
                CanonicalizeRoleCodesInPlace(clause, anchor, child.Name, violations);
        }
    }

    /// <inheritdoc/>
    public async Task<WorkflowGraphValidationReport> ValidateForActivateAsync(
        IReadOnlyList<WorkflowState> states,
        IReadOnlyList<WorkflowTransition> transitions,
        IReadOnlyDictionary<long, OutgoingAssignment?> assignmentByTransitionId,
        Guid tenantId,
        IReadOnlyList<WorkflowParallelBranch>? branches = null,
        CancellationToken ct = default)
    {
        var violations = new List<GraphRuleViolation>();
        var branchList = branches ?? [];

        // Index transitions by FromStateId for fast per-state lookup.
        var transitionsByFromState = transitions
            .GroupBy(t => t.FromStateId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Branch-aware R1 exemptions (all empty when the definition has no parallel branches → behaves
        // identically to the pre-branch validator):
        //  - A BRANCHING state's only "outgoing" is the fork (a WorkflowParallelBranch, not a transition),
        //    so it can never satisfy R1 — always exempt.
        //  - A CHILD-TARGET state hosts the fork's pooled work, so it needs no human transition of its own,
        //    BUT only when reachable EXCLUSIVELY via the fork. If a normal (non-recall) transition also
        //    targets it, the normal-flow path would dead-end there, so R1 must still fire (do not mask it).
        //  - The CONTINUATION state is deliberately NOT exempt — it still needs an outgoing Close transition.
        var branchingStateIds = branchList.Select(b => b.BranchingStateId).ToHashSet();
        var childTargetStateIds = branchList.SelectMany(b => b.Children).Select(c => c.TargetStateId).ToHashSet();
        var normalToStateIds = transitions.Where(t => !t.IsRecall).Select(t => t.ToStateId).ToHashSet();

        foreach (var state in states)
        {
            if (state.IsFinal) continue; // Final states exempt from all handler rules.

            var outgoing = transitionsByFromState.TryGetValue(state.Id, out var list) ? list : [];

            // Human-actionable: !IsRecall and NOT the AutoTimeout transition.
            // AutoTimeout transition is identified by state.AutoTransitionOnTimeoutId — exclude it.
            var humanTransitions = outgoing
                .Where(t => !t.IsRecall && t.Id != state.AutoTransitionOnTimeoutId)
                .ToList();

            // R1: ≥1 human-actionable outgoing transition required.
            if (humanTransitions.Count == 0)
            {
                // Branch-aware exemptions only matter when the state has no human transition.
                if (branchingStateIds.Contains(state.Id))
                    continue;
                if (childTargetStateIds.Contains(state.Id) && !normalToStateIds.Contains(state.Id))
                    continue;

                violations.Add(new GraphRuleViolation(
                    Rule: "R1",
                    StateName: state.Name,
                    TransitionActionName: null,
                    Detail: $"Non-final state '{state.Name}' has no human-actionable outgoing transition."));
                // Continue to check remaining states; R2/R3/R4 need an assignment so skip if R1 failed.
                continue;
            }

            // R2, R3, R4: check each human transition's assignment.
            foreach (var transition in humanTransitions)
            {
                assignmentByTransitionId.TryGetValue(transition.Id, out var outgoingAssignment);
                var assignment = outgoingAssignment?.Assignment;

                // R2: assignment must exist for every non-final-state outgoing human transition.
                if (assignment is null)
                {
                    violations.Add(new GraphRuleViolation(
                        Rule: "R2",
                        StateName: state.Name,
                        TransitionActionName: transition.ActionName,
                        Detail: $"Transition '{transition.ActionName}' from state '{state.Name}' has no assignment rule."));
                    continue; // Can't check R3/R4 without an assignment.
                }

                // R4: Intersect combine mode is NotSupported at MVP.
                if (assignment.CombineMode == AssignmentCombineMode.Intersect)
                {
                    violations.Add(new GraphRuleViolation(
                        Rule: "R4",
                        StateName: state.Name,
                        TransitionActionName: transition.ActionName,
                        Detail: $"Transition '{transition.ActionName}': CombineMode=Intersect is not supported at MVP (RT2-15)."));
                }

                // R3 + R4 per-clause checks.
                foreach (var clause in assignment.Clauses)
                {
                    // R4: NotSupported clause modes.
                    if (_notSupportedModes.Contains(clause.Mode))
                    {
                        violations.Add(new GraphRuleViolation(
                            Rule: "R4",
                            StateName: state.Name,
                            TransitionActionName: transition.ActionName,
                            Detail: $"Transition '{transition.ActionName}': clause mode '{clause.Mode}' is not supported at MVP (RT2-15)."));
                        continue;
                    }

                    // R3: ByPermission codes must exist in PermCodes.All.
                    // Canonicalization (Trim + ToUpperInvariant) runs unconditionally for valid codes;
                    // RefsJson is rewritten in-memory via SetCanonicalRefsJson when needed.
                    // Caller persists via SaveChanges ONLY on the happy path (Option A).
                    if (clause.Mode == AssignmentClauseMode.ByPermission && clause.RefsJson is not null)
                    {
                        CheckByPermissionCodesAndCanonicalize(clause, state.Name, transition.ActionName, violations);
                    }

                    // ByRole role codes canonicalize Trim+ToUpperInvariant at Activate.
                    // No PermCodes.All-style static lookup; structural-only (codes are stable ASCII).
                    if (clause.Mode == AssignmentClauseMode.ByRole && clause.RefsJson is not null)
                    {
                        CanonicalizeRoleCodesInPlace(clause, state.Name, transition.ActionName, violations);
                    }

                    // RT2-15 Phase 02: ByGroup codes canonicalize Trim+ToUpperInvariant at Activate.
                    // No Q2 identity hop here — codes were already validated at Save time.
                    // Canonicalization on Activate is defensive normalization only (persisted already canonical).
                    if (clause.Mode == AssignmentClauseMode.ByGroup && clause.RefsJson is not null)
                    {
                        CanonicalizeGroupCodesInPlace(clause, state.Name, transition.ActionName, violations);
                    }

                    // RT2-15 Phase 04: ByOrgUnit codes canonicalize Trim+ToUpperInvariant at Activate.
                    // Mirror ByGroup Activate-time handling — structural canonicalization only.
                    if (clause.Mode == AssignmentClauseMode.ByOrgUnit && clause.RefsJson is not null)
                    {
                        CanonicalizeOrgUnitCodesInPlace(clause, state.Name, transition.ActionName, violations);
                    }
                }
            }
        }

        // RB5 (Activate): every branch child must carry a resolvable, claimable pool. Without it the
        // fork fan-out resolves no candidate and an AND-join would never complete (the def must not
        // activate). No canonicalization here — Save already persisted canonical clauses, and no
        // pre-existing parallel def has ever been activatable to leave non-canonical data at rest.
        ValidateBranchChildPoolsForActivate(branchList, states, violations);

        // RB5 live-claimant hop: a branch child whose pool is EXCLUSIVELY ByRole and resolves to zero
        // active users would stage an unclaimable task at fork time → its AND-join could never complete
        // → the instance silently deadlocks (proven reachable). Reject at activation (config-time) rather
        // than strand at runtime. Uses the DIRECT-only reverse of the claim-side GetUserRoleCodesAsync
        // (both AspNetUserRoles-only, PQ-1 A) so "resolves to nobody" here ⟺ "no user can claim" at runtime
        // — do NOT swap this for an effective-permission lookup, which merges group-inherited roles and
        // would break that equivalence.
        //
        // Scope boundary (deliberate):
        //  - ByPermission pools are NOT checked: no reverse permission→users lookup exists (building it is a
        //    separate deferred epic). A child with ANY ByPermission clause is skipped — its pool may have
        //    holders we cannot enumerate, so rejecting it here would be a false-reject.
        //  - Empty-refs ByRole (no codes) is left to the existing runtime fork-entry empty-pool guard.
        //  - AllowAssignorOverride is NOT consulted: the branch-child assign/reassign override guard is an
        //    unwired later phase (WorkflowBranchChildAssignment.AllowAssignorOverride), so a 0-claimant pool
        //    is unrecoverable today. If that guard ships, add `&& !child.Assignment.AllowAssignorOverride`
        //    to the skip condition (an overridable pool is recoverable and must not be rejected).
        //  - Design-time only: a role drained AFTER activation re-opens the deadlock → the deferred branch
        //    reconciler is the runtime backstop for that drift.
        await ValidateBranchChildRoleClaimantsForActivateAsync(branchList, states, tenantId, violations, ct);

        return violations.Count == 0
            ? WorkflowGraphValidationReport.Ok()
            : WorkflowGraphValidationReport.Fail(violations);
    }

    /// <summary>
    /// RB5 live-claimant check (Activate). For each branch child whose claimable clauses are EXCLUSIVELY
    /// ByRole, resolves the role codes against live Identity (active <c>AspNetUserRoles</c>). A child whose
    /// role pool resolves to zero active users is rejected — at runtime its task would be staged but
    /// unclaimable, permanently blocking the AND-join. See the caller for the full scope rationale
    /// (ByPermission skipped, override unwired, design-time drift). Anchors to the branching state so the FE
    /// canvas highlights the fork, mirroring the structural RB5 detail.
    /// </summary>
    private async Task ValidateBranchChildRoleClaimantsForActivateAsync(
        IReadOnlyList<WorkflowParallelBranch> branches,
        IReadOnlyList<WorkflowState> states,
        Guid tenantId,
        List<GraphRuleViolation> violations,
        CancellationToken ct)
    {
        if (branches.Count == 0) return;

        var stateNameById = states.ToDictionary(s => s.Id, s => s.Name);

        foreach (var branch in branches)
        {
            var anchor = stateNameById.TryGetValue(branch.BranchingStateId, out var name)
                ? name
                : branch.BranchingStateId.ToString();

            foreach (var child in branch.Children)
            {
                var clauses = child.Assignment?.Clauses;
                if (clauses is null || clauses.Count == 0)
                    continue; // structural RB5 already flagged a child with no pool.

                // Only an all-ByRole pool can be proven 0-claimant here. A ByPermission clause makes the
                // union potentially non-empty (no reverse lookup), so skip the whole child conservatively.
                if (clauses.Any(c => c.Mode != AssignmentClauseMode.ByRole))
                    continue;

                // Pool = union of the child's clauses. Collect canonical (UPPER+TRIM) role codes — the form
                // ResolveActiveUsersInRolesAsync expects and the resolver stores at runtime. Malformed JSON
                // at rest is unreachable through Save (validated there), but emit a clean RB5 rather than let
                // an uncaught JsonException surface as a 500 — mirrors the R3 malformed-RefsJson handling
                // elsewhere in this validator.
                List<string> roleCodes;
                try
                {
                    roleCodes = clauses
                        .SelectMany(c => ParseRoleCodes(c.RefsJson))
                        .Distinct(StringComparer.Ordinal)
                        .ToList();
                }
                catch (JsonException)
                {
                    violations.Add(BranchViolation("RB5", anchor,
                        $"Branch child '{child.Name}': role pool RefsJson is not valid JSON."));
                    continue;
                }

                // No codes (all clauses are empty-refs) → the runtime empty-pool fork-entry guard owns it.
                if (roleCodes.Count == 0)
                    continue;

                var claimants = await _identityClient.ResolveActiveUsersInRolesAsync(roleCodes, tenantId, ct);
                if (claimants.Count == 0)
                {
                    violations.Add(BranchViolation("RB5", anchor,
                        $"Branch child '{child.Name}': role pool [{string.Join(", ", roleCodes)}] resolves to no active user — the forked child task would be unclaimable and its AND-join could never complete."));
                }
            }
        }
    }

    // Parses RefsJson + canonicalizes role codes (Trim + ToUpperInvariant) to the form
    // ResolveActiveUsersInRolesAsync expects. Blank entries dropped. Self-contained (does not depend on the
    // structural pass having canonicalized first), mirroring the resolver's ParseRefs so the design-time
    // claimant check matches runtime resolution exactly.
    private static IReadOnlyList<string> ParseRoleCodes(string? refsJson)
    {
        if (string.IsNullOrWhiteSpace(refsJson))
            return [];

        var values = JsonSerializer.Deserialize<List<string>>(refsJson);
        if (values is null)
            return [];

        var result = new List<string>(values.Count);
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v))
                result.Add(v.Trim().ToUpperInvariant());
        return result;
    }

    /// <summary>
    /// RB5 (Activate): each branch child needs an assignment with ≥1 clause, and every clause must be a
    /// read-side-claimable mode (ByPermission / ByRole). Violations anchor to the branching state so the
    /// FE canvas highlights the fork. Mirrors the Save-time RB5 guard for the persisted entity graph.
    /// </summary>
    private static void ValidateBranchChildPoolsForActivate(
        IReadOnlyList<WorkflowParallelBranch> branches,
        IReadOnlyList<WorkflowState> states,
        List<GraphRuleViolation> violations)
    {
        if (branches.Count == 0) return;

        var stateNameById = states.ToDictionary(s => s.Id, s => s.Name);

        foreach (var branch in branches)
        {
            var anchor = stateNameById.TryGetValue(branch.BranchingStateId, out var name)
                ? name
                : branch.BranchingStateId.ToString();

            foreach (var child in branch.Children)
            {
                if (child.Assignment is null || child.Assignment.Clauses.Count == 0)
                {
                    violations.Add(BranchViolation("RB5", anchor,
                        $"Branch child '{child.Name}' has no assignment pool — cannot activate (the fork fan-out would have no candidate to claim it)."));
                    continue;
                }

                foreach (var clause in child.Assignment.Clauses)
                {
                    if (clause.Mode is not (AssignmentClauseMode.ByPermission or AssignmentClauseMode.ByRole))
                    {
                        violations.Add(BranchViolation("RB5", anchor,
                            $"Branch child '{child.Name}': clause mode '{clause.Mode}' is not a claimable parallel-branch pool (only ByPermission and ByRole)."));
                    }
                }
            }
        }
    }

    // Parses the RefsJson array, validates each code against PermCodes.All (OrdinalIgnoreCase + Trim),
    // and canonicalizes (Trim + ToUpperInvariant) known-valid codes in-memory via SetCanonicalRefsJson.
    // Canonicalization fires unconditionally for each recognized code; unknown codes are only flagged
    // as violations but NOT written back (avoids persisting garbage as "canonical").
    // Caller persists by calling SaveChanges ONLY on the happy (no-violation) path.
    private static void CheckByPermissionCodesAndCanonicalize(
        WorkflowTransitionAssignmentClause clause,
        string stateName,
        string transitionActionName,
        List<GraphRuleViolation> violations)
    {
        var canonical = CanonicalizeByPermissionCodes(clause.RefsJson, stateName, transitionActionName, violations);
        if (canonical is not null && !string.Equals(canonical, clause.RefsJson, StringComparison.Ordinal))
        {
            clause.SetCanonicalRefsJson(canonical);
        }
    }

    // Phase 03 (plan 260523-1241) — spec-bound overload for Save-time canonicalization.
    // Mirrors the entity-bound overload above; shared pure-fn CanonicalizeByPermissionCodes does the work.
    private static void CheckByPermissionCodesAndCanonicalize(
        AssignmentClauseSpec clause,
        string stateName,
        string transitionActionName,
        List<GraphRuleViolation> violations)
    {
        var canonical = CanonicalizeByPermissionCodes(clause.RefsJson, stateName, transitionActionName, violations);
        if (canonical is not null && !string.Equals(canonical, clause.RefsJson, StringComparison.Ordinal))
        {
            clause.SetCanonicalRefsJson(canonical);
        }
    }

    // ByRole canonicalization helpers (entity + spec overloads).
    // Role codes are stable ASCII strings (e.g. "BTC_MINISTER"), NOT validated against a static set;
    // canonicalization is pure text normalization (Trim + ToUpperInvariant). Malformed JSON emits R3
    // violation (fail-loud symmetry with ByPermission) — caller does not SaveChanges on violation path.
    private static void CanonicalizeRoleCodesInPlace(
        WorkflowTransitionAssignmentClause clause,
        string stateName,
        string transitionActionName,
        List<GraphRuleViolation> violations)
    {
        var canonical = CanonicalizeStringRefs(clause.RefsJson, "ByRole", stateName, transitionActionName, violations);
        if (canonical is not null && !string.Equals(canonical, clause.RefsJson, StringComparison.Ordinal))
        {
            clause.SetCanonicalRefsJson(canonical);
        }
    }

    private static void CanonicalizeRoleCodesInPlace(
        AssignmentClauseSpec clause,
        string stateName,
        string transitionActionName,
        List<GraphRuleViolation> violations)
    {
        var canonical = CanonicalizeStringRefs(clause.RefsJson, "ByRole", stateName, transitionActionName, violations);
        if (canonical is not null && !string.Equals(canonical, clause.RefsJson, StringComparison.Ordinal))
        {
            clause.SetCanonicalRefsJson(canonical);
        }
    }

    // RT2-15 Phase 04 — ByOrgUnit canonicalization helpers (entity + spec overloads).
    // OrgUnit codes are arbitrary strings (Trim+ToUpperInvariant); same shape as ByGroup helpers.
    // Save-time validation is structural only; code existence enforced at runtime by resolver.
    private static List<string>? CanonicalizeOrgUnitCodesInPlace(
        WorkflowTransitionAssignmentClause clause,
        string stateName,
        string transitionActionName,
        List<GraphRuleViolation> violations)
    {
        var (canonical, codes) = CanonicalizeStringRefsWithList(clause.RefsJson, "ByOrgUnit", stateName, transitionActionName, violations);
        if (canonical is not null && !string.Equals(canonical, clause.RefsJson, StringComparison.Ordinal))
            clause.SetCanonicalRefsJson(canonical);
        return codes;
    }

    private static List<string>? CanonicalizeOrgUnitCodesInPlace(
        AssignmentClauseSpec clause,
        string stateName,
        string transitionActionName,
        List<GraphRuleViolation> violations)
    {
        var (canonical, codes) = CanonicalizeStringRefsWithList(clause.RefsJson, "ByOrgUnit", stateName, transitionActionName, violations);
        if (canonical is not null && !string.Equals(canonical, clause.RefsJson, StringComparison.Ordinal))
            clause.SetCanonicalRefsJson(canonical);
        return codes;
    }

    // RT2-15 Phase 02 — ByGroup canonicalization helpers (entity + spec overloads).
    // Group codes are arbitrary strings (Trim+ToUpperInvariant); same shape as ByRole helpers.
    // Returns the canonical list (for Q2 batching) when canonicalization succeeds; null on JSON error.
    private static List<string>? CanonicalizeGroupCodesInPlace(
        WorkflowTransitionAssignmentClause clause,
        string stateName,
        string transitionActionName,
        List<GraphRuleViolation> violations)
    {
        var (canonical, codes) = CanonicalizeStringRefsWithList(clause.RefsJson, "ByGroup", stateName, transitionActionName, violations);
        if (canonical is not null && !string.Equals(canonical, clause.RefsJson, StringComparison.Ordinal))
            clause.SetCanonicalRefsJson(canonical);
        return codes;
    }

    private static List<string>? CanonicalizeGroupCodesInPlace(
        AssignmentClauseSpec clause,
        string stateName,
        string transitionActionName,
        List<GraphRuleViolation> violations)
    {
        var (canonical, codes) = CanonicalizeStringRefsWithList(clause.RefsJson, "ByGroup", stateName, transitionActionName, violations);
        if (canonical is not null && !string.Equals(canonical, clause.RefsJson, StringComparison.Ordinal))
            clause.SetCanonicalRefsJson(canonical);
        return codes;
    }

    /// <summary>
    /// Shared pure helper: parse RefsJson array of strings, Trim+ToUpperInvariant each, return serialized JSON.
    /// Emits R3 violation on JSON parse failure. Applies to ByRole and ByGroup (both are arbitrary string refs).
    /// Returns null when JSON parse fails or input is null.
    /// </summary>
    private static string? CanonicalizeStringRefs(
        string? refsJson,
        string modeLabel,
        string stateName,
        string transitionActionName,
        List<GraphRuleViolation> violations)
    {
        var (canonical, _) = CanonicalizeStringRefsWithList(refsJson, modeLabel, stateName, transitionActionName, violations);
        return canonical;
    }

    /// <summary>
    /// Like <see cref="CanonicalizeStringRefs"/> but also returns the canonical list for Q2 batching.
    /// Returns (null, null) on JSON error; (json, list) on success.
    /// </summary>
    private static (string? Json, List<string>? Codes) CanonicalizeStringRefsWithList(
        string? refsJson,
        string modeLabel,
        string stateName,
        string transitionActionName,
        List<GraphRuleViolation> violations)
    {
        if (refsJson is null) return (null, null);

        List<string>? names;
        try
        {
            names = JsonSerializer.Deserialize<List<string>>(refsJson);
        }
        catch (JsonException)
        {
            violations.Add(new GraphRuleViolation(
                Rule: "R3",
                StateName: stateName,
                TransitionActionName: transitionActionName,
                Detail: $"Transition '{transitionActionName}': {modeLabel} RefsJson is not valid JSON: {refsJson}"));
            return (null, null);
        }

        if (names is null) return (null, null);

        var canonical = new List<string>(names.Count);
        foreach (var raw in names)
        {
            var trimmed = raw?.Trim() ?? string.Empty;
            canonical.Add(trimmed.ToUpperInvariant());
        }
        return (JsonSerializer.Serialize(canonical), canonical);
    }

    /// <summary>
    /// Pure helper: parse RefsJson array, validate each code against PermCodes.All,
    /// emit R3 violations for unknowns / invalid JSON, return canonical JSON form (Trim + Upper).
    /// Returns null only when JSON parse fails or RefsJson is null/empty.
    /// </summary>
    private static string? CanonicalizeByPermissionCodes(
        string? refsJson,
        string stateName,
        string transitionActionName,
        List<GraphRuleViolation> violations)
    {
        if (refsJson is null) return null;

        List<string>? codes;
        try
        {
            codes = JsonSerializer.Deserialize<List<string>>(refsJson);
        }
        catch (JsonException)
        {
            violations.Add(new GraphRuleViolation(
                Rule: "R3",
                StateName: stateName,
                TransitionActionName: transitionActionName,
                Detail: $"Transition '{transitionActionName}': ByPermission RefsJson is not valid JSON: {refsJson}"));
            return null;
        }

        if (codes is null) return null;

        var canonicalCodes = new List<string>(codes.Count);

        foreach (var rawCode in codes)
        {
            var trimmed = rawCode?.Trim() ?? string.Empty;
            var upper = trimmed.ToUpperInvariant();

            if (!_permCodeSet.Contains(trimmed))
            {
                violations.Add(new GraphRuleViolation(
                    Rule: "R3",
                    StateName: stateName,
                    TransitionActionName: transitionActionName,
                    Detail: $"Transition '{transitionActionName}': permission code '{trimmed}' is not in PermCodes.All."));
            }

            // Accumulate canonical form regardless of validity.
            // Caller does not SaveChanges when violations exist (Option A) — safe to write upper-cased garbage.
            canonicalCodes.Add(upper);
        }

        return JsonSerializer.Serialize(canonicalCodes);
    }
}
