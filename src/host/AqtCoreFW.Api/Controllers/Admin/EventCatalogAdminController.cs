using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Infrastructure.Events;
using AqtCoreFW.SharedKernel.Api;
using AqtCoreFW.SharedKernel.Presentation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqtCoreFW.Api.Controllers.Admin;

/// <summary>
/// Admin endpoints for discovering registered domain events (M07 Event Catalog).
/// Read-only â€” catalog is populated at startup by module registrations.
/// Requires SystemAdmin role â€” internal framework visibility only.
/// </summary>
[Route("api/v1/admin/event-catalog")]
[Authorize(Policy = PermCodes.System.Config.View)]
[RequiresMfa]
public sealed class EventCatalogAdminController(IEventCatalogService catalog) : ApiControllerBase
{
    /// <summary>List all registered domain events ordered by module then name.</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public IActionResult List()
    {
        var entries = catalog.GetAll().Select(e => new EventCatalogDto(
            e.EventName,
            e.SourceModule,
            e.Description,
            e.EventType.AssemblyQualifiedName ?? e.EventType.FullName ?? e.EventName,
            e.SchemaVersion));

        return Ok(ApiResponse<object>.Ok(new { items = entries, totalCount = entries.Count() }));
    }

    /// <summary>Get details for a single registered event by its short class name.</summary>
    [HttpGet("{eventName}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public IActionResult GetByName(string eventName)
    {
        var entry = catalog.GetByName(eventName);
        if (entry is null)
            return NotFound();

        return Ok(ApiResponse<EventCatalogDto>.Ok(new EventCatalogDto(
            entry.EventName,
            entry.SourceModule,
            entry.Description,
            entry.EventType.AssemblyQualifiedName ?? entry.EventType.FullName ?? entry.EventName,
            entry.SchemaVersion)));
    }
}

/// <summary>Read-only projection of a catalog entry for the REST response.</summary>
public sealed record EventCatalogDto(
    string EventName,
    string SourceModule,
    string? Description,
    string AssemblyQualifiedName,
    string SchemaVersion);
