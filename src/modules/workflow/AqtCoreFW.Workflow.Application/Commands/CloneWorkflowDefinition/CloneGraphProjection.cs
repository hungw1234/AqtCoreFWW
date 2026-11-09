using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Domain.Entities;

namespace AqtCoreFW.Workflow.Application.Commands.CloneWorkflowDefinition;

/// <summary>
/// Projects a source definition's persisted graph (id-keyed entities) into the name-keyed
/// transport specs consumed by <c>IWorkflowDefinitionRepository.ReplaceGraphAsync</c>.
///
/// Why name-keyed: a clone re-INSERTs every state, so the clone's state IDENTITY values differ
/// from the source's. State references (transition FKs, branch branching/continuation/target/
/// resolution states) are bare bigint with no DB FK, so they must travel as the state's STABLE
/// identity — its Name — and be re-resolved to the clone's fresh ids after states persist. This
/// mirrors the SaveDefinitionGraph wire→spec path; here the source is the live graph, not a payload.
///
/// Scope: this projection is the deep-copy of the two satellite families the clone previously
/// dropped — transition assignments (separate entity keyed by transition id) and parallel branches
/// (separate aggregate keyed by definition id). State + transition copy stays the repository's job
/// via ReplaceGraphAsync; AutoTransitionOnTimeoutId remap stays the handler's (it needs the clone's
/// post-save transition ids).
/// </summary>
internal static class CloneGraphProjection
{
    /// <summary>Builds the clone's fresh state entities from the source states (auto-timeout deferred to handler remap).</summary>
    public static List<WorkflowState> BuildCloneStates(long cloneId, IReadOnlyList<WorkflowState> sourceStates)
    {
        var states = new List<WorkflowState>(sourceStates.Count);
        foreach (var s in sourceStates)
        {
            // AutoTransitionOnTimeoutId references a TRANSITION id that does not exist until the clone's
            // transitions persist — handled post-save by the handler's remap. Null here, like Clone().
            states.Add(WorkflowState.Create(
                cloneId, s.Name, s.DisplayNameVi, s.DisplayNameEn,
                s.IsInitial, s.IsFinal, s.Color, s.SortOrder,
                s.SlaHours, autoTransitionOnTimeoutId: null,
                positionX: s.PositionX, positionY: s.PositionY));
        }
        return states;
    }

    /// <summary>
    /// Builds name-keyed transition specs carrying each transition's assignment payload (the family
    /// the clone used to drop). <paramref name="assignmentByTransitionId"/> is the source-transition-id
    /// → assignment map from <c>GetForValidationAsync</c>; a missing/null entry = no pool rule.
    /// </summary>
    public static List<NewTransitionSpec> BuildTransitionSpecs(
        IReadOnlyList<WorkflowTransition> sourceTransitions,
        IReadOnlyDictionary<long, string> stateNameById,
        IReadOnlyDictionary<long, OutgoingAssignment?> assignmentByTransitionId)
    {
        var specs = new List<NewTransitionSpec>(sourceTransitions.Count);
        foreach (var t in sourceTransitions)
        {
            var assignment = assignmentByTransitionId.TryGetValue(t.Id, out var oa) ? oa?.Assignment : null;
            specs.Add(new NewTransitionSpec(
                stateNameById[t.FromStateId], stateNameById[t.ToStateId],
                t.ActionName, t.ActionLabelVi, t.ActionLabelEn,
                t.ConditionsJson, t.SortOrder, t.IsRecall,
                t.SourceHandle, t.TargetHandle)
            {
                Assignment = MapTransitionAssignment(assignment),
            });
        }
        return specs;
    }

    /// <summary>
    /// Builds name-keyed branch specs (fork + children + per-child pools + resolutions) — the second
    /// family the clone used to drop. State references are de-referenced to Names via the source map.
    /// </summary>
    public static List<NewBranchSpec> BuildBranchSpecs(
        IReadOnlyList<WorkflowParallelBranch> sourceBranches,
        IReadOnlyDictionary<long, string> stateNameById)
    {
        var specs = new List<NewBranchSpec>(sourceBranches.Count);
        foreach (var b in sourceBranches)
        {
            var children = new List<NewBranchChildSpec>(b.Children.Count);
            foreach (var c in b.Children)
            {
                var resolutions = new List<NewBranchResolutionSpec>(c.Resolutions.Count);
                foreach (var r in c.Resolutions)
                {
                    resolutions.Add(new NewBranchResolutionSpec(
                        r.ResolutionType,
                        r.ResultingStateId is { } rid ? stateNameById[rid] : null));
                }

                children.Add(new NewBranchChildSpec(
                    stateNameById[c.TargetStateId], c.Name, c.DisplayNameVi,
                    c.DepartmentId, c.DefaultAssigneeId, c.SortOrder, resolutions)
                {
                    Assignment = MapChildAssignment(c.Assignment),
                });
            }

            specs.Add(new NewBranchSpec(
                stateNameById[b.BranchingStateId], stateNameById[b.ContinuationStateId],
                b.BranchType, b.RequiredCount, b.AllowCancelOnAnyReject, b.TimeoutMinutes,
                b.PositionX, b.PositionY, children));
        }
        return specs;
    }

    private static AssignmentSpec? MapTransitionAssignment(WorkflowTransitionAssignment? a)
    {
        if (a is null) return null;
        var clauses = new List<AssignmentClauseSpec>(a.Clauses.Count);
        foreach (var c in a.Clauses)
            clauses.Add(new AssignmentClauseSpec(c.Mode, c.RefsJson));
        return new AssignmentSpec(a.CombineMode, a.AllowAssignorOverride, clauses);
    }

    private static AssignmentSpec? MapChildAssignment(WorkflowBranchChildAssignment? a)
    {
        if (a is null) return null;
        var clauses = new List<AssignmentClauseSpec>(a.Clauses.Count);
        foreach (var c in a.Clauses)
            clauses.Add(new AssignmentClauseSpec(c.Mode, c.RefsJson));
        return new AssignmentSpec(a.CombineMode, a.AllowAssignorOverride, clauses);
    }
}
