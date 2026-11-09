using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Application.Validation;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.ToggleDefinitionActive;

/// <summary>
/// Phase 5c: on Activate (IsActive=true), runs R1-R4 graph validation BEFORE calling
/// definition.Activate. Uses GetForValidationAsync (tracking load — States + Transitions +
/// Assignments + Clauses in one scope) so R3 canonicalization rewrites are persisted via
/// UpdateAsync's SaveChanges on the happy path (Option A: persist only when graph is valid).
/// Deactivation skips validation entirely and uses the lighter GetWithStatesAndTransitionsAsync.
/// </summary>
public sealed class ToggleDefinitionActiveCommandHandler(
    IWorkflowDefinitionRepository repository,
    IWorkflowGraphValidator graphValidator)
    : IRequestHandler<ToggleDefinitionActiveCommand, Result>
{
    public async Task<Result> Handle(
        ToggleDefinitionActiveCommand request,
        CancellationToken cancellationToken)
    {
        return request.IsActive
            ? await HandleActivateAsync(request, cancellationToken)
            : await HandleDeactivateAsync(request, cancellationToken);
    }

    // Activate path: single load via GetForValidationAsync (tracking, full graph).
    // Guard checks (tenant, initial state) run against the loaded definition.
    // R3 canonicalization rewrites accumulate in tracked Clause entities.
    // UpdateAsync (existing call) triggers SaveChanges — persists both Activate mutation
    // AND canonical rewrites atomically.
    private async Task<Result> HandleActivateAsync(
        ToggleDefinitionActiveCommand request,
        CancellationToken ct)
    {
        // Resolve PublicId → internal long Id required by GetForValidationAsync.
        // YAGNI: don't add a PublicId-overload on GetForValidationAsync for one caller.
        var resolveResult = await repository.GetWithStatesAndTransitionsAsync(
            request.DefinitionId, ct);

        if (resolveResult.IsFailed)
            return Result.Fail(WorkflowErrors.DefinitionNotFound(request.DefinitionId));

        var resolved = resolveResult.Value;
        if (resolved.TenantId != request.TenantId)
            return Result.Fail(WorkflowErrors.DefinitionNotFound(request.DefinitionId));

        if (!resolved.States.Any(s => s.IsInitial))
            return Result.Fail(WorkflowErrors.NoInitialState());

        // Full tracking load for R3 canonicalization. Uses the internal long Id from the
        // resolve step above (T2 entity — long Id is internal PK).
        var (definition, assignmentByTransitionId) = await repository.GetForValidationAsync(
            resolved.Id, ct);

        // Defensive: should never be null since we just resolved it above, but guard anyway.
        if (definition is null)
            return Result.Fail(WorkflowErrors.DefinitionNotFound(request.DefinitionId));

        // Branch-aware Activate validation needs the parallel branches (R1 exemptions + RB5 pool check).
        // NoTracking load — Activate does not canonicalize branch-child clauses (Save already did).
        var branches = await repository.GetParallelBranchesForValidationAsync(resolved.Id, ct);

        // Phase 5c R1-R4: graph validation (R3 also mutates clause.RefsJson in-memory).
        // Pass definition.TenantId (authoritative from DB load) — tenantId guard above ensures request.TenantId == definition.TenantId.
        var graphReport = await graphValidator.ValidateForActivateAsync(
            definition.States,
            definition.Transitions,
            assignmentByTransitionId,
            definition.TenantId,
            branches: branches,
            ct: ct);

        if (!graphReport.IsValid)
            return Result.Fail(WorkflowErrors.GraphValidationFailed(graphReport.Violations));

        // Happy path: activate and persist (single SaveChanges via UpdateAsync).
        // UpdateAsync calls Context.SaveChangesAsync — persists both Activate mutation
        // AND any R3 canonical rewrites accumulated by the validator.
        definition.Activate(request.ModifiedBy);
        await repository.UpdateAsync(definition, ct);
        return Result.Ok();
    }

    // Deactivate path: lightweight load — no assignment/clause load needed.
    private async Task<Result> HandleDeactivateAsync(
        ToggleDefinitionActiveCommand request,
        CancellationToken ct)
    {
        var result = await repository.GetWithStatesAndTransitionsAsync(
            request.DefinitionId, ct);

        if (result.IsFailed)
            return Result.Fail(WorkflowErrors.DefinitionNotFound(request.DefinitionId));

        var definition = result.Value;
        if (definition.TenantId != request.TenantId)
            return Result.Fail(WorkflowErrors.DefinitionNotFound(request.DefinitionId));

        definition.Deactivate(request.ModifiedBy);
        await repository.UpdateAsync(definition, ct);
        return Result.Ok();
    }
}
