using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Application.Commands.ManageGroup;
using AqtCoreFW.Identity.Application.Queries.GetGroupById;
using AqtCoreFW.Identity.Application.Queries.ListGroups;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// Admin CRUD for user groups â€” bulk role assignment via group membership.
/// Requires Admin or SystemAdmin role.
/// </summary>
[Route("api/v1/admin/groups")]
[Authorize(Policy = PermCodes.Identity.Group.View)]
[RequiresMfa]
[EnableRateLimiting("write-ops")]
public sealed class GroupsAdminController(ISender mediator) : ApiControllerBase
{
    /// <summary>List all groups for the current tenant.</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> List(CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        return ToApiResponse(await mediator.Send(new ListGroupsQuery(tenantId), ct));
    }

    /// <summary>Get group details with member count and role assignments.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new GetGroupByIdQuery(id), ct));

    /// <summary>Create a new user group.</summary>
    [HttpPost]
    [Authorize(Policy = PermCodes.Identity.Group.Manage)]
    [ProducesResponseType(201)]
    [ProducesResponseType(409)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateGroupRequest request,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        var result = await mediator.Send(
            new CreateGroupCommand(request.Code, request.Name, request.Description, tenantId, ResolveUserId()), ct);

        if (result.IsSuccess)
            return StatusCode(201, new { id = result.Value });

        return ToApiResponse(result);
    }

    /// <summary>Update group name/description/active status.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermCodes.Identity.Group.Manage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateGroupRequest request,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new UpdateGroupCommand(id, request.Name, request.Description, request.IsActive), ct));

    /// <summary>Add a user to a group (idempotent).</summary>
    [HttpPost("{id:guid}/members")]
    [Authorize(Policy = PermCodes.Identity.Group.Manage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> AddMember(
        Guid id,
        [FromBody] GroupMemberRequest request,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new AddUserToGroupCommand(id, request.UserId, ResolveUserId()), ct));

    /// <summary>Remove a user from a group.</summary>
    [HttpDelete("{id:guid}/members/{userId:guid}")]
    [Authorize(Policy = PermCodes.Identity.Group.Manage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> RemoveMember(
        Guid id, Guid userId, CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new RemoveUserFromGroupCommand(id, userId), ct));

    /// <summary>Assign a role to a group â€” all members inherit the role.</summary>
    [HttpPost("{id:guid}/roles")]
    [Authorize(Policy = PermCodes.Identity.Group.Manage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> AssignRole(
        Guid id,
        [FromBody] GroupRoleRequest request,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new AssignRoleToGroupCommand(id, request.RoleId, ResolveUserId()), ct));

    /// <summary>Remove a role from a group.</summary>
    [HttpDelete("{id:guid}/roles/{roleId:guid}")]
    [Authorize(Policy = PermCodes.Identity.Group.Manage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> RemoveRole(
        Guid id, Guid roleId, CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new RemoveRoleFromGroupCommand(id, roleId), ct));
}

public sealed record CreateGroupRequest(string Code, string Name, string? Description);
public sealed record UpdateGroupRequest(string Name, string? Description, bool? IsActive);
public sealed record GroupMemberRequest(Guid UserId);
public sealed record GroupRoleRequest(Guid RoleId);
