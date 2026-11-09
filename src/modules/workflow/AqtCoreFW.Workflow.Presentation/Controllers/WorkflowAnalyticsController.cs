using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Presentation;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Application.Queries.Analytics;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Workflow.Presentation.Controllers;

/// <summary>
/// Workflow analytics read endpoints â€” aggregate KPI data for dashboard charts.
/// All endpoints require WORKFLOW.ANALYTICS.VIEW permission.
/// TenantId always resolved from JWT; never accepted from request body.
/// Results cached 15 minutes per handler (ICacheService, keyed by tenantId + params).
/// </summary>
[Route("api/v1/workflow/analytics")]
[Authorize(Policy = PermCodes.Workflow.Analytics.View)]
[EnableRateLimiting("read-ops")]
public sealed class WorkflowAnalyticsController(ISender mediator) : ApiControllerBase
{
    /// <summary>Count of active instances per state for a definition, with % share.</summary>
    [HttpGet("state-distribution")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> StateDistribution(
        [FromQuery] Guid definitionId,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new GetStateDistributionQuery(definitionId, ResolveTenantId()), ct));

    /// <summary>Average and median minutes spent per state over a date range.</summary>
    [HttpGet("processing-time")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> ProcessingTime(
        [FromQuery] Guid definitionId,
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new GetAvgProcessingTimeByStateQuery(definitionId, ResolveTenantId(), from, to), ct));

    /// <summary>Transition count per user grouped by Day/Week/Month.</summary>
    [HttpGet("throughput")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> Throughput(
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        [FromQuery] GroupByPeriod groupBy = GroupByPeriod.Day,
        [FromQuery] Guid? definitionId = null,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new GetThroughputByUserQuery(ResolveTenantId(), from, to, groupBy, definitionId), ct));

    /// <summary>SLA on-time compliance % per period for completed instances with a DueAt.</summary>
    [HttpGet("sla-compliance")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> SlaCompliance(
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        [FromQuery] GroupByPeriod groupBy = GroupByPeriod.Month,
        [FromQuery] Guid? definitionId = null,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new GetSlaComplianceTrendQuery(ResolveTenantId(), from, to, groupBy, definitionId), ct));

    /// <summary>Active instances stuck in a state longer than minHours.</summary>
    [HttpGet("aging")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> Aging(
        [FromQuery] int minHours = 24,
        [FromQuery] Guid? definitionId = null,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new GetAgingReportQuery(ResolveTenantId(), minHours, definitionId), ct));

    /// <summary>States ranked by average dwell time (bottleneck detection).</summary>
    [HttpGet("bottlenecks")]
    [ProducesResponseType(200)]
    [ProducesResponseType(401)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> Bottlenecks(
        [FromQuery] Guid definitionId,
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new GetBottleneckAnalysisQuery(definitionId, ResolveTenantId(), from, to), ct));
}
