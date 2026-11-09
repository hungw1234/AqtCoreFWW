using AqtCoreFW.Audit.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Commands.UpdateIncidentStatus;

public sealed class UpdateIncidentStatusCommandHandler(ISecurityIncidentRepository repository)
    : IRequestHandler<UpdateIncidentStatusCommand, Result>
{
    public async Task<Result> Handle(
        UpdateIncidentStatusCommand request,
        CancellationToken cancellationToken)
    {
        var incident = await repository.GetByIdAsync(request.IncidentId, cancellationToken);
        if (incident is null)
            return Result.Fail(new NotFoundError($"SecurityIncident {request.IncidentId} not found"));

        incident.UpdateStatus(request.NewStatus, request.MitigationNote);
        await repository.SaveChangesAsync(cancellationToken);
        return Result.Ok();
    }
}
