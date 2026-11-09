using AqtCoreFW.Identity.Domain.ValueObjects;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Commands.ChangeTenantStatus;

/// <summary>
/// Command to change tenant status (Active/Suspended/Archived).
/// DELETE endpoint maps to ChangeStatus(Archived) — soft-delete only.
/// </summary>
public sealed record ChangeTenantStatusCommand(
    Guid TenantId,
    TenantStatus NewStatus,
    Guid ActorId) : IRequest<Result>;
