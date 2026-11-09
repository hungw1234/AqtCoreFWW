using AqtCoreFW.Audit.Application.Commands.CreateRtbfRequest;
using AqtCoreFW.Audit.Application.Commands.ProcessRtbfRequest;
using AqtCoreFW.Audit.Application.Commands.RejectRtbfRequest;
using AqtCoreFW.Audit.Application.Queries.GetRtbfRequests;
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
/// Right-to-be-Forgotten (RTBF) request management â€” Law 91/2025 Art. 9 compliance.
/// Restricted to SystemAdmin only â€” RTBF operations involve PII erasure and compliance actions.
/// Tenant isolation: SystemAdmin may query cross-tenant (null tenantId) or filter by specific tenant.
/// </summary>
[Route("api/v1/admin/rtbf-requests")]
[Authorize(Policy = PermCodes.Audit.Rtbf.View)]
[RequiresMfa]
[EnableRateLimiting("write-ops")]
public sealed class RtbfController(ISender mediator, ITenantContext tenantContext) : ApiControllerBase
{
    /// <summary>List RTBF requests with optional status filter.</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? tenantId,
        [FromQuery] RtbfStatus? status,
        [FromQuery][StringLength(200)] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        // SystemAdmin may pass explicit tenantId to filter, or null for cross-tenant view.
        // Non-SystemAdmin cannot reach this endpoint (role gate above), but guard defensively.
        return ToApiResponse(await mediator.Send(
            new GetRtbfRequestsQuery(ResolveTenantId(tenantId), status, search, page, pageSize),
            cancellationToken));
    }

    /// <summary>Create a new RTBF anonymization request (PDPL Art. 17).</summary>
    [HttpPost]
    [Authorize(Policy = PermCodes.Audit.Rtbf.Execute)]
    [ProducesResponseType(201)]
    [ProducesResponseType(400)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateRtbfBody body,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new CreateRtbfRequestCommand(
                body.SubjectEmail,
                body.Reason ?? "RTBF request",
                GetActorId(),
                tenantContext.TenantId ?? Guid.Empty),
            cancellationToken));

    /// <summary>Mark RTBF request as processed (PII anonymization completed).</summary>
    [HttpPost("{id:guid}/process")]
    [Authorize(Policy = PermCodes.Audit.Rtbf.Execute)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Process(
        Guid id,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new ProcessRtbfRequestCommand(id, GetActorId()),
            cancellationToken));

    /// <summary>Reject RTBF request with reason.</summary>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = PermCodes.Audit.Rtbf.Execute)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Reject(
        Guid id,
        [FromBody] RejectRtbfBody body,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new RejectRtbfRequestCommand(id, GetActorId(), body.Reason),
            cancellationToken));

    /// <summary>
    /// Resolve effective tenantId: non-SystemAdmin users are always scoped to their JWT tenant.
    /// SystemAdmin with null tenantId param â†’ cross-tenant query (system-wide RTBF view).
    /// </summary>
    private Guid? ResolveTenantId(Guid? requestedTenantId) =>
        tenantContext.IsSystemAdmin ? requestedTenantId : tenantContext.TenantId;

    private Guid GetActorId()
    {
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}

public sealed record CreateRtbfBody(string SubjectEmail, string? Reason = null, string? RequestedAt = null);
// ProcessedBy removed â€” resolved server-side from JWT via GetActorId() (prevents actor spoofing)
public sealed record RejectRtbfBody(string Reason);
