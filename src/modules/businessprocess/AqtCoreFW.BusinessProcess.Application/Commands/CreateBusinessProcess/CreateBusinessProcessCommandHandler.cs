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

namespace AqtCoreFW.BusinessProcess.Application.Commands.CreateBusinessProcess;

/// <summary>
/// Pattern A (R13): pre-DB strict tenant guard — no SystemAdmin bypass for writes (mirrors repo guard).
/// Duplicate-code pre-check returns GOV_BP_003; rare race (TOCTOU) surfaces as 500 via unique index — filed FU.
/// Cache invalidation broad-by-prefix on the tenant — low-write workload makes a finer key not worth the complexity.
/// Activate event NOT emitted here — BP starts inactive; the operator must explicitly call Activate.
/// BP MVP P7: resolves WorkflowDefinition + IntakeFormTemplate PublicIds → bigint FKs via module clients
/// so FE never needs to handle internal bigint PKs.
/// </summary>
public sealed class CreateBusinessProcessCommandHandler(
    IBusinessProcessRepository repository,
    IWorkflowModuleClient workflowClient,
    IFormTemplateQueryClient formTemplateClient,
    IGuidGenerator gen,
    ITenantContext tenantContext,
    ICacheService cache,
    ILogger<CreateBusinessProcessCommandHandler> logger)
    : IRequestHandler<CreateBusinessProcessCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateBusinessProcessCommand request, CancellationToken cancellationToken)
    {
        if (request.TenantId != tenantContext.TenantId)
        {
            logger.LogWarning(
                "Cross-tenant CreateBusinessProcess denied: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}",
                tenantContext.TenantId, request.TenantId);
            return Result.Fail(new ValidationError("Cross-tenant access denied"));
        }

        // Resolve WorkflowDefinition PublicId → internal bigint FK (GOV_BP_011 on not-found/cross-tenant)
        var wfInternalId = await workflowClient.TryResolveDefinitionInternalIdAsync(
            request.WorkflowDefinitionPublicId, request.TenantId, cancellationToken);
        if (wfInternalId is null)
            return Result.Fail(new ValidationError(
                $"{BusinessProcessErrorCodes.WorkflowDefinitionNotFound}: WorkflowDefinition not found for tenant."));

        // Resolve optional IntakeFormTemplate PublicId → internal bigint FK (GOV_BP_012 on not-found/cross-tenant)
        long? intakeFormInternalId = null;
        if (request.IntakeFormTemplatePublicId.HasValue)
        {
            intakeFormInternalId = await formTemplateClient.TryResolveTemplateInternalIdAsync(
                request.IntakeFormTemplatePublicId.Value, request.TenantId, cancellationToken);
            if (intakeFormInternalId is null)
                return Result.Fail(new ValidationError(
                    $"{BusinessProcessErrorCodes.IntakeFormTemplateNotFound}: IntakeFormTemplate not found for tenant."));

            // GOV_BP_010 (both-or-neither) is only enforced later in the aggregate factory; the
            // validator deliberately doesn't duplicate it. Enforce the pinned-template ⇒ version
            // half HERE so the snapshot check below can't NRE on a null version (raw request:
            // PublicId set + Version null is reachable from the API). Mirrors the aggregate's
            // 422 rather than letting it 500.
            if (request.IntakeFormTemplateVersion is null)
                return Result.Fail(new ValidationError(
                    "GOV_BP_010: IntakeFormTemplateVersion is required when an intake form template is pinned."));

            // Fail-fast: the pinned intake (template,version) MUST have a published snapshot.
            // Without this, an unpublished/draft intake form is accepted here and only blows up
            // for the end-user at run time (GOV_SYS_001 in GetIntakeFormSchema). Root-caused 2026-05-18.
            var intakeSnapshot = await formTemplateClient.GetVersionSchemaByInternalIdAsync(
                intakeFormInternalId.Value,
                request.IntakeFormTemplateVersion.Value,
                request.TenantId,
                cancellationToken);
            if (intakeSnapshot is null)
                return Result.Fail(new ValidationError(
                    $"{BusinessProcessErrorCodes.IntakeFormTemplateNotFound}: Intake form (v{request.IntakeFormTemplateVersion}) chưa được publish — không thể tạo BP."));
        }

        var existing = await repository.GetByCodeAsync(request.Code, cancellationToken);
        if (existing is not null)
            return Result.Fail(new ConflictError(
                $"{BusinessProcessErrorCodes.DuplicateCode}: BusinessProcess code '{request.Code}' already exists for tenant."));

        BusinessProcessDefinition bp;
        try
        {
            bp = BusinessProcessDefinition.Create(
                gen,
                request.TenantId,
                request.Code,
                request.Name,
                wfInternalId.Value,
                request.CreatedBy,
                request.Description,
                intakeFormInternalId,
                request.IntakeFormTemplateVersion);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("GOV_BP_010"))
        {
            return Result.Fail(new ValidationError(ex.Message));
        }

        await repository.AddAsync(bp, cancellationToken);
        await cache.RemoveByPrefixAsync(BusinessProcessCacheKeys.TenantPrefix(request.TenantId), cancellationToken);

        logger.LogInformation(
            "BusinessProcess created: publicId={PublicId}, code={Code}, tenantId={TenantId}, createdBy={CreatedBy}",
            bp.PublicId, bp.Code, bp.TenantId, request.CreatedBy);

        return Result.Ok(bp.PublicId);
    }
}
