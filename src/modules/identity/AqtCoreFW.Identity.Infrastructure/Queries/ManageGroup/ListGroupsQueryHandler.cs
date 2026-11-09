using AqtCoreFW.Identity.Application.Queries.ListGroups;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Queries.ManageGroup;

/// <summary>Handles ListGroupsQuery — reads directly from IdentityDbContext.</summary>
public sealed class ListGroupsQueryHandler(IdentityDbContext db)
    : IRequestHandler<ListGroupsQuery, Result<IReadOnlyList<GroupDto>>>
{
    public async Task<Result<IReadOnlyList<GroupDto>>> Handle(
        ListGroupsQuery request, CancellationToken ct)
    {
        var groups = await db.UserGroups
            .Where(g => request.TenantId == null || g.TenantId == request.TenantId)
            .OrderBy(g => g.Name)
            .Select(g => new GroupDto(
                g.PublicId, g.Code, g.Name, g.Description,
                g.IsActive, g.TenantId, g.CreatedAt))
            .ToListAsync(ct);

        return Result.Ok<IReadOnlyList<GroupDto>>(groups);
    }
}
