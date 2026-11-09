using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Commands.ReportSecurityIncident;

/// <summary>
/// Creates a new security incident report.
/// Returns MessageId (Guid AK) — T3 plain-class, no PublicId.
/// </summary>
public sealed class ReportSecurityIncidentCommandHandler(
    ISecurityIncidentRepository repository,
    IGuidGenerator guidGenerator)
    : IRequestHandler<ReportSecurityIncidentCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        ReportSecurityIncidentCommand request,
        CancellationToken cancellationToken)
    {
        var incident = SecurityIncident.Report(
            guidGenerator,
            request.TenantId,
            request.Title,
            request.Severity,
            request.Description,
            request.ReportedBy,
            request.OccurredAt);

        await repository.AddAsync(incident, cancellationToken);
        return Result.Ok(incident.MessageId);
    }
}
