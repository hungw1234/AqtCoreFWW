using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Commands.DelegateRole;

/// <summary>Delegate all roles from DelegatorId to DelegateId for a bounded time window.</summary>
public sealed record DelegateRoleCommand(
    Guid DelegatorId,
    Guid DelegateId,
    DateTime ValidFrom,
    DateTime ValidTo,
    string Reason,
    Guid ActorId) : IRequest<Result<Guid>>;
