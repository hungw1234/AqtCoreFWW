using FluentResults;
using MediatR;

namespace AqtCoreFW.Audit.Application.Commands.CreateRtbfRequest;

/// <summary>Create a new RTBF anonymization request — PDPL Art. 17 compliance.</summary>
public sealed record CreateRtbfRequestCommand(
    string SubjectEmail,
    string Reason,
    Guid RequestedBy,
    Guid TenantId) : IRequest<Result<Guid>>;
