using AqtCoreFW.Forms.Domain.Events;
using AqtCoreFW.Forms.Domain.Errors;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Forms.Application.Commands.ApproveSubmission;

public sealed class ApproveSubmissionCommandHandler(
    IFormSubmissionRepository repository,
    IGuidGenerator guidGenerator,
    IPublisher publisher,
    ILogger<ApproveSubmissionCommandHandler> logger)
    : IRequestHandler<ApproveSubmissionCommand, Result>
{
    public async Task<Result> Handle(ApproveSubmissionCommand request, CancellationToken cancellationToken)
    {
        // IFormSubmissionRepository uses Guid key = PublicId (T2 external AK)
        var result = await repository.GetByIdAsync(request.SubmissionId, cancellationToken);
        if (result.IsFailed)
            return Result.Fail(FormErrors.SubmissionNotFound(request.SubmissionId));

        var submission = result.Value;
        if (submission.TenantId != request.TenantId)
            return Result.Fail(FormErrors.AccessDenied());

        // Self-approval guard — reviewer cannot approve their own submission (SoD)
        if (submission.SubmittedBy == request.ReviewedBy)
            return Result.Fail(new SharedKernel.Errors.ValidationError(
                "Không thể phê duyệt biểu mẫu do chính mình nộp.", "ReviewedBy"));

        // Warn when manually approving a workflow-managed submission — possible duplicate path
        if (submission.WorkflowInstanceId.HasValue)
            logger.LogWarning(
                "Manual approval on workflow-managed submission {SubmissionId} (workflow {WorkflowInstanceId}). " +
                "Domain guard will prevent duplicate state change if already approved.",
                request.SubmissionId, submission.WorkflowInstanceId);

        try
        {
            submission.Approve(request.ReviewedBy, request.Comment);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail(new SharedKernel.Errors.ValidationError(ex.Message, "Status"));
        }

        await repository.UpdateAsync(submission, cancellationToken);

        // PublicId = Guid (T2 external AK); FormTemplateId = long (T1 internal PK)
        await publisher.Publish(
            new FormApprovedEvent(
                submission.PublicId,
                submission.FormTemplateId,
                submission.TenantId,
                request.ReviewedBy,
                guidGenerator.NewIdRandom()),
            cancellationToken);

        return Result.Ok();
    }
}
