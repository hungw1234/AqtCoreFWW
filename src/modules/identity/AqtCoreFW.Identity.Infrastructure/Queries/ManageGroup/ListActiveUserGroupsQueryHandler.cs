using AqtCoreFW.Identity.Application.Queries.ListActiveUserGroups;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Queries.ManageGroup;

/// <summary>
/// Handles ListActiveUserGroupsQuery — lightweight picker query for Workflow ByGroup clause editor.
/// Returns groups scoped to the caller's tenant, sorted by Name ASC.
/// Filters to IsActive=true when ActiveOnly=true (default).
/// </summary>
public sealed class ListActiveUserGroupsQueryHandler(IdentityDbContext db)
    : IRequestHandler<ListActiveUserGroupsQuery, Result<IReadOnlyList<UserGroupSummaryDto>>>
{
    public async Task<Result<IReadOnlyList<UserGroupSummaryDto>>> Handle(
        ListActiveUserGroupsQuery request,
        CancellationToken ct)
    {
        var query = db.UserGroups
            .Where(g => g.TenantId == request.TenantId || g.TenantId == null);

        if (request.ActiveOnly)
            query = query.Where(g => g.IsActive);

        var groups = await query
            .OrderBy(g => g.Name)
            .Select(g => new UserGroupSummaryDto(g.PublicId, g.Code, g.Name, g.IsActive))
            .ToListAsync(ct);

        return Result.Ok<IReadOnlyList<UserGroupSummaryDto>>(groups);
    }
}
