using AqtCoreFW.Identity.Application.Commands.ManageGroup;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Commands.ManageGroup;

/// <summary>
/// Creates a new UserGroup (T2). Validates Code uniqueness within the tenant before persisting.
/// Returns PublicId (Guid) — T2 external-facing identifier.
/// </summary>
public sealed class CreateGroupCommandHandler(IdentityDbContext db, IGuidGenerator guidGen)
    : IRequestHandler<CreateGroupCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateGroupCommand cmd, CancellationToken ct)
    {
        // Enforce Code uniqueness per tenant
        var exists = await db.UserGroups
            .AnyAsync(g => g.Code == cmd.Code.Trim().ToUpperInvariant() && g.TenantId == cmd.TenantId, ct);

        if (exists)
            return Result.Fail(new ConflictError($"Group code '{cmd.Code}' already exists in this tenant."));

        var group = UserGroup.Create(guidGen, cmd.Code, cmd.Name, cmd.Description, cmd.TenantId, cmd.CreatedBy);

        db.UserGroups.Add(group);
        await db.SaveChangesAsync(ct);

        return Result.Ok(group.PublicId);
    }
}
