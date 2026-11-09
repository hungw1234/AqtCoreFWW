namespace AqtCoreFW.SharedKernel.Contracts.Clients;

/// <summary>
/// Cross-module read-only client over the audit module's own <c>[audit]</c> schema. Lets other
/// modules read audit rollups WITHOUT holding their own <c>[audit]</c>-capable DB credential — the
/// reads run on the audit-owner login inside the audit module (ISO read-isolation). Callers MUST be
/// SystemAdmin or own the tenant — the interface trusts caller authorization.
/// Monolith: InProcessAuditEvidenceClient. Microservice: HttpAuditEvidenceClient.
/// </summary>
public interface IAuditEvidenceClient
{
    /// <summary>
    /// Aggregate compliance evidence for a tenant in a closed-open period.
    /// Returns audit-log summary, top-50 security incidents (by OccurredAt DESC),
    /// and tenant-wide retention status snapshot (period-agnostic for retention bucket).
    /// 3 internal Dapper roundtrips behind a single public method (M2 — collapsed surface).
    /// </summary>
    Task<ComplianceEvidenceData> GetComplianceEvidenceAsync(
        Guid tenantId,
        DateTimeOffset periodFrom,
        DateTimeOffset periodTo,
        CancellationToken ct = default);

    /// <summary>
    /// Audit-log immutability + integrity snapshot for the D7 security-health dashboard
    /// (QĐ 742 §3.4.a): whether <c>tr_AuditLog_NoMutate</c> is active + total entry count.
    /// Reads <c>[audit]</c> on the audit-owner login; admin-only (audit-admin bypass) — the caller
    /// does NOT need its own <c>[audit]</c> credential.
    /// </summary>
    Task<AuditIntegrityStatusData> GetIntegrityStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Audit retention policies + live hot-row count for the D7 security-health dashboard
    /// (QĐ 742 §3.2.a/3.2.b). <paramref name="tenantId"/> null = global / all-tenant view.
    /// Reads <c>[audit]</c> on the audit-owner login; admin-only (audit-admin bypass).
    /// </summary>
    Task<AuditRetentionStatusData> GetRetentionPolicyStatusAsync(
        Guid? tenantId,
        CancellationToken ct = default);
}

/// <summary>3-bucket evidence record. Aggregator output; not Dapper-deserialized.</summary>
public sealed record ComplianceEvidenceData(
    AuditLogSummary AuditSummary,
    IReadOnlyList<SecurityIncidentEvidence> SecurityIncidents,
    RetentionStatusEvidence RetentionStatus);

/// <summary>Audit log totals + period echo. Class-based for Dapper (no positional record).</summary>
public sealed class AuditLogSummary
{
    public int TotalEvents { get; init; }
    public int LoginEvents { get; init; }
    public int DataAccessEvents { get; init; }
    public int AdminEvents { get; init; }
    public DateTimeOffset PeriodFrom { get; init; }
    public DateTimeOffset PeriodTo { get; init; }
}

/// <summary>Top-N security incident row. Class-based for Dapper.</summary>
public sealed class SecurityIncidentEvidence
{
    public string IncidentId { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public DateTimeOffset OccurredAt { get; init; }
}

/// <summary>Tenant-wide retention status snapshot. Class-based for Dapper.</summary>
public sealed class RetentionStatusEvidence
{
    public int TotalRecords { get; init; }
    public int ArchivedRecords { get; init; }
    public int PurgedRecords { get; init; }
    public int PendingPurge { get; init; }
}

// ── D7 security-health read models (GetIntegrityStatusAsync / GetRetentionPolicyStatusAsync) ──

/// <summary>
/// Audit-log integrity snapshot (D7). <see cref="ImmutabilityTriggerActive"/> = the DB-level
/// UPDATE/DELETE guard <c>tr_AuditLog_NoMutate</c> exists and is enabled.
/// </summary>
public sealed record AuditIntegrityStatusData(
    bool ImmutabilityTriggerActive,
    string TriggerName,
    long TotalLogEntries);

/// <summary>One retention window for a (TenantId, CategoryId) pair. CategoryId null = all-categories default.</summary>
public sealed record AuditRetentionPolicyLine(
    long Id,
    Guid? TenantId,
    int? CategoryId,
    int RetentionDays,
    bool ArchiveBeforePurge,
    string? ArchivePath);

/// <summary>
/// Retention status (D7): configured policies + live hot-row count.
/// <see cref="ArchiveRowCount"/> is null until a cold-archive sink is provisioned.
/// </summary>
public sealed record AuditRetentionStatusData(
    IReadOnlyList<AuditRetentionPolicyLine> Policies,
    long HotRowCount,
    long? ArchiveRowCount);
