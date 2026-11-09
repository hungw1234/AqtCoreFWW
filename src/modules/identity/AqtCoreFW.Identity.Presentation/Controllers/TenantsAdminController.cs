using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Application.Commands.ChangeTenantStatus;
using AqtCoreFW.Identity.Application.Commands.CreateTenant;
using AqtCoreFW.Identity.Application.Commands.UpdateTenant;
using AqtCoreFW.Identity.Application.Queries.GetTenantById;
using AqtCoreFW.Identity.Application.Queries.ListTenantsAdmin;
using AqtCoreFW.Identity.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// Admin endpoints for tenant lifecycle management.
/// SystemAdmin only â€” manages the canonical Tenant registry.
/// </summary>
[Route("api/v1/admin/tenants")]
[Authorize(Policy = PermCodes.Identity.Tenant.View)]
[RequiresMfa]
public sealed class TenantsAdminController(ISender mediator) : ApiControllerBase
{
    /// <summary>List all tenants with optional filters and pagination.</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] TenantStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        return ToApiResponse(await mediator.Send(
            new ListTenantsAdminQuery(search, status, page, pageSize), ct));
    }

    /// <summary>Get a single tenant by ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new GetTenantByIdQuery(id), ct));

    /// <summary>Create a new tenant.</summary>
    [HttpPost]
    [Authorize(Policy = PermCodes.Identity.Tenant.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(201)]
    [ProducesResponseType(409)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateTenantRequest request,
        CancellationToken ct = default)
    {
        var actorId = ResolveUserId();
        var result = await mediator.Send(
            new CreateTenantCommand(request.Name, request.Code, request.ContactEmail, actorId), ct);

        if (result.IsSuccess)
            return CreatedAtAction(nameof(GetById), new { id = result.Value }, new { id = result.Value });

        return ToApiResponse(result);
    }

    /// <summary>Update tenant (Name, ContactEmail). Code is immutable.</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermCodes.Identity.Tenant.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateTenantRequest request,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new UpdateTenantCommand(id, request.Name, request.ContactEmail, ResolveUserId()), ct));

    /// <summary>Change tenant status (Active, Suspended, Archived).</summary>
    [HttpPost("{id:guid}/status")]
    [Authorize(Policy = PermCodes.Identity.Tenant.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> ChangeStatus(
        Guid id,
        [FromBody] ChangeStatusRequest request,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new ChangeTenantStatusCommand(id, request.Status, ResolveUserId()), ct));

    /// <summary>Soft-delete tenant (sets Status = Archived). Hard delete forbidden.</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PermCodes.Identity.Tenant.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new ChangeTenantStatusCommand(id, TenantStatus.Archived, ResolveUserId()), ct));
}

/// <summary>Request body for tenant creation.</summary>
public sealed record CreateTenantRequest(
    string Name,
    string Code,
    string? ContactEmail);

/// <summary>Request body for tenant update (Code is immutable, not included).</summary>
public sealed record UpdateTenantRequest(
    string Name,
    string? ContactEmail);

/// <summary>Request body for status change.</summary>
public sealed record ChangeStatusRequest(TenantStatus Status);
