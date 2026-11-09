using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Commands.ApproveDelegation;

/// <summary>
/// Approve a PendingApproval delegation, transitioning it to Active.
/// ActorId must be Admin or SystemAdmin — enforced in handler.
/// </summary>
public sealed record ApproveDelegationCommand(
    Guid DelegationId,
    Guid ActorId) : IRequest<Result>;
