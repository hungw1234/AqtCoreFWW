using AqtCoreFW.Forms.Application.Queries.GetFormAnalytics;
using AqtCoreFW.Forms.Application.Queries.GetFormVersionDiff;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqtCoreFW.Forms.Presentation.Controllers;

/// <summary>
/// Template extension endpoints: analytics + version diff.
/// Kept separate to stay under 200 lines per file.
/// </summary>
[Route("api/v1/forms/templates")]
[Authorize]
public sealed class FormTemplatesExtController(ISender mediator) : ApiControllerBase
{
    /// <summary>Get aggregate analytics for a form template (status counts + last 30 days by date).</summary>
    [HttpGet("{publicId:guid}/analytics")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetAnalytics(
        Guid publicId,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetFormAnalyticsQuery(publicId, ResolveTenantId()), cancellationToken));

    /// <summary>Compare fields between two template versions. Returns added/removed/modified fields.</summary>
    [HttpGet("{publicId:guid}/diff")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetVersionDiff(
        Guid publicId,
        [FromQuery] int fromVersion,
        [FromQuery] int toVersion,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetFormVersionDiffQuery(publicId, ResolveTenantId(), fromVersion, toVersion),
            cancellationToken));
}
