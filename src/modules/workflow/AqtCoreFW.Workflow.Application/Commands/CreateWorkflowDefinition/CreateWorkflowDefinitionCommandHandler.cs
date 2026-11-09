using System.Transactions;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Workflow.Application.Commands.CreateWorkflowDefinition;

public sealed class CreateWorkflowDefinitionCommandHandler(
    IWorkflowDefinitionRepository repository,
    IGuidGenerator gen)
    : IRequestHandler<CreateWorkflowDefinitionCommand, Result<WorkflowDefinitionDto>>
{
    public async Task<Result<WorkflowDefinitionDto>> Handle(
        CreateWorkflowDefinitionCommand request,
        CancellationToken cancellationToken)
    {
        // Validate: exactly one initial state required
        var initialCount = request.States.Count(s => s.IsInitial);
        if (initialCount != 1)
            return Result.Fail(WorkflowErrors.NoInitialState());

        // Validate: no duplicate state names
        var duplicateState = request.States
            .GroupBy(s => s.Name)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicateState is not null)
            return Result.Fail(WorkflowErrors.DuplicateStateName(duplicateState.Key));

        // Pass 1 — validate transition state name references + duplicate transitions
        // using name keys only (IDs are not assigned yet — states are IDENTITY bigints).
        var stateNames = request.States
            .Select(s => s.Name)
            .ToHashSet(StringComparer.Ordinal);

        // Tuple equality is case-sensitive struct equality — no custom comparer needed.
        var seenTransitions = new HashSet<(string FromName, string ActionName)>();

        foreach (var t in request.Transitions)
        {
            if (!stateNames.Contains(t.FromStateName))
                return Result.Fail(new Error($"FromState '{t.FromStateName}' not found in definition."));
            if (!stateNames.Contains(t.ToStateName))
                return Result.Fail(new Error($"ToState '{t.ToStateName}' not found in definition."));

            var key = (t.FromStateName, t.ActionName);
            if (!seenTransitions.Add(key))
                return Result.Fail(new Error(
                    $"Transition '{t.ActionName}' from state '{t.FromStateName}' already exists."));
        }

        var definition = WorkflowDefinition.Create(
            gen,
            request.Name,
            request.Description,
            request.TenantId,
            request.CreatedBy,
            entityType: request.EntityType);

        // Pass 2 — add states to aggregate and persist.
        // repository.AddAsync calls SaveChangesAsync → DB assigns IDENTITY bigint IDs.
        foreach (var s in request.States)
        {
            var state = WorkflowState.Create(
                definition.Id, s.Name, s.DisplayNameVi, s.DisplayNameEn,
                s.IsInitial, s.IsFinal, s.Color, s.SortOrder,
                s.SlaHours, s.AutoTransitionOnTimeoutId,
                s.PositionX, s.PositionY);
            definition.AddState(state);
        }

        // P1 #1 — wrap both saves in a single ambient transaction so that a failure
        // between Pass-2 (AddAsync) and Pass-3 (UpdateAsync) rolls back the entire unit.
        // TransactionScopeAsyncFlowOption.Enabled is required for async/await support
        // (System.Transactions flows the ambient tx across await boundaries).
        // SQL Server EF Core enlists automatically; InMemory provider silently no-ops.
        using var scope = new TransactionScope(
            TransactionScopeOption.Required,
            new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
            TransactionScopeAsyncFlowOption.Enabled);

        await repository.AddAsync(definition, cancellationToken);

        // Pass 3 — build name→Id map from saved states (IDs are now non-zero bigints).
        // Then wire transitions with correct FK values and persist again.
        var stateIdByName = definition.States
            .ToDictionary(s => s.Name, s => s.Id, StringComparer.Ordinal);

        foreach (var t in request.Transitions)
        {
            // P1 #2 — TryGetValue instead of indexer to guard against state drift
            // (e.g. state added to aggregate but absent from original request.States).
            // Indexer would throw KeyNotFoundException → unhandled 500.
            if (!stateIdByName.TryGetValue(t.FromStateName, out var fromId))
                return Result.Fail(new ValidationError(
                    $"Transition references unknown state '{t.FromStateName}'.",
                    nameof(t.FromStateName)));
            if (!stateIdByName.TryGetValue(t.ToStateName, out var toId))
                return Result.Fail(new ValidationError(
                    $"Transition references unknown state '{t.ToStateName}'.",
                    nameof(t.ToStateName)));

            var transition = WorkflowTransition.Create(
                definition.Id, fromId, toId,
                t.ActionName, t.ActionLabelVi, t.ActionLabelEn,
                t.ConditionsJson, t.SortOrder, t.IsRecall,
                t.SourceHandle, t.TargetHandle);
            definition.AddTransition(transition);
        }

        await repository.UpdateAsync(definition, cancellationToken);

        scope.Complete();
        return Result.Ok(MapToDto(definition));
    }

    internal static WorkflowDefinitionDto MapToDto(WorkflowDefinition d) => new(
        d.PublicId, d.Name, d.Description, d.IsActive, d.TenantId, d.CreatedAt,
        d.DefinitionKey, d.Version, d.IsLatest, d.EntityType,
        d.States.Select(s => new WorkflowStateDto(
            s.Id, s.Name, s.DisplayNameVi, s.DisplayNameEn,
            s.IsInitial, s.IsFinal, s.Color, s.SortOrder,
            s.SlaHours, s.AutoTransitionOnTimeoutId)).ToList(),
        d.Transitions.Select(t => new WorkflowTransitionDto(
            t.Id, t.FromStateId, t.ToStateId, t.ActionName,
            t.ActionLabelVi, t.ActionLabelEn,
            t.ConditionsJson, t.SortOrder, t.IsRecall)).ToList(),
        // A freshly created/cloned definition has no parallel branches yet (authored later via SaveGraph).
        []);
}
