using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Presentation;
using AqtCoreFW.Integration.Application.Commands.CreatePartner;
using Microsoft.AspNetCore.RateLimiting;
using AqtCoreFW.Integration.Application.Commands.UpdatePartner;
using AqtCoreFW.Integration.Application.Commands.DeletePartner;
using AqtCoreFW.Integration.Application.Queries.GetPartner;
using AqtCoreFW.Integration.Application.Queries.ListPartners;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqtCoreFW.Integration.Presentation.Controllers;

/// <summary>
/// Integration Partner REST API — CRUD + lifecycle state transitions.
/// TenantId resolved from JWT claim (ITenantContext) — never caller-supplied.
/// T2 entity: route uses {publicId:guid} (PublicId AK — external-facing key).
/// </summary>
[Route("api/v1/integration/partners")]
[Authorize]
public sealed class PartnersController(ISender mediator, ITenantContext tenantContext)
    : ApiControllerBase
{
    /// <summary>List partners (paginated). pageSize clamped to [1, 100].</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> List(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var tenantId = tenantContext.TenantId ?? Guid.Empty;
        pageSize = Math.Clamp(pageSize, 1, 100);
        return ToApiResponse(await mediator.Send(
            new ListPartnersQuery(tenantId, search, page, pageSize), ct));
    }

    /// <summary>Get partner detail by PublicId (Guid, T2 AK).</summary>
    [HttpGet("{publicId:guid}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(Guid publicId, CancellationToken ct)
    {
        var tenantId = tenantContext.TenantId ?? Guid.Empty;
        return ToApiResponse(await mediator.Send(
            new GetPartnerQuery(publicId, tenantId), ct));
    }

    /// <summary>Create a new integration partner.</summary>
    [HttpPost]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreatePartnerBody body, CancellationToken ct)
    {
        var tenantId = tenantContext.TenantId ?? Guid.Empty;
        return ToApiResponse(await mediator.Send(
            new CreatePartnerCommand(tenantId, body.Name, body.Code,
                body.ContactEmail, body.ContactPhone,
                body.Endpoint, body.AuthScheme), ct));
    }

    /// <summary>Update partner mutable fields (PublicId Guid in route).</summary>
    [HttpPut("{publicId:guid}")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Update(
        Guid publicId, [FromBody] UpdatePartnerBody body, CancellationToken ct)
    {
        var tenantId = tenantContext.TenantId ?? Guid.Empty;
        return ToApiResponse(await mediator.Send(
            new UpdatePartnerCommand(publicId, tenantId, body.Name, body.Code,
                body.ContactEmail, body.ContactPhone,
                body.Endpoint, body.AuthScheme), ct));
    }

    /// <summary>Soft-delete a partner (PublicId Guid in route).</summary>
    [HttpDelete("{publicId:guid}")]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(Guid publicId, CancellationToken ct)
    {
        var tenantId = tenantContext.TenantId ?? Guid.Empty;
        return ToApiResponse(await mediator.Send(
            new DeletePartnerCommand(publicId, tenantId), ct));
    }
}

// Request body records — lightweight, no entity types (GOV_SEC_004 mass-assignment prevention)
public sealed record CreatePartnerBody(
    string Name, string Code, string? ContactEmail, string? ContactPhone,
    string? Endpoint, string? AuthScheme);

public sealed record UpdatePartnerBody(
    string Name, string Code, string? ContactEmail, string? ContactPhone,
    string? Endpoint, string? AuthScheme);
