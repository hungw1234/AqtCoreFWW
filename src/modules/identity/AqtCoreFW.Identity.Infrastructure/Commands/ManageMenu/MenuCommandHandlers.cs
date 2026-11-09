using AqtCoreFW.Identity.Application.Commands.ManageMenu;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Identity.Infrastructure.Commands.ManageMenu;

/// <summary>
/// Creates a new menu item with its permission requirements.
/// Returns PublicId (Guid) — T2 external-facing identifier.
/// </summary>
public sealed class CreateMenuCommandHandler(IdentityDbContext db, IGuidGenerator guidGen)
    : IRequestHandler<CreateMenuCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(CreateMenuCommand cmd, CancellationToken ct)
    {
        var codeExists = await db.AppMenus.AnyAsync(
            m => m.Code == cmd.Code.Trim() && m.TenantId == cmd.TenantId, ct);

        if (codeExists)
            return Result.Fail(new ConflictError(
                $"Menu code '{cmd.Code}' already exists for this tenant."));

        // System actor (Guid.Empty) — command has no ActorId field
        var menu = AppMenu.Create(
            guidGen,
            cmd.Code,
            cmd.Title,
            cmd.ParentId,
            description: null,
            cmd.Icon,
            cmd.Route,
            cmd.SortOrder,
            cmd.TenantId,
            actorId: Guid.Empty);

        db.AppMenus.Add(menu);

        // Permission codes are added after Add so EF can fix up MenuId FK on SaveChanges
        foreach (var code in cmd.PermissionCodes)
            menu.RolePermissions.Add(new MenuRolePermission { PermissionCode = code });

        await db.SaveChangesAsync(ct);
        return Result.Ok(menu.PublicId);
    }
}

/// <summary>
/// Updates an existing menu item — replaces permission codes in full.
/// Menu is looked up by PublicId (T2 external key).
/// Global system menus (TenantId=null) are only editable by SystemAdmin.
/// </summary>
public sealed class UpdateMenuCommandHandler(IdentityDbContext db, ITenantContext tenantContext)
    : IRequestHandler<UpdateMenuCommand, Result>
{
    public async Task<Result> Handle(UpdateMenuCommand cmd, CancellationToken ct)
    {
        var menu = await db.AppMenus
            .Include(m => m.RolePermissions)
            .FirstOrDefaultAsync(m => m.PublicId == cmd.Id, ct);

        if (menu is null)
            return Result.Fail(new NotFoundError($"Menu {cmd.Id} not found."));

        // Guard: global system menus (TenantId=null) — only SystemAdmin may modify.
        if (menu.TenantId is null && !tenantContext.IsSystemAdmin)
            return Result.Fail(new ForbiddenError(
                "System menu can only be modified by SystemAdmin."));

        // System actor (Guid.Empty) — command has no ActorId field
        menu.Update(cmd.Title, description: null, cmd.Icon, cmd.Route, cmd.SortOrder, actorId: Guid.Empty);
        menu.UpdateParentId(cmd.ParentId, actorId: Guid.Empty);

        if (cmd.IsActive) menu.Activate(Guid.Empty);
        else menu.Deactivate(Guid.Empty);

        // Replace permission codes — remove old, add new
        db.MenuRolePermissions.RemoveRange(menu.RolePermissions);
        foreach (var code in cmd.PermissionCodes)
            menu.RolePermissions.Add(new MenuRolePermission { PermissionCode = code });

        await db.SaveChangesAsync(ct);
        return Result.Ok();
    }
}
