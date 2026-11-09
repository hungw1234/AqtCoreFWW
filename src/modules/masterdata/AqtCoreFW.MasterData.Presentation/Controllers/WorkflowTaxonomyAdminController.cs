using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.MasterData.Application.Commands.WorkflowTaxonomy;
using AqtCoreFW.MasterData.Application.DTOs;
using AqtCoreFW.MasterData.Application.Queries.WorkflowTaxonomy;
using AqtCoreFW.MasterData.Domain.Enums;
using AqtCoreFW.SharedKernel.Api;
using AqtCoreFW.SharedKernel.Application.Pagination;
using AqtCoreFW.SharedKernel.Presentation;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.MasterData.Presentation.Controllers;

/// <summary>
/// Phase-00 workflow taxonomy admin endpoints (10 total).
/// Read: MASTER.WORKFLOW_TAXONOMY.VIEW â€” any authenticated admin.
/// Write: PLATFORM.WORKFLOW_TAXONOMY.MANAGE â€” SystemAdmin only (global cross-tenant scope).
/// Writes are rate-limited (write-ops policy) + audit-logged via AuditBehavior pipeline.
/// Note (plan D7/R6): reactivation requires explicit POST /{id}/restore. PUT body cannot flip
/// IsActive because UpdateCommand has no such field â€” by construction, PUT never reactivates.
/// </summary>
[ApiController]
[Route("api/v1/admin/master-data/workflow-taxonomy")]
[RequiresMfa]
public class WorkflowTaxonomyAdminController(ISender mediator) : ApiControllerBase
{
    // â”€â”€ Workflow State Taxonomy â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [HttpGet("states")]
    [Authorize(Policy = PermCodes.Master.WorkflowTaxonomy.View)]
    public Task<IActionResult> ListStates(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 200,
        [FromQuery] bool includeInactive = false,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
        => SendPaged(new GetWorkflowStateTaxonomiesQuery(page, pageSize, includeInactive, search), ct);

    [HttpGet("states/{id:guid}")]
    [Authorize(Policy = PermCodes.Master.WorkflowTaxonomy.View)]
    public async Task<IActionResult> GetStateById(Guid id, CancellationToken ct)
        => ToApiResponse(await mediator.Send(new GetWorkflowStateTaxonomyByIdQuery(id), ct));

    [HttpPost("states")]
    [Authorize(Policy = PermCodes.Platform.WorkflowTaxonomy.Manage)]
    [EnableRateLimiting("write-ops")]
    public async Task<IActionResult> CreateState(
        [FromBody] CreateWorkflowStateTaxonomyRequest req,
        CancellationToken ct)
    {
        var cmd = new CreateWorkflowStateTaxonomyCommand(
            req.Code, req.NameVi, req.NameEn, req.Category,
            req.SortOrder, req.I18nKey, req.Icon, req.Color);
        var result = await mediator.Send(cmd, ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetStateById), new { id = result.Value },
                ApiResponse<object>.Ok(new { id = result.Value }))
            : ToApiResponse(result);
    }

    [HttpPut("states/{id:guid}")]
    [Authorize(Policy = PermCodes.Platform.WorkflowTaxonomy.Manage)]
    [EnableRateLimiting("write-ops")]
    public async Task<IActionResult> UpdateState(
        Guid id,
        [FromBody] UpdateWorkflowStateTaxonomyRequest req,
        CancellationToken ct)
    {
        var cmd = new UpdateWorkflowStateTaxonomyCommand(
            id, req.NameVi, req.NameEn, req.I18nKey, req.Icon, req.Color,
            req.Category, req.SortOrder, req.RowVersion);
        return ToApiResponse(await mediator.Send(cmd, ct));
    }

    [HttpDelete("states/{id:guid}")]
    [Authorize(Policy = PermCodes.Platform.WorkflowTaxonomy.Manage)]
    [EnableRateLimiting("write-ops")]
    public async Task<IActionResult> DeleteState(
        Guid id,
        [FromBody] DeleteTaxonomyRequest req,
        CancellationToken ct)
    {
        var cmd = new DeleteWorkflowStateTaxonomyCommand(id, req.ConfirmCode, req.Reason);
        var result = await mediator.Send(cmd, ct);
        return result.IsSuccess
            ? Ok(ApiResponse<object>.Ok(new { referencedByDefinitionCount = 0 }))
            : ToApiResponse(result);
    }

    [HttpPost("states/{id:guid}/restore")]
    [Authorize(Policy = PermCodes.Platform.WorkflowTaxonomy.Manage)]
    [EnableRateLimiting("write-ops")]
    public async Task<IActionResult> RestoreState(Guid id, CancellationToken ct)
        => ToApiResponse(await mediator.Send(new RestoreWorkflowStateTaxonomyCommand(id), ct));

    // â”€â”€ Workflow Action Taxonomy â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [HttpGet("actions")]
    [Authorize(Policy = PermCodes.Master.WorkflowTaxonomy.View)]
    public Task<IActionResult> ListActions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 200,
        [FromQuery] bool includeInactive = false,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
        => SendPaged(new GetWorkflowActionTaxonomiesQuery(page, pageSize, includeInactive, search), ct);

    [HttpGet("actions/{id:guid}")]
    [Authorize(Policy = PermCodes.Master.WorkflowTaxonomy.View)]
    public async Task<IActionResult> GetActionById(Guid id, CancellationToken ct)
        => ToApiResponse(await mediator.Send(new GetWorkflowActionTaxonomyByIdQuery(id), ct));

    [HttpPost("actions")]
    [Authorize(Policy = PermCodes.Platform.WorkflowTaxonomy.Manage)]
    [EnableRateLimiting("write-ops")]
    public async Task<IActionResult> CreateAction(
        [FromBody] CreateWorkflowActionTaxonomyRequest req,
        CancellationToken ct)
    {
        var cmd = new CreateWorkflowActionTaxonomyCommand(
            req.Code, req.NameVi, req.NameEn, req.SortOrder, req.I18nKey, req.Icon);
        var result = await mediator.Send(cmd, ct);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetActionById), new { id = result.Value },
                ApiResponse<object>.Ok(new { id = result.Value }))
            : ToApiResponse(result);
    }

    [HttpPut("actions/{id:guid}")]
    [Authorize(Policy = PermCodes.Platform.WorkflowTaxonomy.Manage)]
    [EnableRateLimiting("write-ops")]
    public async Task<IActionResult> UpdateAction(
        Guid id,
        [FromBody] UpdateWorkflowActionTaxonomyRequest req,
        CancellationToken ct)
    {
        var cmd = new UpdateWorkflowActionTaxonomyCommand(
            id, req.NameVi, req.NameEn, req.I18nKey, req.Icon,
            req.SortOrder, req.RowVersion);
        return ToApiResponse(await mediator.Send(cmd, ct));
    }

    [HttpDelete("actions/{id:guid}")]
    [Authorize(Policy = PermCodes.Platform.WorkflowTaxonomy.Manage)]
    [EnableRateLimiting("write-ops")]
    public async Task<IActionResult> DeleteAction(
        Guid id,
        [FromBody] DeleteTaxonomyRequest req,
        CancellationToken ct)
    {
        var cmd = new DeleteWorkflowActionTaxonomyCommand(id, req.ConfirmCode, req.Reason);
        var result = await mediator.Send(cmd, ct);
        return result.IsSuccess
            ? Ok(ApiResponse<object>.Ok(new { referencedByDefinitionCount = 0 }))
            : ToApiResponse(result);
    }

    [HttpPost("actions/{id:guid}/restore")]
    [Authorize(Policy = PermCodes.Platform.WorkflowTaxonomy.Manage)]
    [EnableRateLimiting("write-ops")]
    public async Task<IActionResult> RestoreAction(Guid id, CancellationToken ct)
        => ToApiResponse(await mediator.Send(new RestoreWorkflowActionTaxonomyCommand(id), ct));

    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private async Task<IActionResult> SendPaged<T>(
        IRequest<Result<PagedResult<T>>> query, CancellationToken ct)
    {
        var result = await mediator.Send(query, ct);
        if (!result.IsSuccess) return ToApiResponse(result);
        var paged = result.Value;
        return Ok(ApiResponse<IReadOnlyList<T>>.Ok(paged.Items, paged.Meta));
    }
}

// â”€â”€ Request DTOs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

public sealed record CreateWorkflowStateTaxonomyRequest(
    string Code,
    string NameVi,
    string NameEn,
    WorkflowStateCategory Category,
    int SortOrder = 0,
    string? I18nKey = null,
    string? Icon = null,
    string? Color = null);

public sealed record UpdateWorkflowStateTaxonomyRequest(
    string NameVi,
    string NameEn,
    string? I18nKey,
    string? Icon,
    string? Color,
    WorkflowStateCategory Category,
    int SortOrder,
    byte[] RowVersion);

public sealed record CreateWorkflowActionTaxonomyRequest(
    string Code,
    string NameVi,
    string NameEn,
    int SortOrder = 0,
    string? I18nKey = null,
    string? Icon = null);

public sealed record UpdateWorkflowActionTaxonomyRequest(
    string NameVi,
    string NameEn,
    string? I18nKey,
    string? Icon,
    int SortOrder,
    byte[] RowVersion);

/// <summary>
/// DELETE body â€” confirmCode matched case-sensitive (plan R7); reason min 10 chars.
/// Reused for both state and action endpoints.
/// </summary>
public sealed record DeleteTaxonomyRequest(string ConfirmCode, string Reason);
