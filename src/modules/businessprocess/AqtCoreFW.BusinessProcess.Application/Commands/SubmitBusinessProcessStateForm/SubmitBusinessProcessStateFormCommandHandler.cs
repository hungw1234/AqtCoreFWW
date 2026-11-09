using AqtCoreFW.BusinessProcess.Application.Abstractions;
using AqtCoreFW.BusinessProcess.Domain.Errors;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.BusinessProcess.Application.Commands.SubmitBusinessProcessStateForm;

/// <summary>
/// Validates the (BP, state, formTemplate) binding then delegates to <see cref="IFormSubmissionRunClient"/>.
/// Pattern-A pre-DB tenant guard; no SystemAdmin bypass on writes.
/// </summary>
public sealed class SubmitBusinessProcessStateFormCommandHandler(
    IBusinessProcessRuntimeQueries runtimeQueries,
    IFormTemplateQueryClient formTemplateClient,
    IFormSubmissionRunClient formSubmissionClient,
    ITenantContext tenantContext,
    ILogger<SubmitBusinessProcessStateFormCommandHandler> logger)
    : IRequestHandler<SubmitBusinessProcessStateFormCommand, Result<SubmitBusinessProcessStateFormResult>>
{
    public async Task<Result<SubmitBusinessProcessStateFormResult>> Handle(
        SubmitBusinessProcessStateFormCommand request, CancellationToken cancellationToken)
    {
        if (request.TenantId != tenantContext.TenantId)
        {
            logger.LogWarning(
                "Cross-tenant SubmitBusinessProcessStateForm denied: currentTenantId={CurrentTenantId}, attemptedTenantId={AttemptedTenantId}",
                tenantContext.TenantId, request.TenantId);
            return Result.Fail(new ValidationError("Cross-tenant access denied"));
        }

        var bindings = await runtimeQueries.GetStateFormTemplateIdsAsync(
            request.BpPublicId, request.WorkflowStateId, request.TenantId, cancellationToken);

        var matching = bindings.FirstOrDefault(b => b.FormTemplateId == request.FormTemplateId);
        if (matching is null)
            return Result.Fail(new ValidationError(
                $"{BusinessProcessErrorCodes.DuplicateBinding}: FormTemplate không thuộc state này của BusinessProcess."));

        var snapshot = await formTemplateClient.GetVersionSchemaByInternalIdAsync(
            matching.FormTemplateId, matching.FormTemplateVersion, request.TenantId, cancellationToken);
        if (snapshot is null)
            return Result.Fail(new NotFoundError(
                $"{BusinessProcessErrorCodes.NotFound}: FormTemplate snapshot v{matching.FormTemplateVersion} không tìm thấy."));

        var outcome = await formSubmissionClient.CreateForBpRunAsync(
            request.WorkflowInstanceId,
            request.WorkflowStateId,
            snapshot.FormTemplatePublicId,
            request.ActorId,
            request.Data,
            request.ConsentGiven,
            request.TenantId,
            request.Signature,
            cancellationToken);

        if (!outcome.IsSuccess)
            return outcome.ToFailureResult<SubmitBusinessProcessStateFormResult>(
                "BusinessProcess Run không tìm thấy.");

        logger.LogInformation(
            "BP state form submitted: workflowInstanceId={WorkflowInstanceId}, workflowStateId={WorkflowStateId}, formSubmissionId={FormSubmissionId}",
            request.WorkflowInstanceId, request.WorkflowStateId, outcome.SubmissionId);

        return Result.Ok(new SubmitBusinessProcessStateFormResult(outcome.SubmissionId!.Value));
    }
}
