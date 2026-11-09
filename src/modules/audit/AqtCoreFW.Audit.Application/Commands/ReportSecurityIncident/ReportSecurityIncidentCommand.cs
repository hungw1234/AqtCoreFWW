using AqtCoreFW.Audit.Domain.ValueObjects;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Commands.ReportSecurityIncident;

public sealed record ReportSecurityIncidentCommand(
    Guid? TenantId,
    string Title,
    AuditSeverity Severity,
    string Description,
    Guid ReportedBy,
    DateTimeOffset OccurredAt) : IRequest<Result<Guid>>;
