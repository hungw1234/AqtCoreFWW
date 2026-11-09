using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Forms.Application.Commands.RetryWorkflowLinkNow;
using AqtCoreFW.Forms.Application.Commands.ShelveWorkflowLink;
using AqtCoreFW.Forms.Application.Queries.ListPendingWorkflowLinks;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Forms.Presentation.Controllers;

/// <summary>
/// Admin "Pending Workflow Links" endpoints (P6-DEFER-1 phase 05): list orphan
/// FormSubmission→Workflow link attempts + manual Retry-now / Shelve overrides.
/// Gated entirely by <see cref="PermCodes.Forms.WorkflowLink.Manage"/> — a dedicated
/// least-privilege permission (LOCKED Q3), NOT a reuse of Submit.Approve.
///
/// Retry-now only re-arms the row (NextRetryAt = now); the Hangfire retry job (phase 04) remains
/// the single create path (DRY) and claims the row on its next tick.
/// </summary>
[Route("api/v1/forms/workflow-link-pending")]
[Authorize]
public sealed class WorkflowLinkPendingController(ISender mediator) : ApiControllerBase
{
    /// <summary>Paged list of pending/retrying/succeeded/shelved workflow-link attempts for the tenant.</summary>
    [HttpGet]
    [Authorize(Policy = PermCodes.Forms.WorkflowLink.Manage)]
    [ProducesResponseType(200)]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new ListPendingWorkflowLinksQuery(ResolveTenantId(), status, page, pageSize),
            cancellationToken));

    /// <summary>
    /// Force an immediate retry attempt now. Re-arms NextRetryAt so the auto job claims the row on
    /// its next tick — this endpoint does NOT create the workflow instance itself (single create
    /// path, DRY). Un-shelves a Shelved row back to Pending (manual-path only, LOCKED Q4).
    /// Returns 202 Accepted: the actual resolve/create happens asynchronously via the retry job.
    /// </summary>
    [HttpPost("{submissionId:guid}/retry")]
    [Authorize(Policy = PermCodes.Forms.WorkflowLink.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(202)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> RetryNow(
        Guid submissionId,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new RetryWorkflowLinkNowCommand(submissionId, ResolveTenantId(), ResolveUserId()),
            cancellationToken);

        return result.IsSuccess ? Accepted() : ToApiResponse(result);
    }

    /// <summary>Terminalize (dead-letter) a pending orphan workflow-link row.</summary>
    [HttpPost("{submissionId:guid}/shelve")]
    [Authorize(Policy = PermCodes.Forms.WorkflowLink.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    public async Task<IActionResult> Shelve(
        Guid submissionId,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new ShelveWorkflowLinkCommand(submissionId, ResolveTenantId(), ResolveUserId()),
            cancellationToken));
}
