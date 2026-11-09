using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Application.Commands.ManagePolicyRule;
using AqtCoreFW.Identity.Application.Queries.ListPolicyRules;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// Admin CRUD for policy rules â€” condition-based allow/deny rules evaluated per permission code.
/// Rules are sorted by Priority (desc); first match wins; Deny takes precedence.
/// Requires Admin or SystemAdmin role.
/// </summary>
[Route("api/v1/admin/policy-rules")]
[Authorize(Policy = PermCodes.Identity.Policy.View)]
[RequiresMfa]
[EnableRateLimiting("write-ops")]
public sealed class PolicyRulesAdminController(ISender mediator) : ApiControllerBase
{
    /// <summary>List policy rules for the resolved tenant, optionally filtered by permission code.</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> List(
        [FromQuery] string? permissionCode,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new ListPolicyRulesQuery(ResolveTenantId(), permissionCode), ct));

    /// <summary>Create a new policy rule.</summary>
    [HttpPost]
    [Authorize(Policy = PermCodes.Identity.Policy.Manage)]
    [ProducesResponseType(201)]
    [ProducesResponseType(409)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreatePolicyRuleRequest request,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new CreatePolicyRuleCommand(
            request.Code, request.PermissionCode, request.ConditionExpression,
            request.Effect, request.Priority, request.LogOnDeny,
            request.Description, ResolveTenantId()), ct);

        if (result.IsSuccess)
            return StatusCode(201, new { id = result.Value });

        return ToApiResponse(result);
    }

    /// <summary>Update an existing policy rule.</summary>
    [HttpPut("{id:long}")]
    [Authorize(Policy = PermCodes.Identity.Policy.Manage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Update(
        long id,
        [FromBody] UpdatePolicyRuleRequest request,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new UpdatePolicyRuleCommand(
            id, request.PermissionCode, request.ConditionExpression,
            request.Effect, request.Priority, request.IsActive,
            request.LogOnDeny, request.Description), ct));

    /// <summary>Delete a policy rule by ID.</summary>
    [HttpDelete("{id:long}")]
    [Authorize(Policy = PermCodes.Identity.Policy.Manage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new DeletePolicyRuleCommand(id), ct));
}

public sealed record CreatePolicyRuleRequest(
    string Code,
    string PermissionCode,
    string? ConditionExpression,
    string Effect,
    int Priority,
    bool LogOnDeny,
    string? Description);

public sealed record UpdatePolicyRuleRequest(
    string PermissionCode,
    string? ConditionExpression,
    string Effect,
    int Priority,
    bool IsActive,
    bool LogOnDeny,
    string? Description);
