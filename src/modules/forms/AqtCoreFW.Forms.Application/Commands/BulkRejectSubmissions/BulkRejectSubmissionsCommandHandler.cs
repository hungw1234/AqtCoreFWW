using AqtCoreFW.Forms.Application.Commands.BulkApproveSubmissions;
using AqtCoreFW.Forms.Domain.Events;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Commands.BulkRejectSubmissions;

public sealed class BulkRejectSubmissionsCommandHandler(
    IFormSubmissionRepository repository,
    IGuidGenerator guidGenerator,
    IPublisher publisher)
    : IRequestHandler<BulkRejectSubmissionsCommand, Result<BulkActionResult>>
{
    public async Task<Result<BulkActionResult>> Handle(
        BulkRejectSubmissionsCommand request,
        CancellationToken cancellationToken)
    {
        var succeeded = 0;
        var errors = new List<string>();
        // PublicId = Guid (T2 external AK); FormTemplateId = long (T1 internal PK)
        var rejected = new List<(Guid PublicId, long TemplateId, Guid TenantId)>();

        foreach (var id in request.Ids)
        {
            // IFormSubmissionRepository uses Guid key = PublicId (T2 external AK)
            var result = await repository.GetByIdAsync(id, cancellationToken);
            if (result.IsFailed)
            {
                errors.Add($"Submission {id}: not found");
                continue;
            }

            var submission = result.Value;
            if (submission.TenantId != request.TenantId)
            {
                errors.Add($"Submission {id}: access denied");
                continue;
            }

            // Self-rejection guard — skip own submissions (SoD)
            if (submission.SubmittedBy == request.ReviewedBy)
            {
                errors.Add($"Submission {id}: cannot reject own submission");
                continue;
            }

            try
            {
                submission.Reject(request.ReviewedBy, request.Comment);
                await repository.UpdateAsync(submission, cancellationToken);
                rejected.Add((submission.PublicId, submission.FormTemplateId, submission.TenantId));
                succeeded++;
            }
            catch (InvalidOperationException ex)
            {
                errors.Add($"Submission {id}: {ex.Message}");
            }
            catch (ArgumentException ex)
            {
                errors.Add($"Submission {id}: {ex.Message}");
            }
        }

        // Publish events after all DB writes succeed — avoids partial event publishing on failure
        foreach (var (publicId, templateId, tenantId) in rejected)
        {
            await publisher.Publish(
                new FormRejectedEvent(publicId, templateId, tenantId, request.ReviewedBy, guidGenerator.NewIdRandom()),
                cancellationToken);
        }

        return Result.Ok(new BulkActionResult(succeeded, errors.Count, errors));
    }
}
