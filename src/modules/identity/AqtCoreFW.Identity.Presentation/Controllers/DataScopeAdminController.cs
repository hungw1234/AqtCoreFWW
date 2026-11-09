using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Application.Commands.ManageDataScope;
using AqtCoreFW.Identity.Application.Queries.GetRoleDataScopes;
using AqtCoreFW.Identity.Application.Queries.ListDataScopeTypes;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// Admin endpoints for data scope configuration â€” controls what data rows a role can access.
/// Requires Admin or SystemAdmin role.
/// </summary>
[Route("api/v1/admin/data-scopes")]
[Authorize(Policy = PermCodes.Identity.DataScope.Manage)]
[RequiresMfa]
[EnableRateLimiting("write-ops")]
public sealed class DataScopeAdminController(ISender mediator) : ApiControllerBase
{
    /// <summary>List all available data scope type codes (SELF, ORG_UNIT, ALL, etc.).</summary>
    [HttpGet("types")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> ListTypes(CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new ListDataScopeTypesQuery(), ct));

    /// <summary>Get all data scope assignments for a specific role.</summary>
    [HttpGet("roles/{roleId:guid}")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetRoleScopes(Guid roleId, CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new GetRoleDataScopesQuery(roleId), ct));

    /// <summary>Assign a data scope type to a role.</summary>
    [HttpPost("roles/{roleId:guid}")]
    [ProducesResponseType(201)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> CreateRoleScope(
        Guid roleId,
        [FromBody] CreateRoleDataScopeRequest request,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new CreateRoleDataScopeCommand(
            roleId, request.DataScopeTypeId, request.ScopeField,
            request.ScopeValue, request.Priority), ct);

        if (result.IsSuccess)
            return StatusCode(201, new { id = result.Value });

        return ToApiResponse(result);
    }

    /// <summary>Remove a data scope assignment from a role.</summary>
    [HttpDelete("roles/{roleId:guid}/{scopeId:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteRoleScope(
        Guid roleId, Guid scopeId, CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new DeleteRoleDataScopeCommand(scopeId), ct));
}

public sealed record CreateRoleDataScopeRequest(
    Guid DataScopeTypeId,
    string? ScopeField,
    string? ScopeValue,
    int Priority = 0);
