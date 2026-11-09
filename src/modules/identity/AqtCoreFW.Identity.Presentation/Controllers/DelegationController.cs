using AqtCoreFW.Identity.Application.Commands.ApproveDelegation;
using AqtCoreFW.Identity.Application.Commands.DelegateRole;
using AqtCoreFW.Identity.Application.Commands.RevokeDelegation;
using AqtCoreFW.Identity.Application.Queries.ListDelegations;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// Role delegation endpoints — any authenticated user can delegate or view their delegations.
/// Approve requires the delegator or a SystemAdmin — never the requester or delegate (4-eyes, enforced in handler).
/// Revocation requires the delegator or an Admin/SystemAdmin (enforced in handler).
/// </summary>
[Route("api/v1/delegations")]
[Authorize]
public sealed class DelegationController(ISender mediator) : ApiControllerBase
{
    /// <summary>
    /// GET /api/v1/delegations
    /// List delegations. A non-privileged caller sees only delegations they are a party to; an
    /// Admin/SystemAdmin may use the delegatorId/delegateId filters and sees a tenant-bound view
    /// (scope enforced in the query handler).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? delegatorId,
        [FromQuery] Guid? delegateId,
        [FromQuery] bool? activeOnly,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var isPrivileged = User.IsInRole(Roles.SystemAdmin) || User.IsInRole(Roles.Admin);
        return ToApiResponse(await mediator.Send(
            new ListDelegationsQuery(delegatorId, delegateId, activeOnly, GetActorId(), isPrivileged, page, pageSize), ct));
    }

    /// <summary>
    /// POST /api/v1/delegations
    /// Create a new role delegation for a bounded time window.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(201)]
    [ProducesResponseType(409)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] DelegateRoleRequest request,
        CancellationToken ct = default)
    {
        var actorId = GetActorId();
        var result = await mediator.Send(
            new DelegateRoleCommand(
                request.DelegatorId,
                request.DelegateId,
                request.ValidFrom,
                request.ValidTo,
                request.Reason,
                actorId), ct);

        if (result.IsSuccess)
            return CreatedAtAction(nameof(List), new { delegatorId = request.DelegatorId }, new { id = result.Value });

        return ToApiResponse(result);
    }

    /// <summary>
    /// POST /api/v1/delegations/{id}/approve
    /// Approve a PendingApproval delegation. Requires the delegator or a SystemAdmin — never the
    /// requester or the delegate (4-eyes; enforced in handler).
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new ApproveDelegationCommand(id, GetActorId()), ct));

    /// <summary>
    /// POST /api/v1/delegations/{id}/revoke
    /// Revoke an active delegation via POST (alternative to DELETE).
    /// </summary>
    [HttpPost("{id:guid}/revoke")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> RevokePost(Guid id, CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new RevokeDelegationCommand(id, GetActorId()), ct));

    /// <summary>
    /// DELETE /api/v1/delegations/{id}
    /// Revoke an active delegation (REST-idiomatic).
    /// </summary>
    [HttpDelete("{id:guid}")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new RevokeDelegationCommand(id, GetActorId()), ct));

    // --- helpers ---

    private Guid GetActorId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}

/// <summary>Request body for delegation creation.</summary>
public sealed record DelegateRoleRequest(
    Guid DelegatorId,
    Guid DelegateId,
    DateTime ValidFrom,
    DateTime ValidTo,
    string Reason);
