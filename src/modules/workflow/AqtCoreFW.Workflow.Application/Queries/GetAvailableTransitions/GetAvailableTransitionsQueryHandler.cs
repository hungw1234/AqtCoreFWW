using System.Diagnostics;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Application.Telemetry;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Workflow.Application.Queries.GetAvailableTransitions;

public sealed class GetAvailableTransitionsQueryHandler(
    IReadDbConnection db,
    IIdentityModuleClient identityClient,
    IOrgUnitModuleClient orgUnitClient,
    IWorkflowDefinitionRepository definitionRepository,
    IWorkflowHandlerResolver resolver,
    ILogger<GetAvailableTransitionsQueryHandler> logger)
    : IRequestHandler<GetAvailableTransitionsQuery, Result<IReadOnlyList<WorkflowTransitionDto>>>
{
    public async Task<Result<IReadOnlyList<WorkflowTransitionDto>>> Handle(
        GetAvailableTransitionsQuery request,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            return await HandleCore(request, cancellationToken);
        }
        finally
        {
            sw.Stop();
            WorkflowMetrics.AllowedTransitionsQueryDurationMs.Record(
                sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("tenant", request.TenantId));
        }
    }

    private async Task<Result<IReadOnlyList<WorkflowTransitionDto>>> HandleCore(
        GetAvailableTransitionsQuery request,
        CancellationToken cancellationToken)
    {
        // First resolve the instance's current state and definition.
        // T2 WorkflowInstance: PublicId = Guid AK (external), Id = long bigint PK (internal FK base).
        // CurrentStateId (long) + DefinitionId (long) are bigint FKs — must pass long to transition query.
        // M2 fix: CreatedBy selected so InitiatorUserId matches ExecuteTransition (symmetric Initiator-mode authz).
        const string instanceSql = """
            SELECT Id AS InternalId, CurrentStateId, DefinitionId, CreatedBy
            FROM workflow.WorkflowInstances
            WHERE PublicId = @InstanceId AND TenantId = @TenantId AND IsDeleted = 0
            """;

        var instance = await db.QueryFirstOrDefaultAsync<InstanceStateRow>(
            instanceSql, new { request.InstanceId, request.TenantId }, cancellationToken);

        if (instance is null)
            return Result.Fail(new NotFoundError($"WorkflowInstance '{request.InstanceId}' not found."));

        // Fetch transitions from current state — permission filter applied in memory
        // (avoids dynamic SQL; permission codes are short strings, set is small)
        // Slice 8a: SourceHandle + TargetHandle MUST be selected to match WorkflowTransitionDto's
        // 11-param positional ctor. Dapper does NOT support optional-param ctors with partial
        // column coverage — the column COUNT must align with one of the DTO's ctor signatures.
        const string transitionSql = """
            SELECT Id, FromStateId, ToStateId, ActionName, ActionLabelVi, ActionLabelEn,
                   ConditionsJson, SortOrder, IsRecall, SourceHandle, TargetHandle
            FROM workflow.WorkflowTransitions
            WHERE DefinitionId = @DefinitionId
              AND FromStateId  = @CurrentStateId
            ORDER BY SortOrder
            """;

        var all = (await db.QueryAsync<WorkflowTransitionDto>(
            transitionSql,
            new { instance.DefinitionId, instance.CurrentStateId },
            cancellationToken)).ToList();

        // Slice-5: resolver-based eligibility filter (corrected D1/D5 semantic).
        // A transition T is visible iff the actor satisfies T's OWN assignment rule
        // (T is outgoing from the CURRENT state — symmetric with authz-of-T in ExecuteTransition).
        // Final-target transitions receive no special treatment: resolve T's own rule uniformly.
        var defResult = await definitionRepository.GetWithStatesAndTransitionsByInternalIdAsync(
            instance.DefinitionId, cancellationToken);

        if (defResult.IsFailed)
            // Definition not found → fail-CLOSED: hide all transitions rather than allow-through.
            return Result.Ok((IReadOnlyList<WorkflowTransitionDto>)[]);

        // currentState needed as arg to resolver.Resolve (used for IsRecall / AutoTimeout exclusions)
        var currentState = defResult.Value.States.FirstOrDefault(s => s.Id == instance.CurrentStateId);
        if (currentState is null)
            return Result.Ok((IReadOnlyList<WorkflowTransitionDto>)[]);

        // Load outgoing assignments for the current state ONCE (2-query + join in repo)
        var currentOutgoing = await definitionRepository.GetOutgoingWithAssignmentsAsync(
            instance.CurrentStateId, cancellationToken);

        // Load all four principal sets (permission/role/group/org-unit) so ByRole/ByGroup/ByOrgUnit
        // transitions are visible to their holders — in parity with inbox/claim eligibility.
        var actor = await ActorPrincipalsLoader.LoadAsync(
            identityClient, orgUnitClient, request.UserId, request.TenantId, cancellationToken);

        // M2 fix: use instance.CreatedBy as InitiatorUserId to match ExecuteTransition's semantic.
        // Previously passed request.UserId, causing Initiator-mode rules to show a transition
        // for any querying user when Execute would 403 for non-creators (UI/exec divergence).
        var ctx = new WorkflowResolutionContext(request.TenantId, instance.CreatedBy, instance.InternalId);
        var oaByTransitionId = currentOutgoing.ToDictionary(o => o.Transition.Id);

        var filtered = new List<WorkflowTransitionDto>();
        foreach (var t in all)
        {
            // Recall transitions bypass the resolver gate: their authz model is the instance-creator
            // guard in ExecuteTransitionCommandHandler (:95-98). WorkflowHandlerResolver intentionally
            // excludes IsRecall by design (WorkflowHandlerResolver.cs:32), so resolver would return
            // Empty for any recall T → it would be incorrectly hidden pre-Slice-5 behavior restored.
            if (t.IsRecall)
            {
                filtered.Add(t);
                continue;
            }

            // Find T's own OA from the current state's outgoing set.
            // Transition not found in OA set = no assignment row → fail-CLOSED (hide).
            if (!oaByTransitionId.TryGetValue(t.Id, out var tOA))
                continue;

            try
            {
                var resolution = await resolver.ResolveAsync(
                    currentState,
                    new List<OutgoingAssignment> { tOA },
                    ctx,
                    cancellationToken);

                if (ResolverAuthz.IsActorAuthorized(resolution, actor, request.UserId))
                    filtered.Add(t);
                // else: actor not authorized → transition hidden (fail-CLOSED)
            }
            catch (Exception ex)
            {
                // L1 fix: log resolver throw at Warning (was bare catch with no diagnostic).
                // Transition hidden (fail-CLOSED) — same behavior, now observable.
                logger.LogWarning(ex,
                    "Resolver threw evaluating transition {TransitionId} visibility; hiding (fail-CLOSED).",
                    t.Id);
            }
        }

        return Result.Ok((IReadOnlyList<WorkflowTransitionDto>)filtered);
    }

    /// <summary>
    /// Internal projection row for the instance state/definition lookup.
    /// T2 WorkflowInstance: InternalId = long bigint PK; CurrentStateId + DefinitionId = long bigint FKs.
    /// Class form required — positional records do not support Dapper column aliasing (InternalId alias).
    /// </summary>
    internal sealed class InstanceStateRow
    {
        public long InternalId { get; set; }       // WorkflowInstance.Id — not used downstream but projected for completeness
        public long CurrentStateId { get; set; }   // bigint FK to WorkflowStates.Id
        public long DefinitionId { get; set; }     // bigint FK to WorkflowDefinitions.Id
        public Guid CreatedBy { get; set; }        // M2: instance creator — used as InitiatorUserId in resolution context
    }
}
