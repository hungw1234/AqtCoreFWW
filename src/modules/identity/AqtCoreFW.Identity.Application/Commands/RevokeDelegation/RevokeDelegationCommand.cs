using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Commands.RevokeDelegation;

/// <summary>Revoke an active role delegation by ID.</summary>
public sealed record RevokeDelegationCommand(
    Guid DelegationId,
    Guid ActorId) : IRequest<Result>;
