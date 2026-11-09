using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.MasterData.Application.Commands.CreateExternalMapping;
using AqtCoreFW.MasterData.Application.Queries.GetExternalMappings;
using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.MasterData.Presentation.Controllers;

/// <summary>
/// External mapping CRUD endpoints â€” maps internal codes to external system codes.
/// Requires Admin role for writes; any authenticated user can read.
/// </summary>
[ApiController]
[Route("api/v1/external-mappings")]
[Authorize]
public sealed class ExternalMappingController(ISender mediator) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> List(
        [FromQuery] string? externalSystem = null,
        [FromQuery] string? internalCode = null,
        [FromQuery] bool activeOnly = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        return ToApiResponse(await mediator.Send(
            new GetExternalMappingsQuery(ResolveTenantId(), externalSystem, internalCode, activeOnly, page, pageSize), ct));
    }

    [HttpPost]
    [Authorize(Policy = PermCodes.Master.ExtMap.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(201)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateExternalMappingRequest request,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new CreateExternalMappingCommand(
                request.InternalCode, request.ExternalSystem, request.ExternalCode,
                request.Direction, request.DictionaryId,
                request.ValidFrom, request.ValidTo,
                ResolveTenantId(), ResolveUserId()), ct);

        if (result.IsSuccess)
            return CreatedAtAction(nameof(List), new { }, new { id = result.Value });

        return ToApiResponse(result);
    }

}

// --- Request DTOs ---

public sealed record CreateExternalMappingRequest(
    string InternalCode,
    string ExternalSystem,
    string ExternalCode,
    MappingDirection Direction,
    Guid? DictionaryId,
    DateTime ValidFrom,
    DateTime? ValidTo);
