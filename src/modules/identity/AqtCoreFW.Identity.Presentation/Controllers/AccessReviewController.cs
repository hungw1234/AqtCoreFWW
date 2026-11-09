using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Application.Commands.DecideAccessReview;
using AqtCoreFW.Identity.Application.Queries.ListPendingAccessReviews;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// Admin endpoints for the QÄ742 periodic access review workflow.
/// Requires Admin or SystemAdmin role.
/// </summary>
[Route("api/v1/admin/access-reviews")]
[Authorize(Policy = PermCodes.Identity.Role.View)]
[RequiresMfa]
[EnableRateLimiting("write-ops")]
public sealed class AccessReviewController(ISender mediator) : ApiControllerBase
{
    /// <summary>List pending access reviews (Decision == null) for the resolved tenant.</summary>
    [HttpGet("pending")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetPending(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        return ToApiResponse(await mediator.Send(
            new ListPendingAccessReviewsQuery(ResolveTenantId(), page, pageSize), ct));
    }

    /// <summary>Approve a pending access review â€” retains the user's role.</summary>
    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new DecideAccessReviewCommand(id, AccessReviewDecision.Retain, ResolveUserId()), ct));

    /// <summary>Reject a pending access review â€” marks role for revocation.</summary>
    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Reject(Guid id, CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new DecideAccessReviewCommand(id, AccessReviewDecision.Revoke, ResolveUserId()), ct));
}
