using AqtCoreFW.BusinessProcess.Application.Abstractions;
using AqtCoreFW.BusinessProcess.Domain.Errors;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.BusinessProcess.Application.Commands.StartBusinessProcessRun;

/// <summary>
/// Atomic BP-Run start: creates the WorkflowInstance and, when the BP pins an intake form, the intake
/// FormSubmission under one shared local SQL transaction via ICrossModuleUnitOfWork. Both module
/// DbContexts enlist on a single SqlConnection so the write never opens a second physical connection,
/// which would promote to a distributed transaction on Linux where MSDTC is unavailable.
///
/// Pattern-A pre-DB tenant guard; SystemAdmin bypass dropped on writes (symmetric with Slice 2A-Write).
/// </summary>
public sealed class StartBusinessProcessRunCommandHandler(
    IBusinessProcessRuntimeQueries runtimeQueries,
    IWorkflowModuleClient workflowClient,
    IFormTemplateQueryClient formTemplateClient,
    IFormSubmissionRunClient formSubmissionClient,
    ICrossModuleUnitOfWork uow,
    ITenantContext tenantContext,
    IGuidGenerator gen,
    ILogger<StartBusinessProcessRunCommandHandler> logger)
    : IRequestHandler<StartBusinessProcessRunCommand, Result<StartBusinessProcessRunResult>>
{
    public async Task<Result<StartBusinessProcessRunResult>> Handle(
        StartBusinessProcessRunCommand request, CancellationToken cancellationToken)
    {
        if (request.TenantId != tenantContext.TenantId)
        {
            logger.LogWarning(
                "Cross-tenant StartBusinessProcessRun denied: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}",
                tenantContext.TenantId, request.TenantId);
            return Result.Fail(new ValidationError("Cross-tenant access denied"));
        }

        var ctx = await runtimeQueries.GetRuntimeContextAsync(request.BpPublicId, request.TenantId, cancellationToken);
        if (ctx is null)
            return Result.Fail(new NotFoundError(
                $"{BusinessProcessErrorCodes.NotFound}: BusinessProcess {request.BpPublicId} không tìm thấy."));

        if (!ctx.IsActive)
            return Result.Fail(new ValidationError(
                $"{BusinessProcessErrorCodes.Inactive}: BusinessProcess không ở trạng thái active."));

        var hasIntakeForm = ctx.IntakeFormTemplateId.HasValue && ctx.IntakeFormVersion.HasValue;
        if (hasIntakeForm && request.IntakeFormData is null)
            return Result.Fail(new ValidationError(
                $"{BusinessProcessErrorCodes.IntakePairingInvalid}: BusinessProcess yêu cầu intake form data."));

        // Resolve the form-template snapshot before opening the transaction — it is a read and its
        // null result (404) must short-circuit before any writes are staged, regardless of tx scope.
        // Externally identical: callers still receive 404 when the snapshot is missing.
        FormTemplateVersionSchemaDto? snapshot = null;
        if (hasIntakeForm)
        {
            snapshot = await formTemplateClient.GetVersionSchemaByInternalIdAsync(
                ctx.IntakeFormTemplateId!.Value,
                ctx.IntakeFormVersion!.Value,
                request.TenantId,
                cancellationToken);

            if (snapshot is null)
            {
                logger.LogWarning(
                    "BP {BpPublicId} pins FormTemplate {TemplateId} v{Version} but snapshot is missing.",
                    request.BpPublicId, ctx.IntakeFormTemplateId.Value, ctx.IntakeFormVersion.Value);
                return Result.Fail(new NotFoundError(
                    $"{BusinessProcessErrorCodes.NotFound}: Intake form snapshot v{ctx.IntakeFormVersion.Value} không tìm thấy."));
            }
        }

        // One shared local SQL transaction for all cross-module writes — WorkflowDbContext and
        // FormsDbContext both enlist onto the same connection at their first I/O, ensuring atomicity
        // without a second physical connection that would promote to DTC on Linux.
        await uow.BeginAsync(cancellationToken);
        WorkflowInstanceResult wiResult;
        Guid? intakeSubmissionId = null;
        try
        {
            try
            {
                // Per-run dossier id: makes each BusinessProcess run exempt from the unique
                // active-entity index, allowing N concurrent runs per definition. entityId stays
                // = bpId so the FE still resolves the BP display name from it.
                var dossierId = gen.NewIdMonotonic();
                wiResult = await workflowClient.CreateInstanceByInternalIdAsync(
                    ctx.WorkflowDefinitionId,
                    request.EntityType,
                    request.EntityId,
                    request.TenantId,
                    request.ActorId,
                    dossierId,
                    cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                await uow.RollbackAsync(cancellationToken);
                logger.LogWarning(ex, "WorkflowDefinition resolve failed for BP {BpPublicId}", request.BpPublicId);
                return Result.Fail(new NotFoundError(
                    $"{BusinessProcessErrorCodes.NotFound}: WorkflowDefinition không khả dụng."));
            }

            // Phase 5b: WF_NO_HANDLER (empty pool at initial state) surfaces as Fail outcome, not throw.
            // Map to validation error so BP runtime rejects with a stable surface (422 via ValidationError).
            if (!wiResult.IsSuccess)
            {
                await uow.RollbackAsync(cancellationToken);
                logger.LogWarning(
                    "WorkflowInstance creation rejected for BP {BpPublicId}: {Code} {Message}",
                    request.BpPublicId, wiResult.ErrorCode, wiResult.ErrorMessage);
                return Result.Fail(new ValidationError(
                    $"{wiResult.ErrorCode ?? BusinessProcessErrorCodes.NotFound}: {wiResult.ErrorMessage ?? "Workflow instance creation failed."}"));
            }

            if (hasIntakeForm)
            {
                var outcome = await formSubmissionClient.CreateForBpRunAsync(
                    wiResult.InstanceId,
                    workflowStateId: null,
                    snapshot!.FormTemplatePublicId,
                    request.ActorId,
                    request.IntakeFormData!,
                    request.ConsentGiven,
                    request.TenantId,
                    request.Signature,
                    cancellationToken);

                if (!outcome.IsSuccess)
                {
                    await uow.RollbackAsync(cancellationToken);
                    return outcome.ToFailureResult<StartBusinessProcessRunResult>(
                        "BusinessProcess không tìm thấy.");
                }

                intakeSubmissionId = outcome.SubmissionId;
            }

            await uow.CommitAsync(cancellationToken);
        }
        catch
        {
            await uow.RollbackAsync(cancellationToken);
            throw;
        }

        logger.LogInformation(
            "BusinessProcess Run started: bpPublicId={BpPublicId}, workflowInstanceId={WorkflowInstanceId}, intakeSubmissionId={IntakeSubmissionId}, tenantId={TenantId}",
            request.BpPublicId, wiResult.InstanceId, intakeSubmissionId, request.TenantId);

        return Result.Ok(new StartBusinessProcessRunResult(
            wiResult.InstanceId,
            wiResult.InitialStateInternalId,
            intakeSubmissionId));
    }
}
