using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Reporting.Application.Abstractions;
using AqtCoreFW.Reporting.Application.Commands.CreateReportDefinition;
using AqtCoreFW.Reporting.Application.Commands.GenerateCompliancePdf;
using AqtCoreFW.Reporting.Application.Commands.RunReport;
using AqtCoreFW.Reporting.Application.Commands.UpdateReportDefinition;
using AqtCoreFW.Reporting.Application.Queries.GetReportDefinitions;
using AqtCoreFW.Reporting.Application.Queries.GetReportExecution;
using AqtCoreFW.Reporting.Domain.Enums;
using AqtCoreFW.Reporting.Domain.Repositories;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Reporting.Presentation.Controllers;

/// <summary>
/// Reports REST API â€” dashboard KPIs, report definitions CRUD, execution polling, compliance PDF.
/// TenantId resolved from ITenantContext (JWT claim); SystemAdmin may override via query param.
/// </summary>
[Route("api/v1/reports")]
[Authorize]
public sealed class ReportsController(
    ISender mediator,
    ITenantContext tenantContext,
    IReportStorageService storageService,
    IReportExecutionRepository executionRepo) : ApiControllerBase
{
    /// <summary>List report definitions (system-wide + tenant-specific). Admin/GovOfficer only.</summary>
    [HttpGet("definitions")]
    [Authorize(Policy = PermCodes.Report.ReportRes.View)]
    [ProducesResponseType(200)]
    public async Task<IActionResult> ListDefinitions(CancellationToken ct)
    {
        var tenantId = tenantContext.TenantId?.ToString();
        return ToApiResponse(await mediator.Send(new GetReportDefinitionsQuery(tenantId), ct));
    }

    /// <summary>Create a new report definition (Admin only).</summary>
    [HttpPost("definitions")]
    [Authorize(Policy = PermCodes.Report.ReportRes.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> CreateDefinition(
        [FromBody] CreateReportDefinitionCommand command,
        CancellationToken ct) =>
        ToApiResponse(await mediator.Send(command, ct));

    /// <summary>Update an existing report definition (Admin only).</summary>
    [HttpPut("definitions/{id:guid}")]
    [Authorize(Policy = PermCodes.Report.ReportRes.Manage)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    [ProducesResponseType(422)]
    public async Task<IActionResult> UpdateDefinition(
        Guid id,
        [FromBody] UpdateReportDefinitionRequest request,
        CancellationToken ct) =>
        ToApiResponse(await mediator.Send(new UpdateReportDefinitionCommand(
            id, request.Name, request.NameVi, request.Description,
            request.SqlTemplate, request.ParametersSchema, request.OutputFormat), ct));

    /// <summary>Execute a report â€” returns executionId immediately (202 Accepted). Max 3 concurrent per tenant.</summary>
    [HttpPost("run")]
    [Authorize(Policy = PermCodes.Report.ReportRes.View)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(202)]
    [ProducesResponseType(404)]
    [ProducesResponseType(429)]
    public async Task<IActionResult> RunReport(
        [FromBody] RunReportRequest request,
        CancellationToken ct)
    {
        var tenantId = tenantContext.TenantId?.ToString() ?? string.Empty;
        var requestedBy = tenantContext.TenantId ?? Guid.Empty;

        // Rate limit: max 3 concurrent pending/running executions per tenant
        var pendingCount = await executionRepo.CountPendingAsync(tenantId, ct);
        if (pendingCount >= 3)
            return StatusCode(429, new { error = "Too many concurrent report executions. Max 3 allowed." });

        var command = new RunReportCommand(
            request.ReportDefinitionId,
            request.ParametersJson,
            tenantId,
            requestedBy,
            request.FormatOverride);
        var result = await mediator.Send(command, ct);
        if (!result.IsSuccess) return ToApiResponse(result);
        return AcceptedAtAction(nameof(GetExecution), new { id = result.Value }, result.Value);
    }

    /// <summary>Poll execution status (tenant-isolated).</summary>
    [HttpGet("executions/{id:guid}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetExecution(Guid id, CancellationToken ct)
    {
        // SystemAdmin sees all; regular users only see own executions
        var requestedBy = tenantContext.IsSystemAdmin ? null : tenantContext.TenantId;
        return ToApiResponse(await mediator.Send(new GetReportExecutionQuery(id, requestedBy), ct));
    }

    /// <summary>Download completed report file (tenant-isolated, path-traversal protected).</summary>
    [HttpGet("executions/{id:guid}/download")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DownloadExecution(Guid id, CancellationToken ct)
    {
        // Tenant isolation check via query handler first
        var requestedBy = tenantContext.IsSystemAdmin ? null : tenantContext.TenantId;
        var checkResult = await mediator.Send(new GetReportExecutionQuery(id, requestedBy), ct);
        if (!checkResult.IsSuccess)
            return ToApiResponse(checkResult);

        // Fetch execution entity directly (ResultFilePath not exposed via DTO)
        // T2: id from route is the PublicId (Guid)
        var execution = await executionRepo.GetByPublicIdAsync(id, ct);
        if (execution is null)
            return NotFound(new { error = "Execution not found." });

        if (execution.Status != ExecutionStatus.Done || string.IsNullOrEmpty(execution.ResultFilePath))
            return NotFound(new { error = "Report not ready for download." });

        var filePath = await storageService.GetDownloadUrlAsync(execution.ResultFilePath, ct);

        // Path traversal protection: verify file is under allowed base path
        if (System.IO.File.Exists(filePath))
        {
            if (!storageService.IsPathAllowed(filePath))
                return Forbid();

            var bytes = await System.IO.File.ReadAllBytesAsync(filePath, ct);
            var fileName = Path.GetFileName(filePath);
            var contentType = fileName.EndsWith(".pdf") ? "application/pdf"
                : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            return File(bytes, contentType, fileName);
        }

        // Presigned URL: redirect
        return Redirect(filePath);
    }

    /// <summary>Generate compliance evidence PDF (Admin only).</summary>
    [HttpPost("compliance/pdf")]
    [Authorize(Policy = PermCodes.Report.ReportRes.Export)]
    [EnableRateLimiting("write-ops")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GenerateCompliancePdf(
        [FromBody] CompliancePdfRequest request,
        CancellationToken ct)
    {
        var tenantId = tenantContext.TenantId?.ToString() ?? string.Empty;
        var command = new GenerateCompliancePdfCommand(
            tenantId,
            request.Framework,
            request.PeriodFrom,
            request.PeriodTo,
            request.GeneratedBy);
        var result = await mediator.Send(command, ct);
        if (!result.IsSuccess)
            return ToApiResponse(result);

        return File(result.Value, "application/pdf", $"compliance-{request.Framework}.pdf");
    }
}

// Request body records â€” lightweight, no entity types (GOV_SEC_004 mass-assignment prevention)
public sealed record RunReportRequest(
    Guid ReportDefinitionId,
    string ParametersJson,
    OutputFormat? FormatOverride = null);

public sealed record CompliancePdfRequest(
    string Framework,
    DateTimeOffset PeriodFrom,
    DateTimeOffset PeriodTo,
    string GeneratedBy);

public sealed record UpdateReportDefinitionRequest(
    string Name,
    string NameVi,
    string Description,
    string SqlTemplate,
    string ParametersSchema,
    OutputFormat OutputFormat);
