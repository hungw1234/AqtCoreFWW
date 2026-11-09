using System.Text;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Identity.Application.Commands.ManageSodRule;
using AqtCoreFW.Identity.Application.Queries.ExportSodViolationLogs;
using AqtCoreFW.Identity.Application.Queries.ListSodRules;
using AqtCoreFW.Identity.Application.Queries.ListSodViolationLogs;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Identity.Presentation.Controllers;

/// <summary>
/// Admin CRUD for Segregation of Duties conflict rules.
/// Defines permission-code pairs that must not coexist on the same user.
/// Requires Admin or SystemAdmin role.
/// V2: DELETE endpoint removed â€” use PUT with IsActive=false for soft-delete.
/// </summary>
[Route("api/v1/admin/sod-rules")]
[Authorize(Policy = PermCodes.Identity.Sod.View)]
[RequiresMfa]
[EnableRateLimiting("write-ops")]
public sealed class SodRulesAdminController(ISender mediator) : ApiControllerBase
{
    /// <summary>List all SoD rules for the resolved tenant.</summary>
    [HttpGet]
    [ProducesResponseType(200)]
    public async Task<IActionResult> List(CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new ListSodRulesQuery(ResolveTenantId()), ct));

    /// <summary>Create a new SoD conflict rule.</summary>
    [HttpPost]
    [Authorize(Policy = PermCodes.Identity.Sod.Manage)]
    [ProducesResponseType(201)]
    [ProducesResponseType(409)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateSodRuleRequest request,
        CancellationToken ct = default)
    {
        var severity = request.Severity ?? "Low";

        var result = await mediator.Send(new CreateSodRuleCommand(
            request.PermissionCodeA,
            request.PermissionCodeB,
            severity,
            request.Description,
            ResolveTenantId()), ct);

        if (result.IsSuccess)
            return StatusCode(201, new { id = result.Value });

        return ToApiResponse(result);
    }

    /// <summary>Update an existing SoD conflict rule.</summary>
    [HttpPut("{id:long}")]
    [Authorize(Policy = PermCodes.Identity.Sod.Manage)]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> Update(
        long id,
        [FromBody] UpdateSodRuleRequest request,
        CancellationToken ct = default)
    {
        var severity = request.Severity ?? "Low";

        return ToApiResponse(await mediator.Send(new UpdateSodRuleCommand(
            id,
            request.PermissionCodeA,
            request.PermissionCodeB,
            severity,
            request.Description,
            request.IsActive), ct));
    }

    /// <summary>List SoD violation audit logs with optional filters. Page size capped at 100.</summary>
    [HttpGet("violations")]
    [Authorize(Policy = PermCodes.Identity.Sod.ViewAuditLog)]
    [ProducesResponseType(200)]
    public async Task<IActionResult> ListViolations(
        [FromQuery] ListSodViolationLogsRequest request,
        CancellationToken ct = default) =>
        ToApiResponse(await mediator.Send(new ListSodViolationLogsQuery(
            ResolveTenantId(),
            request.UserId,
            request.RoleId,
            request.RuleId,
            request.From,
            request.To,
            request.Page ?? 1,
            request.PageSize ?? 50), ct));

    /// <summary>
    /// Export SoD violation audit logs as streaming CSV.
    /// Requires separate ExportAuditLog permission (not ViewAuditLog).
    /// Unbounded streaming with deterministic order (DetectedAt DESC, Id).
    /// </summary>
    [HttpGet("violations/export")]
    [Authorize(Policy = PermCodes.Identity.Sod.ExportAuditLog)]
    [DisableRateLimiting]
    public async Task ExportViolations(
        [FromQuery] ListSodViolationLogsRequest request,
        CancellationToken ct = default)
    {
        Response.ContentType = "text/csv; charset=utf-8";
        Response.Headers.ContentDisposition = $"attachment; filename=\"sod-violations-{DateTime.UtcNow:yyyyMMddHHmm}.csv\"";

        // Write UTF-8 BOM for Excel Vietnamese char compat
        await Response.Body.WriteAsync(Encoding.UTF8.GetPreamble(), ct);

        await using var writer = new StreamWriter(Response.Body, Encoding.UTF8, leaveOpen: true);
        await mediator.Send(new ExportSodViolationLogsQuery(
            ResolveTenantId(),
            request.UserId,
            request.RoleId,
            request.RuleId,
            request.From,
            request.To,
            writer), ct);
    }
}

/// <summary>Create SoD rule request DTO.</summary>
public sealed class CreateSodRuleRequest
{
    public required string PermissionCodeA { get; init; }
    public required string PermissionCodeB { get; init; }
    public string? Description { get; init; }

    /// <summary>Severity level â€” Low, Medium, or High.</summary>
    public string? Severity { get; init; }
}

/// <summary>Update SoD rule request DTO.</summary>
public sealed class UpdateSodRuleRequest
{
    public required string PermissionCodeA { get; init; }
    public required string PermissionCodeB { get; init; }
    public string? Description { get; init; }
    public bool IsActive { get; init; }

    /// <summary>Severity level â€” Low, Medium, or High.</summary>
    public string? Severity { get; init; }
}

/// <summary>Request DTO for listing SoD violation logs.</summary>
public sealed class ListSodViolationLogsRequest
{
    public Guid? UserId { get; init; }
    public Guid? RoleId { get; init; }
    public long? RuleId { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public int? Page { get; init; }
    public int? PageSize { get; init; }
}
