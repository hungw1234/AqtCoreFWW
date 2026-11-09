using AqtCoreFW.BusinessProcess.Application.Caching;
using AqtCoreFW.BusinessProcess.Domain.Errors;
using AqtCoreFW.BusinessProcess.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;
using WorkflowCodes = AqtCoreFW.SharedKernel.Contracts.Clients.WorkflowErrorCodes;

namespace AqtCoreFW.BusinessProcess.Application.Commands.ActivateBusinessProcess;

/// <summary>
/// Pattern A (R13): strict tenant guard pre-DB; repo guards on UpdateAsync (defense in depth).
/// Aggregate's Activate emits <c>BusinessProcessActivatedEvent</c> only on real state change — OutboxInterceptor
/// then writes it to <c>[businessprocess].OutboxMessages</c> in the same transaction as the IsActive flip.
/// Cache invalidation is broad-prefix per tenant.
/// Phase 5c: design-time R1-R4 graph validation runs via IWorkflowModuleClient.ValidateDefinitionGraphAsync
/// AFTER intake form check, BEFORE bp.Activate — architecture rule: BP never references Workflow.Application directly.
/// </summary>
public sealed class ActivateBusinessProcessCommandHandler(
    IBusinessProcessRepository repository,
    IFormTemplateQueryClient formTemplateClient,
    IGuidGenerator gen,
    ITenantContext tenantContext,
    ICacheService cache,
    IWorkflowModuleClient workflowClient,
    ILogger<ActivateBusinessProcessCommandHandler> logger)
    : IRequestHandler<ActivateBusinessProcessCommand, Result>
{
    public async Task<Result> Handle(ActivateBusinessProcessCommand request, CancellationToken cancellationToken)
    {
        if (request.TenantId != tenantContext.TenantId)
        {
            logger.LogWarning(
                "Cross-tenant ActivateBusinessProcess denied: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}",
                tenantContext.TenantId, request.TenantId);
            return Result.Fail(new ValidationError("Cross-tenant access denied"));
        }

        var bp = await repository.GetByPublicIdAsync(request.BusinessProcessPublicId, cancellationToken);
        if (bp is null)
            return Result.Fail(new NotFoundError(
                $"{BusinessProcessErrorCodes.NotFound}: BusinessProcess {request.BusinessProcessPublicId} không tìm thấy."));

        // Fail-fast: don't activate a BP whose pinned intake form has no published snapshot —
        // it would only fail for the end-user at run time (GOV_SYS_001). Root-caused 2026-05-18.
        // GOV_BP_010 guarantees version is set whenever the template id is.
        if (bp.IntakeFormTemplateId is not null && bp.IntakeFormTemplateVersion is not null)
        {
            var intakeSnapshot = await formTemplateClient.GetVersionSchemaByInternalIdAsync(
                bp.IntakeFormTemplateId.Value,
                bp.IntakeFormTemplateVersion.Value,
                request.TenantId,
                cancellationToken);
            if (intakeSnapshot is null)
                return Result.Fail(new ValidationError(
                    $"{BusinessProcessErrorCodes.IntakeFormTemplateNotFound}: Intake form (v{bp.IntakeFormTemplateVersion}) chưa được publish — không thể kích hoạt BP."));
        }

        // Phase 5c R1-R4: validate workflow graph before activating BP.
        // Uses IWorkflowModuleClient hop — BP module must NOT reference Workflow.Application directly.
        var graphOutcome = await workflowClient.ValidateDefinitionGraphAsync(bp.WorkflowDefinitionId, cancellationToken);
        if (!graphOutcome.IsValid)
        {
            var detail = string.Join("; ", graphOutcome.Violations.Select(v => $"[{v.Rule}] {v.Detail}"));
            // 4-B-A dual-metadata: ValidationError base ctor pre-sets Metadata["ErrorCode"]=GOV_SYS_002;
            // MUST overwrite with dict indexer + add WorkflowErrorCode provenance key.
            var graphError = new ValidationError(
                $"Workflow graph validation failed — cannot activate BusinessProcess. {detail}");
            graphError.Metadata["ErrorCode"] = WorkflowCodes.GraphValidationFailed;
            graphError.WithMetadata("WorkflowErrorCode", WorkflowCodes.GraphValidationFailed);
            // PublicMetadata: opts the structured violations into the wire envelope — the sole key that
            // ApiResponse.Fail() surfaces to the client. Mirrors the workflow Save surface so the FE
            // can render per-rule detail on activation failure instead of a generic 422 message.
            // NOTE: WorkflowGraphViolationDto (contract DTO) has no per-violation Metadata field, unlike
            // the workflow Domain GraphRuleViolation. The projection omits it intentionally — BP-Activate's
            // R1-R4 rules do not emit per-violation metadata (e.g. UnknownGroupCodes). Extend the DTO
            // first if that ever changes.
            graphError.WithMetadata("PublicMetadata", (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
            {
                ["violations"] = graphOutcome.Violations
                    .Select(v => new { v.Rule, v.StateName, v.TransitionActionName, v.Detail })
                    .ToList(),
            });
            // GraphErrors: INTERNAL raw violations list — for logs/debug only; stripped at ApiResponse.Fail().
            graphError.WithMetadata("GraphErrors", graphOutcome.Violations);
            return Result.Fail(graphError);
        }

        var wasActive = bp.IsActive;
        bp.Activate(gen, request.ModifiedBy);
        await repository.UpdateAsync(bp, cancellationToken);
        await cache.RemoveByPrefixAsync(BusinessProcessCacheKeys.TenantPrefix(request.TenantId), cancellationToken);

        logger.LogInformation(
            "BusinessProcess activated: publicId={PublicId}, tenantId={TenantId}, modifiedBy={ModifiedBy}, transition={Transition}",
            bp.PublicId, bp.TenantId, request.ModifiedBy, wasActive ? "no-op" : "false->true");

        return Result.Ok();
    }
}
