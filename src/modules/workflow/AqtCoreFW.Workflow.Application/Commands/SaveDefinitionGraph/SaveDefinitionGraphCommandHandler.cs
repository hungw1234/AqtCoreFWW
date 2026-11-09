using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Validation;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Domain.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.SaveDefinitionGraph;

/// <summary>
/// Replaces all states and transitions on a workflow definition.
///
/// State entities are built here with Id=0; SQL Server assigns the IDENTITY in the repository
/// after SaveChanges. Transitions are passed as <see cref="NewTransitionSpec"/> (name-keyed FKs)
/// because their FromStateId/ToStateId are unknown until states are persisted — see the
/// repository's two-phase save for FK resolution.
///
/// Phase 5c: R1 graph validation runs against the in-memory graph BEFORE ReplaceGraphAsync,
/// so a validation failure never partially commits state.
/// </summary>
public sealed class SaveDefinitionGraphCommandHandler(
    IWorkflowDefinitionRepository repository,
    IWorkflowGraphValidator graphValidator)
    : IRequestHandler<SaveDefinitionGraphCommand, Result<WorkflowDefinitionDto>>
{
    public async Task<Result<WorkflowDefinitionDto>> Handle(
        SaveDefinitionGraphCommand request,
        CancellationToken cancellationToken)
    {
        var defResult = await repository.GetWithStatesAndTransitionsAsync(
            request.DefinitionId, cancellationToken);

        if (defResult.IsFailed)
            return Result.Fail(WorkflowErrors.DefinitionNotFound(request.DefinitionId));

        var definition = defResult.Value;

        if (definition.TenantId != request.TenantId)
            return Result.Fail(WorkflowErrors.DefinitionNotFound(request.DefinitionId));

        var initialCount = request.States.Count(s => s.IsInitial);
        if (request.States.Count > 0 && initialCount != 1)
            return Result.Fail(WorkflowErrors.NoInitialState());

        var duplicateState = request.States
            .GroupBy(s => s.Name)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicateState is not null)
            return Result.Fail(WorkflowErrors.DuplicateStateName(duplicateState.Key));

        var stateNames = new HashSet<string>(StringComparer.Ordinal);
        var newStates = new List<WorkflowState>();
        foreach (var s in request.States)
        {
            var state = WorkflowState.Create(
                definition.Id, s.Name, s.DisplayNameVi, s.DisplayNameEn,
                s.IsInitial, s.IsFinal, s.Color, s.SortOrder,
                s.SlaHours, s.AutoTransitionOnTimeoutId,
                s.PositionX, s.PositionY);
            newStates.Add(state);
            stateNames.Add(s.Name);
        }

        var transitionSpecs = new List<NewTransitionSpec>(request.Transitions.Count);
        var seenTransitions = new HashSet<(string, string)>(); // FromStateName + ActionName

        foreach (var t in request.Transitions)
        {
            // ValidationError → HTTP 422 (matches CreateWorkflowDefinition sibling for FE parity).
            if (!stateNames.Contains(t.FromStateName))
                return Result.Fail(new ValidationError(
                    $"FromState '{t.FromStateName}' not found in graph.", nameof(t.FromStateName)));
            if (!stateNames.Contains(t.ToStateName))
                return Result.Fail(new ValidationError(
                    $"ToState '{t.ToStateName}' not found in graph.", nameof(t.ToStateName)));

            var key = (t.FromStateName, t.ActionName);
            if (!seenTransitions.Add(key))
                return Result.Fail(new ValidationError(
                    $"Transition '{t.ActionName}' from state '{t.FromStateName}' already exists.",
                    nameof(t.ActionName)));

            transitionSpecs.Add(new NewTransitionSpec(
                t.FromStateName, t.ToStateName,
                t.ActionName, t.ActionLabelVi, t.ActionLabelEn,
                t.ConditionsJson, t.SortOrder, t.IsRecall,
                t.SourceHandle, t.TargetHandle)
            {
                // T3-WF-RECALL-ASSIGNMENT-Q (ratified 2026-05-23 Option A) — silent-strip:
                // recall transitions never carry an assignment payload, even if FE sends one.
                // Validator R2-R4 already exempt recall (dead-data otherwise persisted).
                Assignment = t.IsRecall ? null : MapAssignment(t.Assignment),
            });
        }

        // Parallel branches (additive). Mirror the transition guards: structural state-name existence
        // + NOfM/duplicate checks return a field-anchored ValidationError (422) before the validator
        // and ReplaceGraphAsync run. The validator (below) re-checks RB1-RB4 as the canonical graph gate.
        var branchSpecsResult = BuildBranchSpecs(request.Branches, stateNames);
        if (branchSpecsResult.IsFailed)
            return Result.Fail(branchSpecsResult.Errors);
        var branchSpecs = branchSpecsResult.Value;

        // Phase 5c R1 + RB1-RB4: validate BEFORE persisting — failure rolls back atomically
        // (no partial commit since ReplaceGraphAsync hasn't been called yet).
        var graphReport = await graphValidator.ValidateForSaveAsync(
            newStates, transitionSpecs, branchSpecs, request.TenantId, cancellationToken);
        if (!graphReport.IsValid)
            return Result.Fail(WorkflowErrors.GraphValidationFailed(graphReport.Violations));

        await repository.ReplaceGraphAsync(
            definition, newStates, transitionSpecs, branchSpecs, request.ModifiedBy, cancellationToken);

        return Result.Ok(MapToDto(definition, transitionSpecs, newStates, branchSpecs));
    }

    /// <summary>
    /// Builds the name-keyed <see cref="NewBranchSpec"/> transport list from the wire payload,
    /// short-circuiting with a field-anchored <see cref="ValidationError"/> (422) on structural breaches
    /// (unknown state names, NOfM without a positive RequiredCount, duplicate branching state). Mirrors
    /// the transition guards above so contract drift surfaces as a typed 422, not a 500.
    /// </summary>
    private static Result<IReadOnlyList<NewBranchSpec>> BuildBranchSpecs(
        IReadOnlyList<GraphBranchInput> branches,
        HashSet<string> stateNames)
    {
        var specs = new List<NewBranchSpec>(branches.Count);
        var seenBranchingStates = new HashSet<string>(StringComparer.Ordinal);

        foreach (var b in branches)
        {
            if (!stateNames.Contains(b.BranchingStateName))
                return Result.Fail(new ValidationError(
                    $"Branch branching state '{b.BranchingStateName}' not found in graph.", nameof(b.BranchingStateName)));
            if (!stateNames.Contains(b.ContinuationStateName))
                return Result.Fail(new ValidationError(
                    $"Branch continuation state '{b.ContinuationStateName}' not found in graph.", nameof(b.ContinuationStateName)));

            if (!seenBranchingStates.Add(b.BranchingStateName))
                return Result.Fail(new ValidationError(
                    $"State '{b.BranchingStateName}' already has a parallel branch (only one fork per state).",
                    nameof(b.BranchingStateName)));

            if (b.BranchType == ParallelBranchType.NOfM && b.RequiredCount is not > 0)
                return Result.Fail(new ValidationError(
                    $"Branch on '{b.BranchingStateName}' uses NOfM but RequiredCount is missing or not positive.",
                    nameof(b.RequiredCount)));

            var children = new List<NewBranchChildSpec>(b.Children?.Count ?? 0);
            // Pre-guard the child + resolution unique indexes so a duplicate yields a clean 422
            // instead of a DbUpdateException → 500 on the replace-all insert. Mirrors the RB3
            // branching-state guard above.
            var seenChildNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in b.Children ?? [])
            {
                if (!stateNames.Contains(c.TargetStateName))
                    return Result.Fail(new ValidationError(
                        $"Branch child target state '{c.TargetStateName}' not found in graph.", nameof(c.TargetStateName)));

                // UX_WorkflowBranchChildren_ParallelBranchId_Name — child machine name unique per fork.
                if (!seenChildNames.Add(c.Name))
                    return Result.Fail(new ValidationError(
                        $"Branch on '{b.BranchingStateName}' has more than one child named '{c.Name}'.", nameof(c.Name)));

                var resolutions = new List<NewBranchResolutionSpec>(c.Resolutions?.Count ?? 0);
                var seenResolutionTypes = new HashSet<BranchResolutionType>();
                foreach (var r in c.Resolutions ?? [])
                {
                    if (r.ResultingStateName is not null && !stateNames.Contains(r.ResultingStateName))
                        return Result.Fail(new ValidationError(
                            $"Branch resolution resulting state '{r.ResultingStateName}' not found in graph.",
                            nameof(r.ResultingStateName)));

                    // UX_WorkflowBranchResolutions_ChildId_ResolutionType — one outcome of each type per child.
                    if (!seenResolutionTypes.Add(r.ResolutionType))
                        return Result.Fail(new ValidationError(
                            $"Branch child '{c.Name}' has more than one '{r.ResolutionType}' resolution.",
                            nameof(r.ResolutionType)));

                    resolutions.Add(new NewBranchResolutionSpec(r.ResolutionType, r.ResultingStateName));
                }

                children.Add(new NewBranchChildSpec(
                    c.TargetStateName, c.Name, c.DisplayNameVi,
                    c.DepartmentId, c.DefaultAssigneeId, c.SortOrder, resolutions)
                {
                    Assignment = MapAssignment(c.Assignment)
                });
            }

            specs.Add(new NewBranchSpec(
                b.BranchingStateName, b.ContinuationStateName, b.BranchType,
                b.RequiredCount, b.AllowCancelOnAnyReject, b.TimeoutMinutes,
                b.PositionX, b.PositionY, children));
        }

        return Result.Ok<IReadOnlyList<NewBranchSpec>>(specs);
    }

    /// <summary>
    /// Maps <see cref="AssignmentInput"/> → <see cref="AssignmentSpec"/> (repo-transport).
    /// Validator R3 (Phase 03) MAY mutate <see cref="AssignmentClauseSpec.RefsJson"/> in place;
    /// repo Phase 3 (Phase 04) persists whatever the spec carries post-validation.
    /// </summary>
    private static AssignmentSpec? MapAssignment(AssignmentInput? input)
    {
        if (input is null) return null;
        // M1 fold (Phase 05 review): System.Text.Json may leave Clauses=null on
        // {"clauses":null} payload despite non-nullable signature; null-guard prevents NRE.
        // Empty list flows through validator R2-R4 cleanly (M2 semantic decision deferred).
        var srcClauses = input.Clauses ?? Array.Empty<AssignmentClauseInput>();
        var clauses = new List<AssignmentClauseSpec>(srcClauses.Count);
        foreach (var c in srcClauses)
            clauses.Add(new AssignmentClauseSpec(c.Mode, c.RefsJson));
        return new AssignmentSpec(input.CombineMode, input.AllowAssignorOverride, clauses);
    }

    /// <summary>
    /// Projects DTO from the persisted definition + the (post-validator, canonicalized) spec list.
    /// Assignment projection sourced from the spec — avoids a 2nd DB round-trip; FE only needs
    /// transition.Id for round-trip key (assignment.Id is opaque). Pairing uses
    /// (FromStateId → FromStateName via newStates) + ActionName as composite key.
    /// </summary>
    private static WorkflowDefinitionDto MapToDto(
        WorkflowDefinition d,
        IReadOnlyList<NewTransitionSpec> specs,
        IReadOnlyList<WorkflowState> newStates,
        IReadOnlyList<NewBranchSpec> branchSpecs)
    {
        var stateIdToName = new Dictionary<long, string>(newStates.Count);
        var stateIdByName = new Dictionary<string, long>(newStates.Count, StringComparer.Ordinal);
        foreach (var s in newStates)
        {
            stateIdToName[s.Id] = s.Name;
            stateIdByName[s.Name] = s.Id;
        }
        var assignmentByKey = new Dictionary<(string FromStateName, string ActionName), AssignmentSpec>(specs.Count);
        foreach (var s in specs)
        {
            if (s.Assignment is not null)
                assignmentByKey[(s.FromStateName, s.ActionName)] = s.Assignment;
        }

        return new(
        d.PublicId, d.Name, d.Description, d.IsActive, d.TenantId, d.CreatedAt,
        d.DefinitionKey, d.Version, d.IsLatest, d.EntityType,
        d.States.Select(s => new WorkflowStateDto(
            s.Id, s.Name, s.DisplayNameVi, s.DisplayNameEn,
            s.IsInitial, s.IsFinal, s.Color, s.SortOrder,
            s.SlaHours, s.AutoTransitionOnTimeoutId,
            s.PositionX, s.PositionY)).ToList(),
        d.Transitions.Select(t =>
        {
            WorkflowTransitionAssignmentDto? assignmentDto = null;
            if (stateIdToName.TryGetValue(t.FromStateId, out var fromName)
                && assignmentByKey.TryGetValue((fromName, t.ActionName), out var a))
            {
                var clauseDtos = new List<WorkflowTransitionAssignmentClauseDto>(a.Clauses.Count);
                foreach (var c in a.Clauses)
                    clauseDtos.Add(new WorkflowTransitionAssignmentClauseDto(c.Mode, c.RefsJson));
                assignmentDto = new WorkflowTransitionAssignmentDto(a.CombineMode, a.AllowAssignorOverride, clauseDtos);
            }

            return new WorkflowTransitionDto(
                t.Id, t.FromStateId, t.ToStateId, t.ActionName,
                t.ActionLabelVi, t.ActionLabelEn,
                t.ConditionsJson, t.SortOrder, t.IsRecall,
                t.SourceHandle, t.TargetHandle)
            {
                Assignment = assignmentDto,
            };
        }).ToList(),
        // Save-response branch projection from specs (long IDs resolved via the freshly-saved state
        // map). Branch/child/resolution row IDs are DB-assigned and not reloaded here — the FE ignores
        // the save response and refetches via GET; Id=0 is a benign placeholder (mirrors the
        // spec-sourced assignment projection above). Cheap is correct.
        ProjectBranches(branchSpecs, stateIdByName));
    }

    /// <summary>Projects name-keyed branch specs → the GET-shaped <see cref="WorkflowBranchDto"/> for the save response.</summary>
    private static List<WorkflowBranchDto> ProjectBranches(
        IReadOnlyList<NewBranchSpec> branchSpecs,
        IReadOnlyDictionary<string, long> stateIdByName)
    {
        long ResolveId(string name) => stateIdByName.TryGetValue(name, out var id) ? id : 0L;

        var result = new List<WorkflowBranchDto>(branchSpecs.Count);
        foreach (var b in branchSpecs)
        {
            var children = b.Children.Select(c => new WorkflowBranchChildDto(
                Id: 0L,
                TargetStateId: ResolveId(c.TargetStateName),
                c.Name, c.DisplayNameVi, c.DepartmentId, c.DefaultAssigneeId, c.SortOrder,
                c.Resolutions.Select(r => new WorkflowBranchResolutionDto(
                    r.ResolutionType,
                    r.ResultingStateName is null ? null : ResolveId(r.ResultingStateName))).ToList(),
                ProjectChildAssignment(c.Assignment)))
                .ToList();

            result.Add(new WorkflowBranchDto(
                Id: 0L,
                BranchingStateId: ResolveId(b.BranchingStateName),
                ContinuationStateId: ResolveId(b.ContinuationStateName),
                b.BranchType, b.RequiredCount, b.AllowCancelOnAnyReject, b.TimeoutMinutes,
                b.PositionX, b.PositionY, children));
        }
        return result;
    }

    /// <summary>
    /// Projects a branch-child assignment spec → the read DTO (reuses the transition assignment DTO
    /// shape). Null spec → null. Clause order preserved; Id-less (save response, FE refetches via GET).
    /// </summary>
    private static WorkflowTransitionAssignmentDto? ProjectChildAssignment(AssignmentSpec? a)
    {
        if (a is null) return null;
        var clauseDtos = new List<WorkflowTransitionAssignmentClauseDto>(a.Clauses.Count);
        foreach (var c in a.Clauses)
            clauseDtos.Add(new WorkflowTransitionAssignmentClauseDto(c.Mode, c.RefsJson));
        return new WorkflowTransitionAssignmentDto(a.CombineMode, a.AllowAssignorOverride, clauseDtos);
    }
}
