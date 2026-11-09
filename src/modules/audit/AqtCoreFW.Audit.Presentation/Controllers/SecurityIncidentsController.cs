using AqtCoreFW.Audit.Application.Commands.ReportSecurityIncident;
using AqtCoreFW.Audit.Application.Commands.UpdateIncidentStatus;
using AqtCoreFW.Audit.Application.Queries.GetSecurityIncidents;
using AqtCoreFW.Audit.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Audit.Presentation.Controllers;

/// <summary>
/// Security incident management â€” QÄ742 mandated tracking.
/// Tenant isolation: non-SystemAdmin users are always scoped to their own tenantId from JWT.
/// </summary>
[Route("api/v1/admin/incidents")]
[Authorize(Policy = PermCodes.Audit.Incident.View)]
[RequiresMfa]
[EnableRateLimiting("write-ops")]
public sealed class SecurityIncidentsController(ISender mediator, ITenantContext tenantContext) : ApiControllerBase
{
    /// <summary>List incidents with optional filters.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? tenantId,
        [FromQuery] AuditSeverity? severity,
        [FromQuery] IncidentStatus? status,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery][StringLength(200)] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        return ToApiResponse(await mediator.Send(
            new GetSecurityIncidentsQuery(ResolveTenantId(tenantId), severity, status, from, to, search, page, pageSize),
            cancellationToken));
    }

    /// <summary>Report a new security incident.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(Guid), 200)]
    public async Task<IActionResult> Report(
        [FromBody] ReportSecurityIncidentCommand command,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(command, cancellationToken));

    /// <summary>Get incident by ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(
        Guid id,
        [FromQuery] Guid? tenantId,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetSecurityIncidentsQuery(ResolveTenantId(tenantId), null, null, null, null, null, 1, 1),
            cancellationToken));

    /// <summary>Update incident status (Investigating / Resolved / Closed).</summary>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        [FromBody] UpdateStatusRequest body,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new UpdateIncidentStatusCommand(id, body.Status, body.MitigationNote),
            cancellationToken));

    /// <summary>
    /// Resolve effective tenantId: non-SystemAdmin users are always scoped to their JWT tenant.
    /// SystemAdmin with null tenantId param â†’ cross-tenant query (system-wide auditing).
    /// </summary>
    private Guid? ResolveTenantId(Guid? requestedTenantId) =>
        tenantContext.IsSystemAdmin ? requestedTenantId : tenantContext.TenantId;
}

public sealed record UpdateStatusRequest(IncidentStatus Status, string? MitigationNote);
