using AqtCoreFW.Forms.Domain.Events;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Forms.Infrastructure.Clients;

/// <summary>
/// In-process implementation of IFormSubmissionStatusClient.
/// Called by Workflow module to approve/reject form submissions on workflow completion.
/// Publishes FormApprovedEvent / FormRejectedEvent after persisting status change.
/// Non-fatal: catches domain exceptions from race conditions (e.g. already approved).
/// Phase-02c: guidGenerator injected for Rule1-compliant event MessageId.
/// </summary>
public sealed class InProcessFormSubmissionStatusClient(
    IFormSubmissionRepository repository,
    IGuidGenerator guidGenerator,
    IPublisher publisher,
    ILogger<InProcessFormSubmissionStatusClient> logger)
    : IFormSubmissionStatusClient
{
    public async Task ApproveSubmissionAsync(
        Guid submissionId, Guid reviewedBy, string comment, CancellationToken ct)
    {
        var result = await repository.GetByIdAsync(submissionId, ct);
        if (result.IsFailed)
        {
            logger.LogWarning("Submission {SubmissionId} not found for approval.", submissionId);
            return;
        }

        var submission = result.Value;
        try
        {
            submission.Approve(reviewedBy, comment);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Cannot approve submission {SubmissionId} — already {Status}.",
                submissionId, submission.Status);
            return;
        }

        await repository.UpdateAsync(submission, ct);

        // submission.PublicId = Guid (T2 external AK); FormTemplateId = long (T1 PK)
        await publisher.Publish(
            new FormApprovedEvent(submission.PublicId, submission.FormTemplateId, submission.TenantId, reviewedBy, guidGenerator.NewIdRandom()), ct);
    }

    public async Task RejectSubmissionAsync(
        Guid submissionId, Guid reviewedBy, string comment, CancellationToken ct)
    {
        var result = await repository.GetByIdAsync(submissionId, ct);
        if (result.IsFailed)
        {
            logger.LogWarning("Submission {SubmissionId} not found for rejection.", submissionId);
            return;
        }

        var submission = result.Value;
        try
        {
            submission.Reject(reviewedBy, comment);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            logger.LogWarning(ex, "Cannot reject submission {SubmissionId} — already {Status}.",
                submissionId, submission.Status);
            return;
        }

        await repository.UpdateAsync(submission, ct);

        // submission.PublicId = Guid (T2 external AK); FormTemplateId = long (T1 PK)
        await publisher.Publish(
            new FormRejectedEvent(submission.PublicId, submission.FormTemplateId, submission.TenantId, reviewedBy, guidGenerator.NewIdRandom()), ct);
    }
}
