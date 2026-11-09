using AqtCoreFW.ModuleName.Application.Commands.CreateModuleName;
using AqtCoreFW.ModuleName.Application.DTOs;
using AqtCoreFW.ModuleName.Application.Queries.GetModuleName;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqtCoreFW.ModuleName.Presentation.Controllers;

/// <summary>
/// ModuleName REST API — authenticated CRUD.
/// Inherits ApiControllerBase for consistent ApiResponse{T} envelope.
/// TenantId resolved from ITenantContext (JWT claim).
/// </summary>
[Route("api/v1/modulename")]
[Authorize]
public sealed class ModuleNameController(
    ISender sender,
    ITenantContext tenantContext,
    ICurrentUser currentUser) : ApiControllerBase
{
    [HttpGet("{id:long}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(long id, CancellationToken ct)
    {
        if (tenantContext.TenantId is not Guid tenantId)
            return Unauthorized();
        return ToApiResponse(await sender.Send(new GetModuleNameQuery(id, tenantId), ct));
    }

    [HttpPost]
    [ProducesResponseType(200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateModuleNameRequest request,
        CancellationToken ct)
    {
        if (tenantContext.TenantId is not Guid tenantId ||
            !currentUser.IsAuthenticated ||
            currentUser.UserId == Guid.Empty)
            return Unauthorized();

        var command = new CreateModuleNameCommand(
            tenantId, request.Title, request.Description, currentUser.UserId);
        return ToApiResponse(await sender.Send(command, ct));
    }
}

public sealed record CreateModuleNameRequest(string Title, string? Description);
