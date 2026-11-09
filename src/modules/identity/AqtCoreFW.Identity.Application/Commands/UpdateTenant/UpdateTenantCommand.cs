using AqtCoreFW.Identity.Domain.ValueObjects;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Commands.UpdateTenant;

/// <summary>
/// Command to update Tenant properties (Name, ContactEmail).
/// Code is IMMUTABLE, Status changes via ChangeTenantStatusCommand.
/// </summary>
public sealed record UpdateTenantCommand(
    Guid TenantId,
    string Name,
    string? ContactEmail,
    Guid ActorId) : IRequest<Result>;
