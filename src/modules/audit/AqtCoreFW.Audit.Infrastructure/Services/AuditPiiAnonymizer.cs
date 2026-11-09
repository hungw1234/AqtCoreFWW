using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Contracts.Rtbf;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Audit.Infrastructure.Services;

/// <summary>
/// RTBF anonymizer for Audit module.
/// Anonymizes DataSnapshot in AuditLogEntries where UserId = dataSubjectId
/// or ResourceId matches the subject's GUID string representation.
/// Uses raw Dapper UPDATE — bypasses EF tamper-prevention guard (which only
/// fires for EF-tracked Modified/Deleted entries, not raw SQL).
/// Idempotent: rows already containing "[DA AN DANH]" are skipped via WHERE clause.
/// HMAC chain integrity: DataSnapshot is not part of BuildFingerprint(), so
/// anonymizing it does not invalidate the HMAC chain.
/// </summary>
public sealed class AuditPiiAnonymizer(
    AuditDbContext db,
    ILogger<AuditPiiAnonymizer> logger) : IModulePiiAnonymizer
{
    private const string Sentinel = "[DA AN DANH]";

    public string ModuleName => "Audit";

    public async Task<RtbfAnonymizationResult> AnonymizeAsync(
        Guid dataSubjectId,
        Guid tenantId,
        string? citizenNationalId,
        CancellationToken cancellationToken = default)
    {
        var conn = db.Database.GetDbConnection() as SqlConnection
                   ?? throw new InvalidOperationException("Audit DB connection is not SqlConnection");

        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(cancellationToken);

        // Anonymize entries where subject is the actor (UserId match) or the resource (ResourceId match).
        // DataSnapshot <> @sentinel ensures idempotency — already-anonymized rows are skipped.
        // Anonymize AuditLogEntries where subject is actor or resource
        int auditRows = await conn.ExecuteAsync(
            """
            UPDATE audit.AuditLogEntries
            SET DataSnapshot = @sentinel
            WHERE TenantId = @tenantId
              AND (UserId = @subjectId
                   OR ResourceId = @subjectIdStr)
              AND (DataSnapshot IS NULL OR DataSnapshot <> @sentinel)
            """,
            new
            {
                sentinel = Sentinel,
                tenantId,
                subjectId = dataSubjectId,
                subjectIdStr = dataSubjectId.ToString()
            });

        // [RT-01] Scrub denormalized SubjectEmail in RtbfRequests to prevent PII leak
        int rtbfRows = await conn.ExecuteAsync(
            """
            UPDATE audit.RtbfRequests
            SET SubjectEmail = @sentinel
            WHERE TenantId = @tenantId
              AND DataSubjectId = @subjectId
              AND (SubjectEmail IS NULL OR SubjectEmail <> @sentinel)
            """,
            new { sentinel = Sentinel, tenantId, subjectId = dataSubjectId });

        var totalRows = auditRows + rtbfRows;
        logger.LogInformation(
            "RTBF Audit: anonymized {AuditCount} AuditLogEntries + {RtbfCount} RtbfRequests.SubjectEmail for subject {SubjectId}",
            auditRows, rtbfRows, dataSubjectId);

        return RtbfAnonymizationResult.Ok(ModuleName, totalRows);
    }
}
