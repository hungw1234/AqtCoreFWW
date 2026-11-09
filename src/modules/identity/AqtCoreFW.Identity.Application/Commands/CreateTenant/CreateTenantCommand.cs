using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Commands.CreateTenant;

/// <summary>
/// Command to create a new Tenant.
/// Code must be unique and becomes immutable after creation.
/// </summary>
public sealed record CreateTenantCommand(
    string Name,
    string Code,
    string? ContactEmail,
    Guid ActorId) : IRequest<Result<Guid>>;
