using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Application.Commands.CreateCredentialPolicy;
using AqtCoreFW.Identity.Application.Commands.DeleteCredentialPolicy;
using AqtCoreFW.Identity.Application.Commands.UpdateCredentialPolicy;
using AqtCoreFW.Identity.Application.Queries.GetCredentialPolicyById;
using AqtCoreFW.Identity.Application.Queries.ListCredentialPolicies;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// Credential (password) policy management endpoints â€” per-tenant policy configuration.
/// Requires Admin role.
/// </summary>
[Route("api/v1/admin/credential-policies")]
[Authorize(Policy = PermCodes.Identity.Policy.View)]
[RequiresMfa]
[EnableRateLimiting("write-ops")]
public sealed class CredentialPoliciesController(ISender mediator) : ApiControllerBase
{
    /// <summary>List credential policies for the current tenant (paginated).</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var tenantId = ResolveTenantId();
        return ToApiResponse(await mediator.Send(
            new ListCredentialPoliciesQuery(tenantId, page, pageSize), ct));
    }

    /// <summary>Get a single credential policy by ID.</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(long id, CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new GetCredentialPolicyByIdQuery(id), ct));

    /// <summary>Create a new credential policy for the current tenant.</summary>
    [HttpPost]
    [Authorize(Policy = PermCodes.Identity.Policy.Manage)]
    [ProducesResponseType(201)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateCredentialPolicyRequest request,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        var result = await mediator.Send(
            new CreateCredentialPolicyCommand(
                request.Name, tenantId,
                request.MinLength, request.MaxLength,
                request.RequireUppercase, request.RequireLowercase,
                request.RequireDigit, request.RequireSpecialChar,
                request.RotationDays, request.MaxFailedAttempts,
                request.LockoutMinutes, request.PasswordHistoryCount,
                request.IsDefault, ResolveUserId()), ct);

        if (result.IsSuccess)
            return CreatedAtAction(nameof(GetById), new { id = result.Value }, null);

        return ToApiResponse(result);
    }

    /// <summary>Update an existing credential policy.</summary>
    [HttpPut("{id:long}")]
    [Authorize(Policy = PermCodes.Identity.Policy.Manage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Update(
        long id,
        [FromBody] UpdateCredentialPolicyRequest request,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new UpdateCredentialPolicyCommand(
                id, request.Name,
                request.MinLength, request.MaxLength,
                request.RequireUppercase, request.RequireLowercase,
                request.RequireDigit, request.RequireSpecialChar,
                request.RotationDays, request.MaxFailedAttempts,
                request.LockoutMinutes, request.PasswordHistoryCount,
                request.IsDefault, ResolveUserId()), ct));

    /// <summary>Soft-delete a credential policy.</summary>
    [HttpDelete("{id:long}")]
    [Authorize(Policy = PermCodes.Identity.Policy.Manage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new DeleteCredentialPolicyCommand(id, ResolveUserId()), ct));
}

public sealed record CreateCredentialPolicyRequest(
    string Name,
    int MinLength,
    int MaxLength,
    bool RequireUppercase,
    bool RequireLowercase,
    bool RequireDigit,
    bool RequireSpecialChar,
    int RotationDays,
    int MaxFailedAttempts,
    int LockoutMinutes,
    int PasswordHistoryCount,
    bool IsDefault);

public sealed record UpdateCredentialPolicyRequest(
    string Name,
    int MinLength,
    int MaxLength,
    bool RequireUppercase,
    bool RequireLowercase,
    bool RequireDigit,
    bool RequireSpecialChar,
    int RotationDays,
    int MaxFailedAttempts,
    int LockoutMinutes,
    int PasswordHistoryCount,
    bool IsDefault);
