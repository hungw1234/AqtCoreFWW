using AqtCoreFW.Audit.Application.Commands.BackfillAuditChain;
using AqtCoreFW.Audit.Application.Queries.ExportAuditLogs;
using AqtCoreFW.Audit.Application.Queries.GetAuditLogs;
using AqtCoreFW.Audit.Application.Queries.GetAuditStatistics;
using AqtCoreFW.Audit.Application.Queries.VerifyAuditChain;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AqtCoreFW.Audit.Presentation.Controllers;

/// <summary>
/// Audit log read endpoints - compliance evidence for QD742 / Law 91/2025.
/// Restricted to GovOfficer and above - audit logs contain sensitive operational data.
/// Tenant isolation: non-SystemAdmin users are always scoped to their own tenantId from JWT.
/// </summary>
[Route("api/v1/audit")]
[Authorize(Policy = PermCodes.Audit.Log.View)]
public sealed class AuditLogsController(ISender mediator, ITenantContext tenantContext) : ApiControllerBase
{
    /// <summary>Query audit logs with filters and pagination.</summary>
    [HttpGet("logs")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetLogs(
        [FromQuery] Guid? tenantId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? userId,
        [FromQuery] string? action,
        [FromQuery] string? moduleName,
        [FromQuery] string? resourceType,
        [FromQuery] string? resourceId,
        [FromQuery][StringLength(200)] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        return ToApiResponse(await mediator.Send(
            new GetAuditLogsQuery(ResolveTenantId(tenantId), from, to, userId, action, moduleName, resourceType, resourceId, search, page, pageSize),
            cancellationToken));
    }

    /// <summary>Aggregated audit statistics (today/week/month counts, by action, by module).</summary>
    [HttpGet("statistics")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetStatistics(
        [FromQuery] Guid? tenantId,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(new GetAuditStatisticsQuery(ResolveTenantId(tenantId)), cancellationToken));

    /// <summary>
    /// Export audit logs as CSV (max 10,000 rows).
    /// Returns text/csv with UTF-8 BOM so Excel opens without encoding issues.
    /// </summary>
    [HttpGet("logs/export")]
    [Authorize(Policy = PermCodes.Audit.Log.Export)]
    [ProducesResponseType(typeof(FileContentResult), 200)]
    public async Task<IActionResult> ExportLogs(
        [FromQuery] Guid? tenantId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] Guid? userId,
        [FromQuery] string? action,
        [FromQuery] string? moduleName,
        [FromQuery] string? resourceType,
        [FromQuery] string? resourceId,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new ExportAuditLogsCsvQuery(ResolveTenantId(tenantId), from, to, userId, action, moduleName, resourceType, resourceId),
            cancellationToken);

        if (result.IsFailed)
            return BadRequest(result.Errors);

        var filename = $"audit-logs-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
        return File(result.Value, "text/csv; charset=utf-8", filename);
    }

    /// <summary>Verify HMAC chain integrity for a tenant's audit log - compliance audit export.</summary>
    [HttpGet("logs/verify-chain")]
    [Authorize(Policy = PermCodes.System.Config.Manage)]
    [ProducesResponseType(200)]
    public async Task<IActionResult> VerifyChain(
        [FromQuery] Guid tenantId,
        CancellationToken cancellationToken = default) =>
        ToApiResponse(await mediator.Send(new VerifyAuditChainQuery(tenantId), cancellationToken));

    /// <summary>
    /// Enqueue a background job to recompute and re-sign the HMAC chain for ALL tenants.
    /// Returns 202 Accepted immediately with the Hangfire job id; the backfill runs asynchronously
    /// on the audit lane to avoid 504 on large prod tables.
    /// Holds a per-tenant applock during each tenant pass so concurrent live signers wait.
    /// The job writes one audit trail entry recording who triggered the backfill and how many
    /// rows were re-signed once it completes.
    /// Restricted to SYSTEM.CONFIG.MANAGE (same gate as VerifyChain - both are SystemAdmin-only operations).
    /// </summary>
    [HttpPost("chain/backfill")]
    [Authorize(Policy = PermCodes.System.Config.Manage)]
    [ProducesResponseType(202)]
    public async Task<IActionResult> BackfillChain(CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(new BackfillAuditChainCommand(), cancellationToken);
        if (result.IsFailed)
            return BadRequest(result.Errors);

        return Accepted(new { jobId = result.Value });
    }

    /// <summary>
    /// Resolve effective tenantId: non-SystemAdmin users are always scoped to their JWT tenant.
    /// SystemAdmin with null tenantId param = cross-tenant query (system-wide auditing).
    /// </summary>
    private Guid? ResolveTenantId(Guid? requestedTenantId) =>
        tenantContext.IsSystemAdmin ? requestedTenantId : tenantContext.TenantId;
}
