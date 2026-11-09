using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Application.Commands.ActivateRole;
using AqtCoreFW.Identity.Application.Commands.CreateRole;
using AqtCoreFW.Identity.Application.Commands.DeleteRole;
using AqtCoreFW.Identity.Application.Commands.SyncRolePermissions;
using AqtCoreFW.Identity.Application.Commands.UpdateRole;
using AqtCoreFW.Identity.Application.Queries.GetRoleById;
using AqtCoreFW.Identity.Application.Queries.GetRolePermissions;
using AqtCoreFW.Identity.Application.Queries.GetRoles;
using AqtCoreFW.Identity.Application.Queries.ListPermissions;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// Role CRUD endpoints â€” manage business roles and permissions.
/// System roles are read-only (cannot rename or delete).
/// </summary>
[Route("api/v1/admin/roles")]
[Authorize(Policy = PermCodes.Identity.Role.View)]
[RequiresMfa]
public sealed class RolesAdminController(ISender mediator) : ApiControllerBase
{
    // â”€â”€â”€ Queries â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Returns all role definitions filtered by tenant [RT-07].</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetRoles(CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        return ToApiResponse(await mediator.Send(new GetRolesQuery(tenantId), ct));
    }

    /// <summary>Returns a single role by ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetRoleById(Guid id, CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        return ToApiResponse(await mediator.Send(new GetRoleByIdQuery(id, tenantId), ct));
    }

    // â”€â”€â”€ Commands â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Create a new business role.</summary>
    [HttpPost]
    [Authorize(Policy = PermCodes.Identity.Role.Manage)]
    [ProducesResponseType(201)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> CreateRole(
        [FromBody] CreateRoleRequest body,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId(); // RT-08: TenantId from JWT
        var actorId = ResolveUserId();

        var result = await mediator.Send(
            new CreateRoleCommand(body.Name, body.Code, body.Description,
                body.RoleType ?? RoleType.Business, tenantId, actorId), ct);

        if (result.IsFailed) return ToApiResponse(result);
        return StatusCode(201, new { id = result.Value });
    }

    /// <summary>Update role name and description.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermCodes.Identity.Role.Manage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> UpdateRole(
        Guid id,
        [FromBody] UpdateRoleRequest body,
        CancellationToken ct = default)
    {
        var actorId = ResolveUserId();
        var tenantId = ResolveTenantId();
        return ToApiResponse(await mediator.Send(
            new UpdateRoleCommand(id, body.Name, body.Description, actorId, tenantId), ct));
    }

    /// <summary>Soft-delete a business role (System roles return 403).</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermCodes.Identity.Role.Manage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteRole(Guid id, CancellationToken ct = default)
    {
        var actorId = ResolveUserId();
        var tenantId = ResolveTenantId();
        return ToApiResponse(await mediator.Send(new DeleteRoleCommand(id, actorId, tenantId), ct));
    }

    /// <summary>Re-activate a previously deactivated role.</summary>
    [HttpPut("{id:guid}/activate")]
    [Authorize(Policy = PermCodes.Identity.Role.Manage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> ActivateRole(Guid id, CancellationToken ct = default)
    {
        var actorId = ResolveUserId();
        var tenantId = ResolveTenantId();
        return ToApiResponse(await mediator.Send(new ActivateRoleCommand(id, actorId, tenantId), ct));
    }

    // â”€â”€â”€ Permission endpoints â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Returns all seeded permissions.</summary>
    [HttpGet("/api/v1/admin/permissions")]
    [Authorize(Policy = PermCodes.Identity.Perm.Assign)]
    [ProducesResponseType(200)]
    public async Task<IActionResult> ListPermissions(CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new ListPermissionsQuery(), ct));

    /// <summary>Returns permissions assigned to a role.</summary>
    [HttpGet("{id:guid}/permissions")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetRolePermissions(Guid id, CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new GetRolePermissionsQuery(id), ct));

    /// <summary>Replace all permissions for a role (idempotent sync).</summary>
    [HttpPut("{id:guid}/permissions")]
    [Authorize(Policy = PermCodes.Identity.Perm.Assign)]
    [ProducesResponseType(200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> SyncRolePermissions(
        Guid id,
        [FromBody] SyncRolePermissionsRequest body,
        CancellationToken ct = default)
    {
        var actorId = ResolveUserId();
        var tenantId = ResolveTenantId();
        var confirmed = Request.Headers.ContainsKey("X-Confirm-SoD");
        return ToApiResponse(await mediator.Send(
            new SyncRolePermissionsCommand(id, body.PermissionIds, actorId, tenantId, confirmed, body.Justification), ct));
    }
}

// â”€â”€â”€ Request DTOs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public sealed class CreateRoleRequest
{
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string? Description { get; init; }
    public RoleType? RoleType { get; init; }
}

public sealed class UpdateRoleRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}

public sealed class SyncRolePermissionsRequest
{
    public IReadOnlyList<Guid> PermissionIds { get; init; } = [];
    /// <summary>Admin justification for overriding SoD warnings (max 2000 chars).</summary>
    public string? Justification { get; init; }
}
