using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Workflow.Application.Commands.CreateWorkflowInstance;

/// <summary>
/// Phase 5b: stages a new <see cref="WorkflowInstance"/> AND its first <see cref="WorkflowTask"/>
/// (for non-final initial states) within a single SaveChanges (S7 invariant). Empty-pool initial
/// states are hard-blocked with <see cref="WorkflowErrors.NoHandlerResolved"/> BEFORE any row is
/// staged — atomicity preserved, no orphan instance.
/// </summary>
public sealed class CreateWorkflowInstanceCommandHandler(
    IWorkflowDefinitionRepository definitionRepository,
    IWorkflowInstanceRepository instanceRepository,
    IWorkflowAssignmentResolver assignmentResolver,
    IWorkflowHandlerResolver handlerResolver,
    IWorkflowTaskFactory taskFactory,
    IGuidGenerator gen,
    ILogger<CreateWorkflowInstanceCommandHandler> logger)
    : IRequestHandler<CreateWorkflowInstanceCommand, Result<WorkflowInstanceDto>>
{
    public async Task<Result<WorkflowInstanceDto>> Handle(
        CreateWorkflowInstanceCommand request,
        CancellationToken cancellationToken)
    {
        // Auto-resolve definition when not explicitly provided
        var definitionPublicId = request.DefinitionId;
        if (definitionPublicId is null || definitionPublicId == Guid.Empty)
        {
            var resolveResult = await assignmentResolver.ResolveAsync(
                request.TenantId, request.EntityType, request.EntitySubType, cancellationToken);
            if (resolveResult.IsFailed)
                return Result.Fail(resolveResult.Errors);
            definitionPublicId = resolveResult.Value;
        }

        // Load definition with states to find initial state (lookup by PublicId)
        var defResult = await definitionRepository.GetWithStatesAndTransitionsAsync(
            definitionPublicId.Value, cancellationToken);
        if (defResult.IsFailed)
            return Result.Fail(defResult.Errors);

        var definition = defResult.Value;

        var initialState = definition.States.FirstOrDefault(s => s.IsInitial);
        if (initialState is null)
            return Result.Fail(WorkflowErrors.NoInitialState());

        // Phase 5b: resolve first-task pool BEFORE staging any row.
        // Final initial state: degenerate workflow — no task created; instance staged + saved.
        // Non-final: load outgoing-with-assignments, resolve via IWorkflowHandlerResolver, fail-CLOSED
        // on resolver exception or empty pool (no orphan instance — atomicity).
        HandlerResolution resolution = HandlerResolution.Empty;
        if (!initialState.IsFinal)
        {
            var outgoing = await definitionRepository.GetOutgoingWithAssignmentsAsync(
                initialState.Id, cancellationToken);

            try
            {
                resolution = await handlerResolver.ResolveAsync(
                    initialState,
                    outgoing,
                    new WorkflowResolutionContext(request.TenantId, request.CreatedBy),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                // Fail-CLOSED: any resolver exception (NotSupportedException for deferred clause modes,
                // misconfigured Intersect combine, etc.) → 422 WF_NO_HANDLER, never 500.
                logger.LogWarning(ex,
                    "Resolver threw resolving initial state {StateId} during instance creation; failing closed.",
                    initialState.Id);
                return Result.Fail(WorkflowErrors.NoHandlerResolved(initialState.Id));
            }

            if (resolution.IsEmpty)
                return Result.Fail(WorkflowErrors.NoHandlerResolved(initialState.Id));
        }

        // WorkflowInstance.Create: first arg is IGuidGenerator, then long definitionId, long initialStateId
        var instance = WorkflowInstance.Create(
            gen,
            definition.Id,      // long internal PK (bigint)
            initialState.Id,    // long internal PK (bigint)
            request.EntityType,
            request.EntityId,
            request.TenantId,
            request.CreatedBy,
            request.SlaHours);

        // PHR-V2 Phase 6 S4 / GATE-1 Path A: single SaveChanges via EF nav-prop propagation.
        //   Stage instance + task in same change-tracker scope. Task's WorkflowInstance navigation
        //   property holds the staged instance reference; EF fills WorkflowInstanceId from the
        //   IDENTITY-generated instance.Id between the two INSERTs within a single SaveChanges.
        //   S7 single-SaveChanges invariant fully restored; no explicit TransactionScope needed.
        instanceRepository.Stage(instance);

        if (!initialState.IsFinal)
        {
            // Q3 ratification: task title = initialState.DisplayNameVi.
            // overrideAssignee=null at creation (no NextHandlerUserId on creation API).
            // P6-DEFER-6: fall back to Name when DisplayNameVi is null/whitespace
            // (definition validator already enforces non-null at save; defensive only).
            var taskTitle = !string.IsNullOrWhiteSpace(initialState.DisplayNameVi)
                ? initialState.DisplayNameVi
                : initialState.Name;
            taskFactory.StageFirstOrNextTask(
                instance: instance,
                title: taskTitle,
                tenantId: request.TenantId,
                createdBy: request.CreatedBy,
                overrideAssignee: null,
                resolution: resolution);
        }

        var saveResult = await instanceRepository.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailed)
            return Result.Fail(saveResult.Errors);

        return Result.Ok(MapToDto(instance));
    }

    private static WorkflowInstanceDto MapToDto(WorkflowInstance i) => new(
        i.PublicId,        // T2: expose PublicId as the "Id" in DTO
        i.DefinitionId,    // long internal FK
        i.EntityType,
        i.EntityId,
        i.CurrentStateId,  // long internal FK
        i.TenantId,
        i.StartedAt,
        i.CompletedAt,
        i.DueAt,
        i.CurrentStateEnteredAt,
        []);
}
