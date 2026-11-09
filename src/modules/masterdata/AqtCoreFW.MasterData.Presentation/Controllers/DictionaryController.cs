using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.MasterData.Application.Commands.CreateDictionary;
using AqtCoreFW.MasterData.Application.Commands.CreateDictionaryItem;
using AqtCoreFW.MasterData.Application.Commands.PublishDictionary;
using AqtCoreFW.MasterData.Application.Commands.UpdateDictionary;
using AqtCoreFW.MasterData.Application.Queries.GetDictionaries;
using AqtCoreFW.MasterData.Application.Queries.GetDictionaryItems;
using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.MasterData.Presentation.Controllers;

/// <summary>
/// Dictionary (Danh má»¥c) CRUD and publish endpoints.
/// Requires Admin role for writes; any authenticated user can read.
/// </summary>
[ApiController]
[Route("api/v1/dictionaries")]
[Authorize]
public sealed class DictionaryController(ISender mediator) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> List(
        [FromQuery] DictionaryStatus? status = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        return ToApiResponse(await mediator.Send(
            new GetDictionariesQuery(ResolveTenantId(), status, search, page, pageSize), ct));
    }

    [HttpPost]
    [Authorize(Policy = PermCodes.Master.Dict.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(201)]
    [ProducesResponseType(409)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateDictionaryRequest request,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        var result = await mediator.Send(
            new CreateDictionaryCommand(
                request.Code, request.Name, request.NameVi,
                request.Description, tenantId,
                request.IsSystemDefined, ResolveUserId()), ct);

        if (result.IsSuccess)
            return CreatedAtAction(nameof(List), new { }, new { id = result.Value });

        return ToApiResponse(result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PermCodes.Master.Dict.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateDictionaryRequest request,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new UpdateDictionaryCommand(id, request.Name, request.NameVi, request.Description, ResolveUserId()), ct));

    [HttpPost("{id:guid}/publish")]
    [Authorize(Policy = PermCodes.Master.Dict.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Publish(
        Guid id,
        [FromBody] PublishDictionaryRequest request,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(
            new PublishDictionaryCommand(id, request.Notes, ResolveUserId()), ct));

    [HttpGet("{id:guid}/items")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> ListItems(
        Guid id,
        [FromQuery] bool activeOnly = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 500);
        return ToApiResponse(await mediator.Send(
            new GetDictionaryItemsQuery(id, activeOnly, page, pageSize), ct));
    }

    [HttpPost("{id:guid}/items")]
    [Authorize(Policy = PermCodes.Master.Dict.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(201)]
    [ProducesResponseType(404)]
    [ProducesResponseType(409)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> CreateItem(
        Guid id,
        [FromBody] CreateDictionaryItemRequest request,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new CreateDictionaryItemCommand(
                id, request.Code, request.Name, request.NameVi,
                request.ParentId, request.SortOrder, request.EffectiveFrom,
                request.EffectiveTo, request.Metadata, ResolveTenantId(), ResolveUserId()), ct);

        if (result.IsSuccess)
            return CreatedAtAction(nameof(ListItems), new { id }, new { itemId = result.Value });

        return ToApiResponse(result);
    }

}

// --- Request DTOs ---

public sealed record CreateDictionaryRequest(
    string Code, string Name, string NameVi,
    string? Description, bool IsSystemDefined = false);

public sealed record UpdateDictionaryRequest(
    string Name, string NameVi, string? Description);

public sealed record PublishDictionaryRequest(string? Notes);

public sealed record CreateDictionaryItemRequest(
    string Code, string Name, string NameVi,
    Guid? ParentId, int SortOrder,
    DateTime EffectiveFrom, DateTime? EffectiveTo,
    string? Metadata);
