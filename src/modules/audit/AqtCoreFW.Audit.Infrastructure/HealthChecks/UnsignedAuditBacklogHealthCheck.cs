using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Audit.Infrastructure.HealthChecks;

/// <summary>
/// Readiness health check that measures the unsigned-entry backlog in the audit chain.
///
/// Reports Unhealthy when EITHER condition holds across ALL tenants:
///   (a) total unsigned row count > <see cref="UnsignedAuditBacklogHealthCheckOptions.MaxUnsignedCount"/>
///   (b) age of the oldest unsigned entry  > <see cref="UnsignedAuditBacklogHealthCheckOptions.MaxOldestUnsignedAge"/>
///
/// WHY the elevated (AuditAdmin) connection is required:
///   Under a non-db_owner runtime login, the RLS FILTER predicate on AuditLogEntries exposes
///   named-tenant rows only when SESSION_CONTEXT N'TenantId' matches, and null-tenant rows only
///   via audit_admin_bypass. There is no single bypass that lets a non-db_owner login see ALL
///   tenants at once — you would need to know the tenant list before setting context (circular).
///   A db_owner-capable connection bypasses the FILTER predicate entirely (IS_MEMBER('db_owner')=1
///   short-circuits), so the COUNT and MIN(OccurredAt) queries see the true cross-tenant backlog.
///   Using the runtime Default connection would under-report the backlog and produce a falsely
///   Healthy result — the exact silent failure this check exists to catch.
/// </summary>
public sealed class UnsignedAuditBacklogHealthCheck(
    IOptions<UnsignedAuditBacklogHealthCheckOptions> options,
    string adminConnectionString,
    ILogger<UnsignedAuditBacklogHealthCheck> logger) : IHealthCheck
{
    // Single SQL round-trip: count unsigned rows AND the oldest OccurredAt together.
    // SequenceId=0 OR HmacSignature='' covers both the "never signed" state and any row
    // that somehow lost its signature during a failed partial update.
    private const string QuerySql = """
        SELECT
            COUNT(*)                    AS UnsignedCount,
            MIN(OccurredAt)             AS OldestUnsignedAt
        FROM [audit].[AuditLogEntries]
        WHERE SequenceId = 0
           OR HmacSignature = ''
        """;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var opts = options.Value;

        try
        {
            await using var conn = new SqlConnection(adminConnectionString);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = new SqlCommand(QuerySql, conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                // No rows at all — table is empty or all signed.
                return HealthCheckResult.Healthy("Audit chain: no unsigned entries.");
            }

            var unsignedCount   = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
            var oldestUnsignedAt = reader.IsDBNull(1) ? (DateTimeOffset?)null : reader.GetDateTimeOffset(1);

            reader.Close();

            var oldestAge = oldestUnsignedAt.HasValue
                ? DateTimeOffset.UtcNow - oldestUnsignedAt.Value
                : TimeSpan.Zero;

            var countExceeded = unsignedCount > opts.MaxUnsignedCount;
            var ageExceeded   = oldestUnsignedAt.HasValue && oldestAge > opts.MaxOldestUnsignedAge;

            var description = BuildDescription(unsignedCount, opts.MaxUnsignedCount, oldestAge, opts.MaxOldestUnsignedAge);

            if (countExceeded || ageExceeded)
            {
                logger.LogWarning(
                    "Audit unsigned-backlog health check UNHEALTHY: unsignedCount={Count} (threshold={CountThreshold}), " +
                    "oldestAge={OldestAge:g} (threshold={AgeThreshold:g})",
                    unsignedCount, opts.MaxUnsignedCount, oldestAge, opts.MaxOldestUnsignedAge);

                return HealthCheckResult.Unhealthy(description);
            }

            return HealthCheckResult.Healthy(description);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Audit unsigned-backlog health check failed with an unexpected error.");
            return HealthCheckResult.Unhealthy("Audit chain health check query failed.", ex);
        }
    }

    private static string BuildDescription(
        int unsignedCount,
        int maxCount,
        TimeSpan oldestAge,
        TimeSpan maxAge) =>
        $"Audit chain unsigned backlog: {unsignedCount} row(s) (threshold: {maxCount}); " +
        $"oldest unsigned age: {oldestAge:g} (threshold: {maxAge:g}).";
}
