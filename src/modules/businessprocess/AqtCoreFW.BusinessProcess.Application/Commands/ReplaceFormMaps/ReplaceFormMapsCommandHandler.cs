using AqtCoreFW.BusinessProcess.Application.Caching;
using AqtCoreFW.BusinessProcess.Domain.Entities;
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

namespace AqtCoreFW.BusinessProcess.Application.Commands.ReplaceFormMaps;

/// <summary>
/// Atomically replaces all state-form and transition-form bindings for a BusinessProcessDefinition.
///
/// Security model:
///   - Pattern A (R13): strict tenant guard pre-DB.
///   - WorkflowStateId / WorkflowTransitionId longs are validated against the BP's
///     WorkflowDefinition structure (GetDefinitionStructureAsync) to prevent cross-definition injection.
///   - FormTemplate Guid PublicIds are resolved to bigint internal Ids via IFormTemplateQueryClient.
///   - RowVersion ETag (If-Match header) required — mismatch returns PreconditionFailedError → 412 (D-B-3).
///
/// Replace-all semantics (D-B-1): ClearStateForms + ClearTransitionForms then re-add all.
/// displayOrder is array index (D-B-2): client-supplied order in DTO is intentionally ignored.
/// AuditBehavior auto-captures via "Replace" verb in CommandVerbs (D-B-4).
/// </summary>
public sealed class ReplaceFormMapsCommandHandler(
    IBusinessProcessRepository repository,
    IWorkflowModuleClient workflowClient,
    IFormTemplateQueryClient formTemplateClient,
    IGuidGenerator gen,
    ITenantContext tenantContext,
    ICacheService cache,
    ILogger<ReplaceFormMapsCommandHandler> logger)
    : IRequestHandler<ReplaceFormMapsCommand, Result>
{
    public async Task<Result> Handle(ReplaceFormMapsCommand request, CancellationToken cancellationToken)
    {
        // Step 1: Pattern-A strict tenant guard pre-DB
        if (request.TenantId != tenantContext.TenantId)
        {
            logger.LogWarning(
                "Cross-tenant ReplaceFormMaps denied: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}",
                tenantContext.TenantId, request.TenantId);
            return Result.Fail(new ValidationError("Cross-tenant access denied"));
        }

        // Step 2: Load BP aggregate (with children) by PublicId + TenantId
        var bp = await repository.GetByPublicIdAsync(request.BusinessProcessPublicId, cancellationToken);
        if (bp is null)
            return Result.Fail(new NotFoundError(
                $"{BusinessProcessErrorCodes.NotFound}: BusinessProcess {request.BusinessProcessPublicId} không tìm thấy."));

        // Step 3: ETag/RowVersion check (D-B-3) — If-Match is MANDATORY; 412 on absent or stale.
        // Missing header = equivalent to "client doesn't know current state" → reject to prevent
        // silent overwrites (D-B-3: mandatory ETag). Empty string from controller header-not-present path.
        if (string.IsNullOrWhiteSpace(request.IfMatchETag))
            return Result.Fail(new PreconditionFailedError(
                "If-Match header is required for this operation."));

        if (!TryParseETag(request.IfMatchETag, out var etagBytes))
            return Result.Fail(new BadRequestError(
                "If-Match header is not valid base64.", "GOV_BP_013"));

        if (!etagBytes!.AsSpan().SequenceEqual(bp.RowVersion))
            return Result.Fail(new PreconditionFailedError(
                "Form-maps changed by another user — please refresh."));

        // Step 4: Resolve WorkflowDefinition structure (validates state/transition IDs belong to this BP's definition)
        var structure = await workflowClient.GetDefinitionStructureAsync(
            bp.WorkflowDefinitionId, request.TenantId, cancellationToken);
        if (structure is null)
            return Result.Fail(new NotFoundError(
                $"{BusinessProcessErrorCodes.WorkflowDefinitionNotFound}: WorkflowDefinition for BP not found."));

        // Step 5: Validate all state IDs belong to this definition
        var invalidStateIds = request.StateForms
            .Select(sf => sf.WorkflowStateId)
            .Where(id => !structure.StateIds.Contains(id))
            .Distinct()
            .ToList();
        if (invalidStateIds.Count > 0)
            return Result.Fail(new ValidationError(
                $"GOV_BP_005: WorkflowState IDs not found in definition: {string.Join(", ", invalidStateIds)}"));

        // Step 6: Validate all transition IDs belong to this definition
        var invalidTransitionIds = request.TransitionForms
            .Select(tf => tf.WorkflowTransitionId)
            .Where(id => !structure.TransitionIds.Contains(id))
            .Distinct()
            .ToList();
        if (invalidTransitionIds.Count > 0)
            return Result.Fail(new ValidationError(
                $"GOV_BP_005: WorkflowTransition IDs not found in definition: {string.Join(", ", invalidTransitionIds)}"));

        // Step 7: Resolve all FormTemplate PublicIds → bigint in parallel
        var stateTemplateResolveTasks = request.StateForms
            .Select(sf => formTemplateClient.TryResolveTemplateInternalIdAsync(
                sf.FormTemplatePublicId, request.TenantId, cancellationToken))
            .ToList();
        var transitionTemplateResolveTasks = request.TransitionForms
            .Select(tf => formTemplateClient.TryResolveTemplateInternalIdAsync(
                tf.FormTemplatePublicId, request.TenantId, cancellationToken))
            .ToList();

        var stateTemplateIds = await Task.WhenAll(stateTemplateResolveTasks);
        var transitionTemplateIds = await Task.WhenAll(transitionTemplateResolveTasks);

        // Check for unresolvable state form templates
        for (var i = 0; i < stateTemplateIds.Length; i++)
        {
            if (stateTemplateIds[i] is null)
                return Result.Fail(new ValidationError(
                    $"GOV_BP_005: FormTemplate {request.StateForms[i].FormTemplatePublicId} not found for tenant."));
        }

        // Check for unresolvable transition form templates
        for (var i = 0; i < transitionTemplateIds.Length; i++)
        {
            if (transitionTemplateIds[i] is null)
                return Result.Fail(new ValidationError(
                    $"GOV_BP_005: FormTemplate {request.TransitionForms[i].FormTemplatePublicId} not found for tenant."));
        }

        // Step 8: Atomic replace — clear all then add new (D-B-1)
        bp.ClearStateForms();
        bp.ClearTransitionForms();

        // Step 9: Add new state form bindings (displayOrder = array index, D-B-2)
        for (var i = 0; i < request.StateForms.Count; i++)
        {
            var dto = request.StateForms[i];
            bp.AddStateForm(BusinessProcessStateForm.Create(
                gen,
                tenantId: bp.TenantId,
                businessProcessDefinitionId: bp.Id,
                workflowStateId: dto.WorkflowStateId,
                formTemplateId: stateTemplateIds[i]!.Value,
                formTemplateVersion: dto.Version,
                createdBy: request.ModifiedBy,
                isRequired: dto.IsRequired,
                displayOrder: i));
        }

        // Step 10: Add new transition form bindings (displayOrder = array index, D-B-2)
        for (var i = 0; i < request.TransitionForms.Count; i++)
        {
            var dto = request.TransitionForms[i];
            bp.AddTransitionForm(BusinessProcessTransitionForm.Create(
                gen,
                tenantId: bp.TenantId,
                businessProcessDefinitionId: bp.Id,
                workflowTransitionId: dto.WorkflowTransitionId,
                formTemplateId: transitionTemplateIds[i]!.Value,
                formTemplateVersion: dto.Version,
                createdBy: request.ModifiedBy,
                isRequired: dto.IsRequired,
                displayOrder: i));
        }

        // Step 11: Persist (EF change tracking + RowVersion bump in same SaveChanges).
        // Real concurrent-replace race (two clients pass same in-memory ETag check) throws
        // DbUpdateConcurrencyException → GlobalExceptionHandler maps to 409. The in-memory
        // pre-check handles the common case; race window for fallback is narrow.
        await repository.UpdateAsync(bp, cancellationToken);

        // Step 12: Broad-prefix cache invalidation (same pattern as Create/Update/Activate)
        await cache.RemoveByPrefixAsync(BusinessProcessCacheKeys.TenantPrefix(request.TenantId), cancellationToken);

        logger.LogInformation(
            "ReplaceFormMaps: bp={BpPublicId}, tenant={TenantId}, stateForms={StateCount}, transitionForms={TransitionCount}, by={ModifiedBy}",
            bp.PublicId, bp.TenantId, request.StateForms.Count, request.TransitionForms.Count, request.ModifiedBy);

        return Result.Ok();
    }

    /// <summary>
    /// Parses the If-Match ETag value, stripping optional W/" and " delimiters.
    /// Returns false if the value is not valid base64 after stripping.
    /// </summary>
    private static bool TryParseETag(string eTag, out byte[]? bytes)
    {
        // Strip HTTP weak ETag wrapper: W/"..." or "..."
        var raw = eTag.Trim()
            .TrimStart('W', '/')
            .Trim('"');
        try
        {
            bytes = Convert.FromBase64String(raw);
            return true;
        }
        catch (FormatException)
        {
            bytes = null;
            return false;
        }
    }
}
