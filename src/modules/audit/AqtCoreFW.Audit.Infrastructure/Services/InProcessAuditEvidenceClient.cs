using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Audit.Infrastructure.Services;

/// <summary>
/// Monolith in-process adapter for IAuditEvidenceClient.
/// 3 sequential Dapper roundtrips against own-schema audit.* tables.
/// Swap to HttpAuditEvidenceClient when module extracted.
/// </summary>
public sealed class InProcessAuditEvidenceClient(
    [FromKeyedServices(ReadConnectionKeys.Audit)] IReadDbConnection db) : IAuditEvidenceClient
{
    public async Task<ComplianceEvidenceData> GetComplianceEvidenceAsync(
        Guid tenantId,
        DateTimeOffset periodFrom,
        DateTimeOffset periodTo,
        CancellationToken ct = default)
    {
        var summary = await GetAuditSummaryAsync(tenantId, periodFrom, periodTo, ct);
        var incidents = await ListSecurityIncidentsAsync(tenantId, periodFrom, periodTo, ct);
        var retention = await GetRetentionStatusAsync(tenantId, ct);
        return new ComplianceEvidenceData(summary, incidents, retention);
    }

    private async Task<AuditLogSummary> GetAuditSummaryAsync(
        Guid tenantId, DateTimeOffset periodFrom, DateTimeOffset periodTo, CancellationToken ct)
    {
        const string sql = """
            SELECT
                COUNT(*) AS TotalEvents,
                SUM(CASE WHEN EventType = 'Login' THEN 1 ELSE 0 END) AS LoginEvents,
                SUM(CASE WHEN EventType = 'DataAccess' THEN 1 ELSE 0 END) AS DataAccessEvents,
                SUM(CASE WHEN EventType LIKE 'Admin%' THEN 1 ELSE 0 END) AS AdminEvents,
                @PeriodFrom AS PeriodFrom,
                @PeriodTo AS PeriodTo
            FROM audit.AuditLogs
            WHERE TenantId = @TenantId
              AND OccurredAt >= @PeriodFrom
              AND OccurredAt < @PeriodTo
              AND IsDeleted = 0
            """;
        return await db.QuerySingleAsync<AuditLogSummary>(sql,
            new { TenantId = tenantId, PeriodFrom = periodFrom, PeriodTo = periodTo }, ct);
    }

    private async Task<IReadOnlyList<SecurityIncidentEvidence>> ListSecurityIncidentsAsync(
        Guid tenantId, DateTimeOffset periodFrom, DateTimeOffset periodTo, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP 50
                CAST(MessageId AS nvarchar(36)) AS IncidentId,
                Severity, Status, Description, OccurredAt
            FROM audit.SecurityIncidents
            WHERE TenantId = @TenantId
              AND OccurredAt >= @PeriodFrom
              AND OccurredAt < @PeriodTo
            ORDER BY OccurredAt DESC
            """;
        var rows = await db.QueryAsync<SecurityIncidentEvidence>(sql,
            new { TenantId = tenantId, PeriodFrom = periodFrom, PeriodTo = periodTo }, ct);
        return rows.ToList();
    }

    private async Task<RetentionStatusEvidence> GetRetentionStatusAsync(
        Guid tenantId, CancellationToken ct)
    {
        const string sql = """
            SELECT
                COUNT(*) AS TotalRecords,
                SUM(CASE WHEN IsArchived = 1 THEN 1 ELSE 0 END) AS ArchivedRecords,
                SUM(CASE WHEN IsPurged = 1 THEN 1 ELSE 0 END) AS PurgedRecords,
                SUM(CASE WHEN RetentionExpiresAt < GETUTCDATE() AND IsPurged = 0 THEN 1 ELSE 0 END) AS PendingPurge
            FROM audit.RetentionRecords
            WHERE TenantId = @TenantId
            """;
        return await db.QuerySingleAsync<RetentionStatusEvidence>(sql,
            new { TenantId = tenantId }, ct);
    }

    // ── D7 security-health reads (moved from the identity SecurityHealth handlers, ISO refactor) ──
    // Identity no longer holds an [audit]-capable credential; these reads run on the audit-owner login here.

    public async Task<AuditIntegrityStatusData> GetIntegrityStatusAsync(CancellationToken ct = default)
    {
        // sys.triggers is a catalog view (not an [audit] user table) — no RLS applies here.
        const string triggerSql = """
            SELECT COUNT(1)
            FROM sys.triggers
            WHERE name = N'tr_AuditLog_NoMutate'
              AND is_disabled = 0
            """;
        var triggerActive = await db.QuerySingleAsync<int>(triggerSql, cancellationToken: ct) > 0;

        // RLS: audit_admin_bypass — admin read of system/global audit rows (D7 is admin-only, so the
        // true count must include NULL-tenant rows hidden from a non-admin connection).
        const string countSql = "SELECT COUNT_BIG(1) FROM [audit].AuditLogEntries";
        var totalEntries = await db.QuerySingleWithAuditAdminBypassAsync<long>(countSql, cancellationToken: ct);

        return new AuditIntegrityStatusData(triggerActive, "tr_AuditLog_NoMutate", totalEntries);
    }

    public async Task<AuditRetentionStatusData> GetRetentionPolicyStatusAsync(
        Guid? tenantId, CancellationToken ct = default)
    {
        // Retention policies for the tenant (+ global rows where TenantId IS NULL).
        const string policySql = """
            SELECT p.Id, p.TenantId, p.Category AS CategoryId,
                   p.RetentionDays, p.ArchiveBeforePurge, p.ArchivePath
            FROM [audit].AuditRetentionPolicies p
            WHERE p.TenantId = @TenantId OR p.TenantId IS NULL
            ORDER BY p.TenantId DESC, p.Category ASC
            """;
        var policyRows = await db.QueryAsync<RetentionPolicyRow>(
            policySql, new { TenantId = tenantId }, ct);
        var policies = policyRows
            .Select(r => new AuditRetentionPolicyLine(
                r.Id, r.TenantId, r.CategoryId, r.RetentionDays, r.ArchiveBeforePurge, r.ArchivePath))
            .ToList();

        // Hot row count — tenant-scoped when tenantId provided, global otherwise.
        // RLS: audit_admin_bypass — when tenantId is null (global view) count NULL-tenant rows too.
        const string countSql = """
            SELECT COUNT_BIG(1)
            FROM [audit].AuditLogEntries
            WHERE (@TenantId IS NULL OR TenantId = @TenantId)
            """;
        var hotRowCount = await db.QuerySingleWithAuditAdminBypassAsync<long>(
            countSql, new { TenantId = tenantId }, ct);

        return new AuditRetentionStatusData(policies, hotRowCount, ArchiveRowCount: null);
    }
}

/// <summary>Dapper row for the retention-policy read — class (not a positional record) for column binding.</summary>
internal sealed class RetentionPolicyRow
{
    public long Id { get; init; }
    public Guid? TenantId { get; init; }
    public int? CategoryId { get; init; }
    public int RetentionDays { get; init; }
    public bool ArchiveBeforePurge { get; init; }
    public string? ArchivePath { get; init; }
}
