using AqtCoreFW.Identity.Application.Queries.ListMenus;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Queries.Menu;

/// <summary>Handles ListMenusQuery — returns all menus with their permission codes (admin view).</summary>
public sealed class ListMenusQueryHandler(IdentityDbContext db)
    : IRequestHandler<ListMenusQuery, Result<IReadOnlyList<MenuDto>>>
{
    public async Task<Result<IReadOnlyList<MenuDto>>> Handle(
        ListMenusQuery request, CancellationToken ct)
    {
        // Union of global (TenantId=null, seeded system menus) + tenant-specific menus.
        // Matches MenuService.LoadFromDbAsync filter so admin view is consistent with sidebar.
        var menus = await db.AppMenus
            .Include(m => m.RolePermissions)
            .Where(m => m.TenantId == null || m.TenantId == request.TenantId)
            .OrderBy(m => m.SortOrder)
            .Select(m => new MenuDto(
                m.PublicId,
                m.ParentId,
                m.Code,
                m.Title,
                m.Icon,
                m.Route,
                m.SortOrder,
                m.IsActive,
                m.TenantId,
                m.RolePermissions.Select(rp => rp.PermissionCode).ToList()))
            .ToListAsync(ct);

        return Result.Ok<IReadOnlyList<MenuDto>>(menus);
    }
}
