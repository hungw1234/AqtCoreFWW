using AqtCoreFW.Identity.Application.Queries.GetRoleDataScopes;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Queries.DataScope;

/// <summary>Handles GetRoleDataScopesQuery — loads data scope assignments for a role.</summary>
public sealed class GetRoleDataScopesQueryHandler(IdentityDbContext db)
    : IRequestHandler<GetRoleDataScopesQuery, Result<IReadOnlyList<RoleDataScopeDto>>>
{
    public async Task<Result<IReadOnlyList<RoleDataScopeDto>>> Handle(
        GetRoleDataScopesQuery request, CancellationToken ct)
    {
        var scopes = await db.RoleDataScopes
            .Include(s => s.DataScopeType)
            .Where(s => s.RoleId == request.RoleId)
            .OrderByDescending(s => s.Priority)
            .Select(s => new RoleDataScopeDto(
                s.Id, s.RoleId, s.DataScopeTypeId,
                s.DataScopeType.Code, s.ScopeField, s.ScopeValue, s.Priority))
            .ToListAsync(ct);

        return Result.Ok<IReadOnlyList<RoleDataScopeDto>>(scopes);
    }
}
