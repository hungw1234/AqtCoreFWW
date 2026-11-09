using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using Dapper;

namespace AqtCoreFW.Identity.Infrastructure.Services;

/// <summary>
/// Monolith in-process adapter for IIdentityAuditClient.
/// Direct Dapper query against [identity].AccessReviews — schema-local, no cross-schema JOIN.
/// Replaces the cross-schema FROM clause previously embedded in
/// Reporting.GetComplianceEvidenceQueryHandler.
///
/// PRESERVES PRE-EXISTING BUG: legacy SQL compared the int column <c>Decision</c> against
/// the string literals <c>'Approved'</c> / <c>'Revoked'</c>, which never match → both counts
/// always = 0. Phase 01 = no behavior change, so the bug is preserved verbatim. A separate
/// follow-up ticket will fix the comparison to match the int enum (Retain=1, Revoke=2).
/// </summary>
public sealed class InProcessIdentityAuditModuleClient(IReadDbConnection db) : IIdentityAuditClient
{
    public async Task<AccessReviewStatusSummary> GetAccessReviewStatusAsync(
        Guid tenantId,
        DateTimeOffset periodFrom,
        DateTimeOffset periodTo,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT
                COUNT(*) AS TotalReviewed,
                SUM(CASE WHEN Decision = 'Approved' THEN 1 ELSE 0 END) AS Approved,
                SUM(CASE WHEN Decision = 'Revoked' THEN 1 ELSE 0 END) AS Revoked,
                ISNULL(MAX(ReviewedAt), @PeriodFrom) AS LastReviewDate
            FROM [identity].AccessReviews
            WHERE TenantId = @TenantId
              AND ReviewedAt >= @PeriodFrom
              AND ReviewedAt < @PeriodTo
            """;

        return await db.QuerySingleAsync<AccessReviewStatusSummary>(
            sql,
            new { TenantId = tenantId, PeriodFrom = periodFrom, PeriodTo = periodTo },
            ct);
    }
}
