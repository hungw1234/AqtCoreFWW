using System.Text.Json;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;

namespace AqtCoreFW.Workflow.Application.Services;

/// <summary>
/// Model B resolver (Phase-2, plan 260519-0421 v2 + addendum; RT2-15 Phases 01-04).
/// MVP modes: SpecificUser / Initiator / ByPermission / PreviousHandler / ByRole / ByGroup / ByOrgUnit implemented;
/// Intersect combine remains fail-loud <see cref="NotSupportedException"/> stub (RT2-15 FU).
/// ByOrgUnit (Phase 04 2026-05-26): temporal assignment predicate-pool producer; uses
/// <see cref="IOrgUnitModuleClient.ResolveActiveUsersInOrgUnitsAsync"/> to resolve active members.
/// PreviousHandler reads the last completed task on the workflow instance via
/// <see cref="IWorkflowTaskRepository.GetLastCompletedTaskAsync"/> — this breaks the prior
/// "zero-dependency, Workflow-module-only" purity invariant per Sunday Q1 ratification.
/// </summary>
public sealed class WorkflowHandlerResolver(IWorkflowTaskRepository taskRepository)
    : IWorkflowHandlerResolver
{
    private readonly IWorkflowTaskRepository _taskRepository = taskRepository;

    public async Task<HandlerResolution> ResolveAsync(
        WorkflowState targetState,
        IReadOnlyList<OutgoingAssignment> outgoing,
        WorkflowResolutionContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(targetState);
        ArgumentNullException.ThrowIfNull(outgoing);
        ArgumentNullException.ThrowIfNull(context);

        var users = new HashSet<Guid>();
        // Distinct by (Type, Ref); cross-transition combination is always Union.
        var predicates = new Dictionary<(CandidatePrincipalType, string), CandidatePredicate>();

        foreach (var o in outgoing)
        {
            // Exclude the auto-timeout transition and any recall transition (Pool definition).
            if (o.Transition.Id == targetState.AutoTransitionOnTimeoutId || o.Transition.IsRecall)
                continue;

            var assignment = o.Assignment;
            if (assignment is null)
                continue;

            if (assignment.CombineMode == AssignmentCombineMode.Intersect)
                throw new NotSupportedException(
                    "Intersect combine has no MVP producer (RT2-15) — deferred to the FE-config plan.");

            foreach (var clause in assignment.Clauses)
                await ApplyClauseAsync(clause.Mode, clause.RefsJson, context, users, predicates, assignment.CombineMode, ct);
        }

        return Finalize(users, predicates.Values);
    }

    public async Task<HandlerResolution> ResolveAssignmentAsync(
        IReadOnlyList<(AssignmentClauseMode Mode, string? RefsJson)> clauses,
        AssignmentCombineMode combineMode,
        WorkflowResolutionContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(clauses);
        ArgumentNullException.ThrowIfNull(context);

        if (combineMode == AssignmentCombineMode.Intersect)
            throw new NotSupportedException(
                "Intersect combine has no MVP producer (RT2-15) — deferred to the FE-config plan.");

        var users = new HashSet<Guid>();
        var predicates = new Dictionary<(CandidatePrincipalType, string), CandidatePredicate>();

        foreach (var (mode, refsJson) in clauses)
            await ApplyClauseAsync(mode, refsJson, context, users, predicates, combineMode, ct);

        return Finalize(users, predicates.Values);
    }

    private async Task ApplyClauseAsync(
        AssignmentClauseMode mode,
        string? refsJson,
        WorkflowResolutionContext context,
        HashSet<Guid> users,
        Dictionary<(CandidatePrincipalType, string), CandidatePredicate> predicates,
        AssignmentCombineMode combine,
        CancellationToken ct)
    {
        switch (mode)
        {
            case AssignmentClauseMode.SpecificUser:
                foreach (var raw in ParseRefs(refsJson))
                    users.Add(Guid.Parse(raw));
                break;

            case AssignmentClauseMode.Initiator:
                users.Add(context.InitiatorUserId);
                break;

            case AssignmentClauseMode.ByPermission:
                foreach (var code in ParseRefs(refsJson))
                {
                    var key = (CandidatePrincipalType.Permission, code);
                    if (!predicates.ContainsKey(key))
                        predicates[key] = new CandidatePredicate(
                            CandidatePrincipalType.Permission, code, combine);
                }
                break;

            case AssignmentClauseMode.PreviousHandler:
                // RT2-15 Phase 01: resolve to the user who completed the most-recent prior
                // task on the same workflow instance. WorkflowInstanceId=0 (no instance yet,
                // e.g. CreateInstance first-task path) → no prior task → empty contribution →
                // falls through to existing fail-CLOSED FineResolverGate 422.
                if (context.WorkflowInstanceId > 0)
                {
                    var lastCompleted = await _taskRepository
                        .GetLastCompletedTaskAsync(context.WorkflowInstanceId, ct);
                    if (lastCompleted?.CompletedBy is { } completor)
                        users.Add(completor);
                }
                break;

            case AssignmentClauseMode.ByRole:
                // Direct-only role semantic (PQ-1 A): push CandidatePredicate(Role, roleCode) —
                // mirror the ByPermission/ByGroup pattern. Refs are role CODES (e.g. "BTC_MINISTER",
                // stable ASCII), already canonicalized (Trim+ToUpperInvariant) Save-time by
                // WorkflowGraphValidator. Read-side claim/inbox queries (WorkflowTaskRepository)
                // match PrincipalType=Role against the user's role codes (GetUserRoleCodesAsync) —
                // read-side shipped (T3-WF-CANDIDATEINDEX-READSIDE-EXTENSION).
                foreach (var roleCode in ParseRefs(refsJson))
                {
                    var roleKey = (CandidatePrincipalType.Role, roleCode);
                    if (!predicates.ContainsKey(roleKey))
                        predicates[roleKey] = new CandidatePredicate(
                            CandidatePrincipalType.Role, roleCode, combine);
                }
                break;

            case AssignmentClauseMode.ByGroup:
                // RT2-15 Phase 02 (2026-05-25): direct-only group semantic (PQ-2 B).
                // Push CandidatePredicate(Group, groupCode) — mirror ByRole pattern. Refs are
                // already canonicalized (Trim+ToUpperInvariant) Save-time by WorkflowGraphValidator.
                // READ-SIDE GAP: WorkflowTaskRepository claim/inbox queries still filter
                // PrincipalType=Permission + Role only (Phase 03 B1 readside extension shipped Role
                // 2026-05-25 v2.137p110, Group arm NOT YET added). Until T3-WF-CANDIDATEINDEX-READSIDE-
                // EXT-GROUP (P0 CRITICAL) ships, Group-pooled tasks are unclaimable + inbox-invisible.
                // Do NOT seed ByGroup assignments in production-touching test seeders.
                foreach (var groupCode in ParseRefs(refsJson))
                {
                    var groupKey = (CandidatePrincipalType.Group, groupCode);
                    if (!predicates.ContainsKey(groupKey))
                        predicates[groupKey] = new CandidatePredicate(
                            CandidatePrincipalType.Group, groupCode, combine);
                }
                break;

            case AssignmentClauseMode.ByOrgUnit:
                // RT2-15 Phase 04 (2026-05-26): temporal org-unit assignment predicate-pool producer.
                // Refs are already canonicalized (Trim+ToUpperInvariant) Save-time by WorkflowGraphValidator.
                // Uses IOrgUnitModuleClient.ResolveActiveUsersInOrgUnitsAsync — temporal filter
                // (IsActive=1, ValidFrom<=Now, ValidTo IS NULL OR ValidTo>Now).
                // Mirror ByGroup pattern: push CandidatePredicate(OrgUnit, code) per clause ref.
                // Predicate-pool path (Option A per 260525-C): push predicates, NOT eager-resolve.
                foreach (var orgUnitCode in ParseRefs(refsJson))
                {
                    var orgUnitKey = (CandidatePrincipalType.OrgUnit, orgUnitCode);
                    if (!predicates.ContainsKey(orgUnitKey))
                        predicates[orgUnitKey] = new CandidatePredicate(
                            CandidatePrincipalType.OrgUnit, orgUnitCode, combine);
                }
                break;

            default:
                throw new NotSupportedException($"Unknown clause mode '{mode}'.");
        }
    }

    private static HandlerResolution Finalize(
        IReadOnlyCollection<Guid> users,
        IReadOnlyCollection<CandidatePredicate> predicates)
    {
        if (users.Count == 0 && predicates.Count == 0)
            return HandlerResolution.Empty;

        if (predicates.Count == 0 && users.Count == 1)
            return HandlerResolution.ToAssignee(users.First());

        if (users.Count == 0)
            return HandlerResolution.Pool([.. predicates]);

        // Multi concrete-user, or concrete-user mixed with predicates: addendum §3 —
        // no MVP producer (no User principal type to index a multi-user pool).
        throw new NotSupportedException(
            "Multi-user or mixed concrete-user + predicate resolution has no MVP producer " +
            "(phase-02 addendum §3) — revisit when an FE config UI produces such assignments.");
    }

    private static IReadOnlyList<string> ParseRefs(string? refsJson)
    {
        if (string.IsNullOrWhiteSpace(refsJson))
            return [];

        var values = JsonSerializer.Deserialize<List<string>>(refsJson);
        if (values is null)
            return [];

        // Drop blank entries; trim so a canonical ref never carries incidental whitespace.
        var result = new List<string>(values.Count);
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v))
                result.Add(v.Trim());
        return result;
    }
}
