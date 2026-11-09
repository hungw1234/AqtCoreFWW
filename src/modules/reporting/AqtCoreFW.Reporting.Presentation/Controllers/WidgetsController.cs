using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Reporting.Application.Commands.CreateWidget;
using AqtCoreFW.Reporting.Application.Commands.UpdateWidget;
using AqtCoreFW.Reporting.Application.Queries.GetWidgetData;
using AqtCoreFW.Reporting.Domain.Enums;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Reporting.Presentation.Controllers;

/// <summary>
/// Report widget REST API â€” create, fetch data, and update dashboard widgets.
/// TenantId resolved server-side from ITenantContext â€” never trusted from request body.
/// </summary>
[Route("api/v1/widgets")]
[Authorize]
public sealed class WidgetsController(
    ISender mediator,
    ITenantContext tenantContext) : ApiControllerBase
{
    /// <summary>Create a new report widget (Admin/SystemAdmin only â€” widgets contain raw SQL).</summary>
    [HttpPost]
    [Authorize(Policy = PermCodes.Report.Widget.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> CreateWidget(
        [FromBody] CreateWidgetRequest request,
        CancellationToken ct)
    {
        var tenantId = tenantContext.TenantId ?? Guid.Empty;
        var createdBy = tenantContext.TenantId ?? Guid.Empty;

        var command = new CreateWidgetCommand(
            request.Name,
            request.Type,
            request.DataQuery,
            tenantId,
            createdBy,
            request.Config,
            request.RefreshSeconds ?? 300);

        return ToApiResponse(await mediator.Send(command, ct));
    }

    /// <summary>Execute a widget's DataQuery and return chart-ready data. T1 entity: id is long internal PK.</summary>
    [HttpGet("{id:long}/data")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(403)]
    public async Task<IActionResult> GetWidgetData(long id, CancellationToken ct)
    {
        var tenantId = tenantContext.TenantId ?? Guid.Empty;
        return ToApiResponse(await mediator.Send(new GetWidgetDataQuery(id, tenantId), ct));
    }

    /// <summary>Update an existing widget (Admin/SystemAdmin only). T1 entity: id is long internal PK.</summary>
    [HttpPut("{id:long}")]
    [Authorize(Policy = PermCodes.Report.Widget.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> UpdateWidget(
        long id,
        [FromBody] UpdateWidgetRequest request,
        CancellationToken ct) =>
        ToApiResponse(await mediator.Send(
            new UpdateWidgetCommand(id, request.Name, request.DataQuery, request.Config, request.RefreshSeconds), ct));
}

// Request body records â€” mass-assignment prevention (GOV_SEC_004)
public sealed record CreateWidgetRequest(
    string Name,
    WidgetType Type,
    string DataQuery,
    string? Config = null,
    int? RefreshSeconds = null);

public sealed record UpdateWidgetRequest(
    string? Name,
    string? DataQuery,
    string? Config,
    int? RefreshSeconds);
