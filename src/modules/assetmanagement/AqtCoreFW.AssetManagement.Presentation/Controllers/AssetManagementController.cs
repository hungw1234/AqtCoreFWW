using AqtCoreFW.AssetManagement.Application.Commands.CreateAssetManagement;
using AqtCoreFW.AssetManagement.Application.Commands.DeleteAssetManagement;
using AqtCoreFW.AssetManagement.Application.Commands.UpdateAssetManagement;
using AqtCoreFW.AssetManagement.Application.DTOs;
using AqtCoreFW.AssetManagement.Application.Queries.GetAssetManagement;
using AqtCoreFW.AssetManagement.Application.Queries.ListAssetManagement;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqtCoreFW.AssetManagement.Presentation.Controllers;

/// <summary>
/// AssetManagement REST API — authenticated CRUD.
/// Inherits ApiControllerBase for consistent ApiResponse{T} envelope.
/// TenantId resolved from ITenantContext (JWT claim).
/// </summary>
[Route("api/v1/asset-management")]
[Authorize]
public sealed class AssetManagementController(
    ISender sender,
    ITenantContext tenantContext,
    ICurrentUser currentUser) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (tenantContext.TenantId is not Guid tenantId)
            return Unauthorized();

        return ToApiResponse(await sender.Send(new ListAssetManagementQuery(tenantId), ct));
    }

    [HttpGet("{id:long}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(long id, CancellationToken ct)
    {
        if (tenantContext.TenantId is not Guid tenantId)
            return Unauthorized();

        return ToApiResponse(await sender.Send(new GetAssetManagementQuery(id, tenantId), ct));
    }

    [HttpPost]
    [ProducesResponseType(200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateAssetManagementRequest request,
        CancellationToken ct)
    {
        if (tenantContext.TenantId is not Guid tenantId ||
            !currentUser.IsAuthenticated ||
            currentUser.UserId == Guid.Empty)
            return Unauthorized();

        var command = new CreateAssetManagementCommand(
            tenantId, request.Title, request.Description, currentUser.UserId);
        return ToApiResponse(await sender.Send(command, ct));
    }

    [HttpPut("{id:long}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Update(
        long id,
        [FromBody] UpdateAssetManagementRequest request,
        CancellationToken ct)
    {
        if (tenantContext.TenantId is not Guid tenantId ||
            !currentUser.IsAuthenticated ||
            currentUser.UserId == Guid.Empty)
            return Unauthorized();

        var command = new UpdateAssetManagementCommand(
            id, tenantId, request.Title, request.Description, currentUser.UserId);
        return ToApiResponse(await sender.Send(command, ct));
    }

    [HttpDelete("{id:long}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        if (tenantContext.TenantId is not Guid tenantId)
            return Unauthorized();

        return ToApiResponse(await sender.Send(new DeleteAssetManagementCommand(id, tenantId), ct));
    }
}

public sealed record CreateAssetManagementRequest(string Title, string? Description);
public sealed record UpdateAssetManagementRequest(string Title, string? Description);
