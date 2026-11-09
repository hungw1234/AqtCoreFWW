using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Forms.Application.Commands.CreateViewDefinition;
using AqtCoreFW.Forms.Application.Commands.DeleteViewDefinition;
using AqtCoreFW.Forms.Application.Commands.UpdateViewDefinition;
using AqtCoreFW.Forms.Application.Queries.GetViewByEntityType;
using AqtCoreFW.Forms.Application.Queries.GetViewDefinition;
using AqtCoreFW.Forms.Application.Queries.ListViewDefinitions;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Forms.Presentation.Controllers;

/// <summary>
/// Views metadata REST API â€” CRUD for ViewDefinitions.
/// GET /api/v1/views?entityType= returns default/first view for that entity type.
/// GET /api/v1/views (without entityType) returns paginated list.
/// Tenant ID and user identity resolved from JWT claims, not request body.
/// </summary>
[Route("api/v1/views")]
[Authorize]
public sealed class ViewsController(ISender mediator) : ApiControllerBase
{
    /// <summary>
    /// List views for tenant (paginated) or get default view for an entity type.
    /// Provide entityType to get the default view; omit for a paginated list.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> Get(
        [FromQuery] string? entityType = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(entityType))
            return ToApiResponse(await mediator.Send(
                new GetViewByEntityTypeQuery(entityType, ResolveTenantId()), cancellationToken));

        pageSize = Math.Clamp(pageSize, 1, 100);
        return ToApiResponse(await mediator.Send(
            new ListViewDefinitionsQuery(ResolveTenantId(), page, pageSize), cancellationToken));
    }

    /// <summary>Get a single ViewDefinition with columns and filters by ID.</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetById(
        long id,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new GetViewDefinitionQuery(id, ResolveTenantId()), cancellationToken));

    /// <summary>Create a new ViewDefinition (with optional initial columns and filters).</summary>
    [HttpPost]
    [Authorize(Policy = PermCodes.Forms.FormView.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateViewRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new CreateViewDefinitionCommand(
                ResolveTenantId(),
                request.Name,
                request.EntityType,
                request.Type,
                ResolveUserId(),
                request.DefaultSortField,
                request.DefaultSortDir,
                request.PageSize ?? 20,
                request.IsDefault ?? false,
                request.Columns?.Select(c => new CreateViewColumnDto(
                    c.FieldName, c.Label, c.DisplayOrder,
                    c.Sortable, c.Filterable, c.Width, c.Formatter)).ToList(),
                request.Filters?.Select(f => new CreateViewFilterDto(
                    f.FieldName, f.Operator, f.DefaultValue, f.IsRequired)).ToList()),
            cancellationToken));

    /// <summary>Update mutable view settings (name, sort, pageSize, isDefault).</summary>
    [HttpPut("{id:long}")]
    [Authorize(Policy = PermCodes.Forms.FormView.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Update(
        long id,
        [FromBody] UpdateViewRequest request,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new UpdateViewDefinitionCommand(
                id, ResolveTenantId(), ResolveUserId(),
                request.Name, request.DefaultSortField,
                request.DefaultSortDir, request.PageSize, request.IsDefault,
                request.Columns?.Select(c => new CreateViewColumnDto(
                    c.FieldName, c.Label, c.DisplayOrder,
                    c.Sortable, c.Filterable, c.Width, c.Formatter)).ToList()),
            cancellationToken));

    /// <summary>Soft-delete a ViewDefinition.</summary>
    [HttpDelete("{id:long}")]
    [Authorize(Policy = PermCodes.Forms.FormView.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> Delete(
        long id,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(
            new DeleteViewDefinitionCommand(id, ResolveTenantId()), cancellationToken));
}

// Request body records â€” no entity types (GOV_SEC_004 mass-assignment prevention)
// TenantId, CreatedBy, UpdatedBy removed â€” resolved from JWT via base class

public sealed record CreateViewRequest(
    string Name,
    string EntityType,
    ViewType Type,
    string? DefaultSortField = null,
    string? DefaultSortDir = null,
    int? PageSize = null,
    bool? IsDefault = null,
    List<CreateViewColumnRequest>? Columns = null,
    List<CreateViewFilterRequest>? Filters = null);

public sealed record CreateViewColumnRequest(
    string FieldName,
    string Label,
    int DisplayOrder,
    bool Sortable = false,
    bool Filterable = false,
    int? Width = null,
    ColumnFormatter Formatter = ColumnFormatter.Text);

public sealed record CreateViewFilterRequest(
    string FieldName,
    FilterOperator Operator,
    string? DefaultValue = null,
    bool IsRequired = false);

public sealed record UpdateViewRequest(
    string? Name = null,
    string? DefaultSortField = null,
    string? DefaultSortDir = null,
    int? PageSize = null,
    bool? IsDefault = null,
    List<CreateViewColumnRequest>? Columns = null);
