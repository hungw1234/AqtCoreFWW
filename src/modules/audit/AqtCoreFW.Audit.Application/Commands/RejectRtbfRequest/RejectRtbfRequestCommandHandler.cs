using AqtCoreFW.Audit.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Commands.RejectRtbfRequest;

public sealed class RejectRtbfRequestCommandHandler(IRtbfRequestRepository repository)
    : IRequestHandler<RejectRtbfRequestCommand, Result>
{
    public async Task<Result> Handle(RejectRtbfRequestCommand request, CancellationToken cancellationToken)
    {
        var rtbf = await repository.GetByIdAsync(request.RequestId, cancellationToken);
        if (rtbf is null)
            return Result.Fail(new NotFoundError($"RtbfRequest {request.RequestId} not found"));

        rtbf.Reject(request.ProcessedBy, request.Reason);
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
