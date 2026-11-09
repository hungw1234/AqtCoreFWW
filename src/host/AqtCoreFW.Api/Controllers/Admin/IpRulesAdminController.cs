using AqtCoreFW.Identity.Application.IpRules;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Api;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Api.Controllers.Admin;

/// <summary>
/// Admin CRUD for IP filter rules (QĐ 742 §2.2.a — IP whitelist/blocklist management).
/// Rules are loaded by IpFilterMiddleware from cache (DB fallback on cache miss, TTL 5 min).
///
/// T1 entity — addressed by long Id.
/// Class-level [RequiresMfa]: all mutations require a valid MFA session (QĐ 742 1.6.b).
/// </summary>
[Route("api/v1/admin/security/ip-rules")]
[Authorize(Policy = PermCodes.Security.IpRule.View)]
[RequiresMfa]
[EnableRateLimiting("write-ops")]
public sealed class IpRulesAdminController(ISender mediator) : ApiControllerBase
{
    /// <summary>GET /api/v1/admin/security/ip-rules — list all rules.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<IpRuleDto>>), 200)]
    public async Task<IActionResult> List(CancellationToken ct = default)
        => ToApiResponse(await mediator.Send(new ListIpRulesQuery(), ct));

    /// <summary>GET /api/v1/admin/security/ip-rules/{id} — get rule by internal Id.</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType(typeof(ApiResponse<IpRuleDto>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> GetById(long id, CancellationToken ct = default)
        => ToApiResponse(await mediator.Send(new GetIpRuleQuery(id), ct));

    /// <summary>
    /// POST /api/v1/admin/security/ip-rules — create a new rule.
    /// Returns 201 Created with the new rule long Id.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = PermCodes.Security.IpRule.Manage)]
    [ProducesResponseType(typeof(ApiResponse<long>), 201)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    public async Task<IActionResult> Create(
        [FromBody] CreateIpRuleRequest request,
        CancellationToken ct = default)
    {
        var actorId = ResolveUserId();
        var result = await mediator.Send(
            new CreateIpRuleCommand(
                request.TenantId,
                request.Cidr,
                request.Description ?? string.Empty,
                request.IsEnabled,
                request.IsBlock,
                actorId), ct);

        if (result.IsFailed) return ToApiResponse(result);
        return StatusCode(201, ApiResponse<long>.Ok(result.Value));
    }

    /// <summary>PUT /api/v1/admin/security/ip-rules/{id} — update an existing rule.</summary>
    [HttpPut("{id:long}")]
    [Authorize(Policy = PermCodes.Security.IpRule.Manage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> Update(
        long id,
        [FromBody] UpdateIpRuleRequest request,
        CancellationToken ct = default)
    {
        var actorId = ResolveUserId();
        return ToApiResponse(await mediator.Send(
            new UpdateIpRuleCommand(
                id,
                request.Cidr,
                request.Description ?? string.Empty,
                request.IsEnabled,
                request.IsBlock,
                actorId), ct));
    }

    /// <summary>DELETE /api/v1/admin/security/ip-rules/{id} — soft-delete a rule.</summary>
    [HttpDelete("{id:long}")]
    [Authorize(Policy = PermCodes.Security.IpRule.Manage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct = default)
        => ToApiResponse(await mediator.Send(new DeleteIpRuleCommand(id), ct));
}

// ── Request DTOs ────────────────────────────────────────────────────────────

public sealed record CreateIpRuleRequest(
    Guid? TenantId,
    string Cidr,
    string? Description,
    bool IsEnabled = true,
    bool IsBlock = true);

public sealed record UpdateIpRuleRequest(
    string Cidr,
    string? Description,
    bool IsEnabled,
    bool IsBlock);
