using AqtCoreFW.Audit.Domain.ValueObjects;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Commands.UpdateIncidentStatus;

public sealed record UpdateIncidentStatusCommand(
    Guid IncidentId,
    IncidentStatus NewStatus,
    string? MitigationNote) : IRequest<Result>;
