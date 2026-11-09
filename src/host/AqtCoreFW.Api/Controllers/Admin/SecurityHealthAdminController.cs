using AqtCoreFW.Identity.Application.SecurityHealth.Queries;
using AqtCoreFW.SharedKernel.Application.Pagination;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.SharedKernel.Api;
using AqtCoreFW.SharedKernel.Presentation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AqtCoreFW.Api.Controllers.Admin;

/// <summary>
/// QĐ 742 cấp 4 — D7 per-criterion visibility pages (read-only aggregator).
/// All endpoints require Security.Health.View permission + valid MFA session (QĐ 742 §1.6.b).
///
/// Endpoints surface compliance snapshots for six criteria:
///   1.4.b/1.4.d  — login / lockout policy
///   1.5.a/1.5.b  — password complexity + rotation policy
///   3.2.a/3.2.b  — audit retention configuration + live row counts
///   3.4.a        — audit log immutability (trigger health check)
///   4.4.a        — encryption key rotation status
///   6.1.b        — backup / restore-drill history
/// </summary>
[Route("api/v1/admin/security/health")]
[Authorize(Policy = PermCodes.Security.Health.View)]
[RequiresMfa]
[EnableRateLimiting("write-ops")]
public sealed class SecurityHealthAdminController(ISender mediator) : ApiControllerBase
{
    /// <summary>
    /// GET /api/v1/admin/security/health/login-policy
    /// QĐ 742 §1.4.b/1.4.d — lockout thresholds + escalation settings for tenant.
    /// </summary>
    [HttpGet("login-policy")]
    [ProducesResponseType(typeof(ApiResponse<LoginPolicyStatusDto>), 200)]
    public async Task<IActionResult> GetLoginPolicyStatus(CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        return ToApiResponse(await mediator.Send(new GetLoginPolicyStatusQuery(tenantId), ct));
    }

    /// <summary>
    /// GET /api/v1/admin/security/health/password-policy
    /// QĐ 742 §1.5.a/1.5.b — password complexity + rotation policy snapshot.
    /// Returns both ASP.NET Identity framework defaults and project CredentialPolicy overrides.
    /// </summary>
    [HttpGet("password-policy")]
    [ProducesResponseType(typeof(ApiResponse<PasswordPolicyStatusDto>), 200)]
    public async Task<IActionResult> GetPasswordPolicyStatus(CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        return ToApiResponse(await mediator.Send(new GetPasswordPolicyStatusQuery(tenantId), ct));
    }

    /// <summary>
    /// GET /api/v1/admin/security/health/audit-retention
    /// QĐ 742 §3.2.a/3.2.b — configured retention windows + live AuditLogEntries row count.
    /// </summary>
    [HttpGet("audit-retention")]
    [ProducesResponseType(typeof(ApiResponse<AuditRetentionStatusDto>), 200)]
    public async Task<IActionResult> GetAuditRetentionStatus(CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        return ToApiResponse(await mediator.Send(new GetAuditRetentionStatusQuery(tenantId), ct));
    }

    /// <summary>
    /// GET /api/v1/admin/security/health/audit-integrity
    /// QĐ 742 §3.4.a — immutability trigger health check + total log entry count.
    /// Returns ImmutabilityTriggerActive=false if tr_AuditLog_NoMutate is disabled or missing.
    /// </summary>
    [HttpGet("audit-integrity")]
    [ProducesResponseType(typeof(ApiResponse<AuditIntegrityStatusDto>), 200)]
    public async Task<IActionResult> GetAuditIntegrityStatus(CancellationToken ct = default)
        => ToApiResponse(await mediator.Send(new GetAuditIntegrityStatusQuery(), ct));

    /// <summary>
    /// GET /api/v1/admin/security/health/key-rotation
    /// QĐ 742 §4.4.a — current encryption key version.
    /// LastRotationAt/NextScheduledRotationAt are null until IKeyRotationMetadataReader
    /// is added to SharedKernel.Contracts (backlog).
    /// </summary>
    [HttpGet("key-rotation")]
    [ProducesResponseType(typeof(ApiResponse<KeyRotationStatusDto>), 200)]
    public async Task<IActionResult> GetKeyRotationStatus(CancellationToken ct = default)
        => ToApiResponse(await mediator.Send(new GetKeyRotationStatusQuery(), ct));

    /// <summary>
    /// GET /api/v1/admin/security/health/backup-history?page=1&amp;pageSize=20
    /// QĐ 742 §6.1.b — paginated backup / restore-drill history (newest first).
    /// </summary>
    [HttpGet("backup-history")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<BackupHistoryItemDto>>), 200)]
    public async Task<IActionResult> GetBackupHistoryStatus(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => ToApiResponse(await mediator.Send(
            new GetBackupHistoryStatusQuery(page, pageSize), ct));
}
