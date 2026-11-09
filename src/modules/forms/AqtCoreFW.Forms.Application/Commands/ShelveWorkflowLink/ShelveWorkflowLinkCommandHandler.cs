using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Commands.ShelveWorkflowLink;

/// <summary>Handler for the Phase-05 admin Shelve (manual dead-letter) override.</summary>
public sealed class ShelveWorkflowLinkCommandHandler(
    IFormSubmissionWorkflowLinkPendingAttemptRepository repository)
    : IRequestHandler<ShelveWorkflowLinkCommand, Result>
{
    public async Task<Result> Handle(
        ShelveWorkflowLinkCommand request,
        CancellationToken cancellationToken)
    {
        var row = await repository.GetTrackedBySubmissionIdAsync(
            request.SubmissionId, request.TenantId, cancellationToken);

        if (row is null)
            return Result.Fail(new NotFoundError(
                $"Không tìm thấy bản ghi chờ kết nối quy trình cho submission '{request.SubmissionId}'."));

        try
        {
            row.MarkShelved(request.ActorId);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Fail(new ConflictError(ex.Message));
        }

        await repository.UpdateAsync(row, cancellationToken);
        return Result.Ok();
    }
}
