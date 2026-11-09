using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Application.Commands.AssignRole;
using AqtCoreFW.Identity.Application.Commands.DeleteUser;
using AqtCoreFW.Identity.Application.Commands.SyncUserRoles;
using AqtCoreFW.Identity.Application.Commands.LockUser;
using AqtCoreFW.Identity.Application.Commands.RegisterUser;
using AqtCoreFW.Identity.Application.Commands.ResetPassword;
using AqtCoreFW.Identity.Application.Commands.UpdateUser;
using AqtCoreFW.Identity.Application.Queries.GetUserById;
using AqtCoreFW.Identity.Application.Queries.GetUserEffectivePermissions;
using AqtCoreFW.Identity.Application.Queries.ListTenants;
using AqtCoreFW.Identity.Application.Queries.ListUsers;
using AqtCoreFW.SharedKernel.Presentation;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// Admin endpoints for user lifecycle management.
/// Requires IDENTITY.USER.VIEW permission for read, specific perms for write actions.
/// </summary>
[Route("api/v1/admin/users")]
[Authorize(Policy = PermCodes.Identity.User.View)]
[RequiresMfa]
[EnableRateLimiting("write-ops")]
public sealed class UsersAdminController(ISender mediator) : ApiControllerBase
{
    /// <summary>List users with optional filters and pagination.</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? departmentCode,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var tenantId = ResolveTenantId();
        return ToApiResponse(await mediator.Send(
            new ListUsersQuery(tenantId, search, departmentCode, isActive, page, pageSize), ct));
    }

    /// <summary>List distinct tenants with user counts â€” SystemAdmin only.</summary>
    [HttpGet("tenants")]
    [Authorize(Policy = PermCodes.System.Config.View)]
    [ProducesResponseType(200)]
    public async Task<IActionResult> ListTenants(CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new ListTenantsQuery(), ct));

    /// <summary>Get a single user by ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(
        Guid id,
        [FromServices] IAuthorizationService authz,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetUserByIdQuery(id), ct);

        // SigningNationalId (CCCD) is Confidential PII. The class is gated on User.View, but the
        // CCCD is needed only to prefill the edit form — narrow it to callers who can actually edit
        // (User.Update); View-only callers receive it nulled.
        if (result.IsSuccess && result.Value.SigningNationalId is not null)
        {
            var canEdit = await authz.AuthorizeAsync(User, PermCodes.Identity.User.Update);
            if (!canEdit.Succeeded)
                result = Result.Ok(result.Value with { SigningNationalId = null });
        }

        return ToApiResponse(result);
    }

    /// <summary>Create a new user (admin-initiated registration).</summary>
    [HttpPost]
    [Authorize(Policy = PermCodes.Identity.User.Create)]
    [ProducesResponseType(201)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateUserRequest request,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        var actorId = ResolveUserId();
        var result = await mediator.Send(
            new RegisterUserCommand(request.FullName, request.Email, request.Password,
                request.DepartmentCode, tenantId, request.SigningNationalId), ct);

        if (result.IsSuccess)
            return CreatedAtAction(nameof(GetById), new { id = result.Value }, null);

        return ToApiResponse(result);
    }

    /// <summary>Update user profile (name, department).</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermCodes.Identity.User.Update)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateUserRequest request,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new UpdateUserCommand(id, request.FullName, request.DepartmentCode, request.OrgUnitId,
                ResolveUserId(), request.SigningNationalId), ct));

    /// <summary>Soft-delete (deactivate) a user â€” audit-compliant, no hard delete.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermCodes.Identity.User.Delete)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new DeleteUserCommand(id, ResolveUserId()), ct));

    /// <summary>Lock a user account.</summary>
    [HttpPost("{id:guid}/lock")]
    [Authorize(Policy = PermCodes.Identity.User.Update)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Lock(Guid id, CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new LockUserCommand(id, Lock: true, ResolveUserId()), ct));

    /// <summary>Unlock a user account.</summary>
    [HttpPost("{id:guid}/unlock")]
    [Authorize(Policy = PermCodes.Identity.User.Update)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Unlock(Guid id, CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new LockUserCommand(id, Lock: false, ResolveUserId()), ct));

    /// <summary>Admin-initiated password reset â€” emails reset link to user (F-25: token never in response).</summary>
    [HttpPost("{id:guid}/reset-password")]
    [Authorize(Policy = PermCodes.Identity.User.Update)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> ResetPassword(Guid id, CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new ResetPasswordCommand(id, ResolveUserId()), ct));

    /// <summary>Assign a role to a user (additive â€” keeps existing roles).</summary>
    [HttpPost("{id:guid}/roles")]
    [Authorize(Policy = PermCodes.Identity.Role.Manage)]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> AssignRole(
        Guid id,
        [FromBody] AssignRoleRequest request,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new AssignRoleCommand(id, request.Role, ResolveUserId()), ct));

    /// <summary>Replace all roles for a user â€” removes unlisted roles, adds new ones.</summary>
    [HttpPut("{id:guid}/roles")]
    [Authorize(Policy = PermCodes.Identity.Role.Manage)]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> SyncRoles(
        Guid id,
        [FromBody] SyncRolesRequest request,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new SyncUserRolesCommand(id, request.Roles, ResolveUserId()), ct));

    /// <summary>
    /// Get the full effective permission summary for a user:
    /// direct roles + group-inherited roles + active delegations + resolved data scope.
    /// Result is Redis-cached (TTL 10 min).
    /// </summary>
    [HttpGet("{id:guid}/effective-permissions")]
    [Authorize(Policy = PermCodes.Identity.User.View)]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetEffectivePermissions(Guid id, CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new GetUserEffectivePermissionsQuery(id), ct));
}

/// <summary>Request body for admin user creation â€” not bound to Entity (GOV_SEC_004).</summary>
public sealed record CreateUserRequest(
    string FullName,
    string Email,
    string Password,
    string? DepartmentCode,
    string? SigningNationalId = null);

/// <summary>Request body for user profile update.</summary>
public sealed record UpdateUserRequest(
    string FullName,
    string? DepartmentCode,
    Guid? OrgUnitId,
    string? SigningNationalId = null);

/// <summary>Request body for role assignment (single, additive).</summary>
public sealed record AssignRoleRequest(string Role);

/// <summary>Request body for role sync (replace all roles).</summary>
public sealed record SyncRolesRequest(IReadOnlyList<string> Roles);
