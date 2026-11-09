using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Application.Commands.ManageMenu;
using AqtCoreFW.Identity.Application.Queries.ListMenus;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// Menu authorization endpoints.
/// GET /my-menus â€” returns the authenticated user's authorized menu tree.
/// Admin sub-routes manage menu structure and permission assignments.
/// </summary>
[Route("api/v1/menus")]
[Authorize]
public sealed class MenuController(
    ISender mediator,
    IEffectivePermissionService permissionService,
    IMenuService menuService) : ApiControllerBase
{
    /// <summary>
    /// Returns the menu tree filtered to items the current user is authorized to see.
    /// Uses effective permissions (direct roles + group roles + delegations).
    /// </summary>
    [HttpGet("my-menus")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetMyMenus(CancellationToken ct = default)
    {
        var userId = ResolveUserId();
        if (userId == Guid.Empty)
            return Unauthorized();

        var summary = await permissionService.GetSummaryAsync(userId, ct);
        var tree = await menuService.GetMenuTreeAsync(summary.PermissionCodes, ct);
        return Ok(tree);
    }

    /// <summary>List all menus regardless of permissions (admin view).</summary>
    [HttpGet("admin/menus")]
    [Authorize(Policy = PermCodes.Identity.Menu.Manage)]
    [ProducesResponseType(200)]
    public async Task<IActionResult> ListAll(CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        return ToApiResponse(await mediator.Send(new ListMenusQuery(tenantId), ct));
    }

    /// <summary>Create a new menu item with permission requirements.</summary>
    [HttpPost("admin/menus")]
    [Authorize(Policy = PermCodes.Identity.Menu.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(201)]
    [ProducesResponseType(409)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateMenuRequest request,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        var result = await mediator.Send(new CreateMenuCommand(
            request.ParentId,
            request.Code,
            request.Title,
            request.Icon,
            request.Route,
            request.SortOrder,
            request.PermissionCodes ?? [],
            tenantId), ct);

        if (result.IsSuccess)
            return StatusCode(201, new { id = result.Value });

        return ToApiResponse(result);
    }

    /// <summary>Update a menu item's properties and permission requirements.</summary>
    [HttpPut("admin/menus/{id:guid}")]
    [Authorize(Policy = PermCodes.Identity.Menu.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateMenuRequest request,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new UpdateMenuCommand(
            id,
            request.ParentId,
            request.Title,
            request.Icon,
            request.Route,
            request.SortOrder,
            request.IsActive,
            request.PermissionCodes ?? []), ct));
}

public sealed record CreateMenuRequest(
    Guid? ParentId,
    string Code,
    string Title,
    string? Icon,
    string? Route,
    int SortOrder,
    IReadOnlyList<string>? PermissionCodes);

public sealed record UpdateMenuRequest(
    Guid? ParentId,
    string Title,
    string? Icon,
    string? Route,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<string>? PermissionCodes);
