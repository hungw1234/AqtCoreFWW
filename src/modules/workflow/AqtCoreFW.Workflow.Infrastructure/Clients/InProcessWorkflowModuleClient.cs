using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Workflow.Application.Commands.ExecuteTransition;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Application.Validation;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Errors;
using MediatR;
using Microsoft.Extensions.Logging;
using Contract = AqtCoreFW.SharedKernel.Contracts.Clients;

namespace AqtCoreFW.Workflow.Infrastructure.Clients;

/// <summary>
/// In-process implementation of IWorkflowModuleClient.
/// - CreateInstanceAsync / CreateInstanceByDefinitionKeyAsync: repository write (Forms, Cases).
/// - ExecuteTransitionAsync: resolves (FromState, ActionName) → TransitionId, then delegates
///   to ExecuteTransitionCommandHandler via IMediator (DRY — parallel-branch + conditions
///   + permission-set authz reused for free).
/// - TryResolveInstanceForEntityAsync: tenant-scoped active-instance lookup for cross-module callers.
///
/// Phase-4b B4c:
///   - WorkflowInstance is T2: PublicId (Guid AK) used for external contract; Id (long) internal only.
///   - WorkflowState is T1: Id (long) only — no Guid AK.
///   - WorkflowInstanceResult.InitialStateId is Guid in contract (SharedKernel — cannot change).
///     WorkflowState has no Guid AK (T1), so Guid.Empty is returned.
///     InitialStateId is declared in contract but has no known consumer reading it (verified Phase-4b).
///     Tracked: Phase-4b B4c — vestigial field, candidate for contract cleanup Phase-4c.
///   - GetWithStatesAndTransitionsByInternalIdAsync used where only long FK is available.
/// </summary>
public sealed class InProcessWorkflowModuleClient(
    IWorkflowDefinitionRepository definitionRepo,
    IWorkflowInstanceRepository instanceRepo,
    IMediator mediator,
    IGuidGenerator gen,
    IWorkflowGraphValidator graphValidator,
    IWorkflowHandlerResolver handlerResolver,
    IWorkflowTaskFactory taskFactory,
    ILogger<InProcessWorkflowModuleClient> logger)
    : IWorkflowModuleClient
{
    /// <summary>
    /// Phase 5b: BP-path instance creation. Stages instance + (non-final initial) first task in
    /// one SaveChanges. WF_NO_HANDLER surfaces via <see cref="WorkflowInstanceResult.Fail"/>
    /// (Q5b ratification) — caller checks <see cref="WorkflowInstanceResult.IsSuccess"/>.
    /// Definition-not-found / no-initial-state still throw (misuse).
    /// </summary>
    public async Task<WorkflowInstanceResult> CreateInstanceAsync(
        CreateWorkflowInstanceRequest request, CancellationToken ct)
    {
        // request.DefinitionId = WorkflowDefinition.PublicId (Guid, T2 AK)
        var defResult = await definitionRepo.GetWithStatesAndTransitionsAsync(request.DefinitionId, ct);
        if (defResult.IsFailed)
            throw new InvalidOperationException(
                $"Workflow definition {request.DefinitionId} not found.");

        var definition = defResult.Value;
        var initialState = definition.States.FirstOrDefault(s => s.IsInitial)
            ?? throw new InvalidOperationException(
                $"Workflow definition {request.DefinitionId} has no initial state.");

        // Phase 5b: resolve first-task pool BEFORE staging any row. Empty pool / resolver throw
        // → Fail outcome with GOV_WFL_014. Atomicity: no orphan instance row.
        HandlerResolution resolution = HandlerResolution.Empty;
        if (!initialState.IsFinal)
        {
            var outgoing = await definitionRepo.GetOutgoingWithAssignmentsAsync(
                initialState.Id, ct);

            try
            {
                resolution = await handlerResolver.ResolveAsync(
                    initialState,
                    outgoing,
                    new WorkflowResolutionContext(request.TenantId, request.CreatedBy),
                    ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Resolver threw resolving initial state {StateId} during BP-path instance creation; failing closed.",
                    initialState.Id);
                return WorkflowInstanceResult.Fail(
                    WorkflowErrorCodes.NoHandlerResolved,
                    $"No eligible handler resolved for state '{initialState.Id}' — workflow misconfigured.");
            }

            if (resolution.IsEmpty)
                return WorkflowInstanceResult.Fail(
                    WorkflowErrorCodes.NoHandlerResolved,
                    $"No eligible handler resolved for state '{initialState.Id}' — workflow misconfigured.");
        }

        // WorkflowInstance.Create: gen for PublicId v7 + long IDs (T2 pattern).
        var instance = WorkflowInstance.Create(
            gen,                 // IGuidGenerator for PublicId v7 monotonic generation
            definition.Id,       // long internal PK (T2 WorkflowDefinition.Id)
            initialState.Id,     // long (T1 WorkflowState.Id)
            request.EntityType,
            request.EntityId,
            request.TenantId,
            request.CreatedBy,
            initialState.SlaHours,
            request.DossierId);   // null for non-BP callers → row stays in the unique active-entity index

        // PHR-V2 Phase 6 S4 / GATE-1 Path A: single SaveChanges via EF nav-prop propagation.
        //   Stage instance + task in same change-tracker scope. EF fills task.WorkflowInstanceId
        //   from the IDENTITY-generated instance.Id within one SaveChanges. S7 invariant restored.
        instanceRepo.Stage(instance);

        if (!initialState.IsFinal)
        {
            // Q3 ratification: task title = initialState.DisplayNameVi.
            // P6-DEFER-6: fall back to Name when DisplayNameVi is null/whitespace.
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

        var saveResult = await instanceRepo.SaveChangesAsync(ct);
        if (saveResult.IsFailed)
        {
            var firstErrCode = saveResult.Errors.Count > 0
                ? ExtractWorkflowErrorCode(saveResult.Errors[0]) ?? WorkflowErrorCodes.InstanceNotFound
                : WorkflowErrorCodes.InstanceNotFound;
            var firstErrMsg = saveResult.Errors.Count > 0
                ? saveResult.Errors[0].Message
                : "Failed to persist workflow instance.";
            return WorkflowInstanceResult.Fail(firstErrCode, firstErrMsg);
        }

        // WorkflowInstanceResult: InstanceId = PublicId (T2 Guid AK for external contract).
        // InitialStateId: WorkflowState is T1 (no Guid AK) — return Guid.Empty (vestigial, Phase-4c cleanup candidate).
        // InitialStateInternalId: real long PK consumed by BP Slice 2C handlers (state-form lookup).
        return new WorkflowInstanceResult(instance.PublicId, Guid.Empty, initialState.Id);
    }

    public async Task<WorkflowInstanceResult> CreateInstanceByInternalIdAsync(
        long workflowDefinitionId,
        string entityType,
        Guid entityId,
        Guid tenantId,
        Guid createdBy,
        Guid? dossierId = null,
        CancellationToken ct = default)
    {
        var defResult = await definitionRepo.GetWithStatesAndTransitionsByInternalIdAsync(workflowDefinitionId, ct);
        if (defResult.IsFailed)
            throw new InvalidOperationException(
                $"Workflow definition (internal Id={workflowDefinitionId}) not found.");

        return await CreateInstanceAsync(new CreateWorkflowInstanceRequest(
            DefinitionId: defResult.Value.PublicId,
            EntityType: entityType,
            EntityId: entityId,
            TenantId: tenantId,
            CreatedBy: createdBy,
            DossierId: dossierId), ct);
    }

    public async Task<WorkflowInstanceResult> CreateInstanceByDefinitionKeyAsync(
        string definitionKey, string entityType, Guid entityId,
        Guid tenantId, Guid createdBy, CancellationToken ct = default)
    {
        // Resolve latest version by key. GetLatestByKeyAsync returns the latest IsLatest=true row
        // for the tenant (falls back to global template when TenantId=Guid.Empty).
        var defResult = await definitionRepo.GetLatestByKeyAsync(definitionKey, tenantId, ct);
        if (defResult.IsFailed)
            throw new InvalidOperationException(
                $"Workflow definition '{definitionKey}' not found for tenant {tenantId}.");

        // Pass PublicId (T2 Guid AK) as DefinitionId for CreateInstanceAsync contract
        return await CreateInstanceAsync(new CreateWorkflowInstanceRequest(
            DefinitionId: defResult.Value.PublicId,
            EntityType: entityType,
            EntityId: entityId,
            TenantId: tenantId,
            CreatedBy: createdBy), ct);
    }

    public async Task<Guid?> TryResolveInstanceForEntityAsync(
        string entityType, Guid entityId, Guid tenantId, CancellationToken ct = default)
    {
        // Active-only (CompletedAt IS NULL) per GetActiveInstancesByEntityAsync semantics.
        // Repo does NOT filter by TenantId — we filter here for cross-tenant isolation.
        var instances = await instanceRepo.GetActiveInstancesByEntityAsync(entityType, entityId, ct);
        var scoped = instances.Where(i => i.TenantId == tenantId).ToList();

        if (scoped.Count == 0) return null;

        if (scoped.Count > 1)
        {
            // Data-integrity guard: UX_WorkflowInstances_Active_Entity unique index
            // (migration 20260429110600) prevents new dupes; legacy rows may still trigger this branch.
            // Log with PublicIds (Guid AK) for ops visibility.
            logger.LogWarning(
                "CaseWorkflowInstanceDuplicate: {Count} active WorkflowInstances for {EntityType}/{EntityId} tenant {TenantId}. PublicIds: {Ids}",
                scoped.Count, entityType, entityId, tenantId,
                string.Join(",", scoped.Select(i => i.PublicId)));
        }

        // Return PublicId (Guid AK) — that's what the cross-module contract expects
        return scoped.OrderBy(i => i.StartedAt).First().PublicId;
    }

    public async Task<Contract.ExecuteTransitionOutcome> ExecuteTransitionAsync(
        Contract.ExecuteTransitionRequest request, CancellationToken ct = default)
    {
        // 1. Load instance by PublicId (Guid AK — cross-module contract uses Guid)
        var instanceResult = await instanceRepo.GetWithHistoryAsync(request.InstanceId, ct);
        if (instanceResult.IsFailed)
            return Contract.ExecuteTransitionOutcome.Fail(
                WorkflowErrorCodes.InstanceNotFound,
                $"WorkflowInstance '{request.InstanceId}' not found.");

        var instance = instanceResult.Value;
        if (instance.TenantId != request.TenantId)
            return Contract.ExecuteTransitionOutcome.Fail(
                WorkflowErrorCodes.InstanceNotFound,
                $"WorkflowInstance '{request.InstanceId}' not found.");

        // 2. Load definition by internal long Id (instance.DefinitionId is long FK)
        var defResult = await definitionRepo.GetWithStatesAndTransitionsByInternalIdAsync(
            instance.DefinitionId, ct);
        if (defResult.IsFailed)
            return Contract.ExecuteTransitionOutcome.Fail(
                WorkflowErrorCodes.DefinitionNotFound,
                $"WorkflowDefinition for instance '{request.InstanceId}' not found.");

        var definition = defResult.Value;

        // 3. Resolve transition by (FromState long, ActionName). No match → InvalidTransition.
        var transition = definition.Transitions.FirstOrDefault(t =>
            t.FromStateId == instance.CurrentStateId && t.ActionName == request.ActionName);
        if (transition is null)
            return Contract.ExecuteTransitionOutcome.Fail(
                WorkflowErrorCodes.InvalidTransition,
                $"Action '{request.ActionName}' is not a valid transition from current state.");

        // 4. Delegate to existing handler — authz + conditions + parallel-branch owned there.
        //    TransitionId is long (T1 WorkflowTransition.Id).
        var command = new ExecuteTransitionCommand(
            InstanceId: request.InstanceId,    // Guid (T2 PublicId — handler uses GetWithHistoryAsync)
            TransitionId: transition.Id,        // long (T1 PK)
            ActorId: request.ActorId,
            Comment: request.Comment,
            Metadata: request.Metadata,
            FileIds: request.FileIds,
            NextHandlerUserId: request.NextHandlerUserId); // Slice 10a: thread 3-layer override (DDTO-DEFAULT)

        var handlerResult = await mediator.Send(command, ct);

        if (handlerResult.IsFailed)
        {
            var first = handlerResult.Errors.Count > 0 ? handlerResult.Errors[0] : null;
            var code = ExtractWorkflowErrorCode(first) ?? WorkflowErrorCodes.InvalidTransition;
            var message = first?.Message ?? "Transition failed.";
            return Contract.ExecuteTransitionOutcome.Fail(code, message);
        }

        // 5. Map success — look up target state from loaded definition (long Id comparison).
        var targetState = definition.States.First(s => s.Id == transition.ToStateId);
        // ExecuteTransitionResult.NewStateId is Guid in contract (SharedKernel — cannot change).
        // WorkflowState is T1 (no Guid AK) — return Guid.Empty.
        // No known consumer reads NewStateId from the outcome (verified Phase-4b B4c).
        // Candidate for contract cleanup Phase-4c.
        return Contract.ExecuteTransitionOutcome.Success(new Contract.ExecuteTransitionResult(
            NewStateId: Guid.Empty,
            NewStateName: targetState.Name,
            IsFinal: targetState.IsFinal,
            NewStateInternalId: targetState.Id));
    }

    public async Task<Contract.TransitionLookup?> ResolveTransitionIdAsync(
        Guid instanceId, string actionName, Guid tenantId, CancellationToken ct = default)
    {
        var instanceResult = await instanceRepo.GetByIdAsync(instanceId, ct);
        if (instanceResult.IsFailed) return null;

        var instance = instanceResult.Value;
        if (instance.TenantId != tenantId) return null;

        var defResult = await definitionRepo.GetWithStatesAndTransitionsByInternalIdAsync(
            instance.DefinitionId, ct);
        if (defResult.IsFailed) return null;

        var transition = defResult.Value.Transitions.FirstOrDefault(t =>
            t.FromStateId == instance.CurrentStateId && t.ActionName == actionName);
        return transition is null
            ? null
            : new Contract.TransitionLookup(transition.Id, transition.FromStateId);
    }

    /// <summary>
    /// P2 (BP-Run R14): look up tenant of an instance by PublicId for cross-tenant guard.
    /// Returns null when not found; otherwise returns the owning TenantId for caller comparison.
    /// </summary>
    public async Task<Guid?> GetTenantForInstanceAsync(Guid instanceId, CancellationToken ct = default)
    {
        var result = await instanceRepo.GetByIdAsync(instanceId, ct);
        if (result.IsFailed) return null;
        return result.Value.TenantId;
    }

    /// <summary>
    /// BP MVP P7: resolve WorkflowDefinition PublicId → internal bigint Id for tenant.
    /// Reuses GetWithStatesAndTransitionsAsync (same repo call used by CreateInstanceAsync) —
    /// KISS: no new repo method needed on this cold admin path.
    /// Returns null for not-found OR cross-tenant (no existence leak).
    /// </summary>
    public async Task<long?> TryResolveDefinitionInternalIdAsync(
        Guid definitionPublicId, Guid tenantId, CancellationToken ct = default)
    {
        var result = await definitionRepo.GetWithStatesAndTransitionsAsync(definitionPublicId, ct);
        if (result.IsFailed) return null;
        var def = result.Value;
        // Cross-tenant: return null without leaking existence
        if (def.TenantId != tenantId) return null;
        return def.Id;
    }

    /// <summary>
    /// BP MVP P7-FU1 Phase B: returns the set of valid (StateId, TransitionId) longs for a definition,
    /// tenant-scoped. Reuses GetWithStatesAndTransitionsByInternalIdAsync (already available, KISS).
    /// Returns null for not-found OR cross-tenant — no existence leak.
    /// </summary>
    public async Task<Contract.WorkflowDefinitionStructure?> GetDefinitionStructureAsync(
        long workflowDefinitionId, Guid tenantId, CancellationToken ct = default)
    {
        var result = await definitionRepo.GetWithStatesAndTransitionsByInternalIdAsync(workflowDefinitionId, ct);
        if (result.IsFailed) return null;
        var def = result.Value;
        // Cross-tenant: return null without leaking existence
        if (def.TenantId != tenantId) return null;
        return new Contract.WorkflowDefinitionStructure(
            StateIds: def.States.Select(s => s.Id).ToHashSet(),
            TransitionIds: def.Transitions.Select(t => t.Id).ToHashSet());
    }

    /// <summary>
    /// BP MVP P7-FU1.2: long → Guid PublicId resolver, tenant-scoped. Reuses
    /// GetWithStatesAndTransitionsByInternalIdAsync (same EF call used by GetDefinitionStructureAsync).
    /// Returns null for not-found OR cross-tenant — no existence leak.
    /// </summary>
    public async Task<Guid?> TryGetDefinitionPublicIdAsync(
        long workflowDefinitionInternalId, Guid tenantId, CancellationToken ct = default)
    {
        var result = await definitionRepo.GetWithStatesAndTransitionsByInternalIdAsync(workflowDefinitionInternalId, ct);
        if (result.IsFailed) return null;
        var def = result.Value;
        if (def.TenantId != tenantId) return null;
        return def.PublicId;
    }

    /// <summary>
    /// Phase 5c: runs design-time R1-R4 graph validation on the definition identified by
    /// its internal bigint Id. Uses GetForValidationAsync (tracking load — States + Transitions +
    /// Assignments + Clauses) so R3 canonicalization rewrites are persisted via SaveChangesAsync
    /// on the happy path (Option A: persist only when graph is valid — no SaveChanges on failure).
    /// Architecture: BP module calls this via IWorkflowModuleClient — never references
    /// Workflow.Application or Workflow.Domain directly.
    /// </summary>
    public async Task<WorkflowGraphValidationOutcome> ValidateDefinitionGraphAsync(
        long workflowDefinitionId,
        CancellationToken ct = default)
    {
        var (definition, assignmentByTransitionId) = await definitionRepo.GetForValidationAsync(
            workflowDefinitionId, ct);

        if (definition is null)
        {
            return WorkflowGraphValidationOutcome.Fail(
            [
                new WorkflowGraphViolationDto("NOT_FOUND", null, null,
                    $"WorkflowDefinition '{workflowDefinitionId}' not found.")
            ]);
        }

        // Branch-aware Activate validation needs the parallel branches (R1 exemptions + RB5 pool check).
        var branches = await definitionRepo.GetParallelBranchesForValidationAsync(workflowDefinitionId, ct);

        var report = await graphValidator.ValidateForActivateAsync(
            definition.States,
            definition.Transitions,
            assignmentByTransitionId,
            definition.TenantId,
            branches: branches,
            ct: ct);

        if (report.IsValid)
        {
            // Option A: persist canonical rewrites only on success.
            // SaveChangesAsync writes any R3 canonicalization mutations (clause.RefsJson rewritten
            // in-memory by the validator) to the WF database.
            // BP context is not involved — this is a standalone WF-side write within the IWorkflowModuleClient
            // call. BP's own SaveChanges (UpdateAsync on the BusinessProcess aggregate) runs separately.
            await definitionRepo.SaveChangesAsync(ct);
            return WorkflowGraphValidationOutcome.Ok();
        }

        var dtos = report.Violations
            .Select(v => new WorkflowGraphViolationDto(v.Rule, v.StateName, v.TransitionActionName, v.Detail))
            .ToList();

        return WorkflowGraphValidationOutcome.Fail(dtos);
    }

    /// <summary>
    /// Extract the GOV_WFL_NNN code attached by WorkflowErrors factories.
    /// Null when the error came from another layer (e.g. repository) without a structured code.
    /// </summary>
    private static string? ExtractWorkflowErrorCode(FluentResults.IError? error)
    {
        if (error is null) return null;
        return error.Metadata.TryGetValue("WorkflowErrorCode", out var value)
            ? value as string
            : null;
    }
}
