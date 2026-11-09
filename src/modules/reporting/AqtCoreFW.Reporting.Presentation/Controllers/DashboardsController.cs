using AqtCoreFW.Reporting.Application.Commands.CreateDashboard;
using AqtCoreFW.Reporting.Application.Commands.UpdateDashboard;
using AqtCoreFW.Reporting.Application.Queries.GetDashboard;
using AqtCoreFW.Reporting.Application.Queries.ListDashboards;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Reporting.Presentation.Controllers;

/// <summary>
/// Dashboard layout REST API — CRUD for user dashboard configurations.
/// TenantId and OwnerId resolved server-side from ITenantContext (JWT claims).
/// </summary>
[Route("api/v1/dashboards")]
[Authorize]
public sealed class DashboardsController(
    ISender mediator,
    ITenantContext tenantContext) : ApiControllerBase
{
    /// <summary>Create a new dashboard layout for the current user.</summary>
    [HttpPost]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> CreateDashboard(
        [FromBody] CreateDashboardRequest request,
        CancellationToken ct)
    {
        var ownerId = tenantContext.TenantId ?? Guid.Empty;
        var tenantId = tenantContext.TenantId ?? Guid.Empty;

        var command = new CreateDashboardCommand(
            request.Name,
            ownerId,
            tenantId,
            request.IsDefault ?? false);

        return ToApiResponse(await mediator.Send(command, ct));
    }

    /// <summary>Get a single dashboard layout by ID. T1 entity: id is long internal PK.</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetDashboard(long id, CancellationToken ct) =>
        ToApiResponse(await mediator.Send(new GetDashboardQuery(id), ct));

    /// <summary>List all dashboards owned by the current user in their tenant.</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> ListDashboards(CancellationToken ct)
    {
        var ownerId = tenantContext.TenantId ?? Guid.Empty;
        var tenantId = tenantContext.TenantId ?? Guid.Empty;
        return ToApiResponse(await mediator.Send(new ListDashboardsQuery(ownerId, tenantId), ct));
    }

    /// <summary>Update an existing dashboard layout. T1 entity: id is long internal PK.</summary>
    [HttpPut("{id:long}")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> UpdateDashboard(
        long id,
        [FromBody] UpdateDashboardRequest request,
        CancellationToken ct) =>
        ToApiResponse(await mediator.Send(
            new UpdateDashboardCommand(id, request.Name, request.WidgetPositions, request.IsDefault), ct));
}

// Request body records — mass-assignment prevention (GOV_SEC_004)
public sealed record CreateDashboardRequest(string Name, bool? IsDefault = false);

public sealed record UpdateDashboardRequest(
    string? Name,
    string? WidgetPositions,
    bool? IsDefault);
