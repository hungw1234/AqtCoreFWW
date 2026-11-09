namespace AqtCoreFW.SharedKernel.Contracts.Clients;

/// <summary>
/// Cross-module aggregator for access-review evidence (compliance reporting).
/// Returns pre-aggregated rollups so callers don't pull raw audit rows across module boundaries.
///
/// Monolith: <c>InProcessIdentityAuditModuleClient</c> (Identity.Infrastructure).
/// Microservice: <c>HttpIdentityAuditClient</c> when Identity is extracted.
///
/// NOTE: lives in SharedKernel.Contracts pre-split. Will migrate to
/// <c>AqtCoreFW.Identity.Contracts</c> assembly during Phase 05.
/// </summary>
public interface IIdentityAuditClient
{
    /// <summary>
    /// Returns access-review rollup for a tenant over a closed-open period [<paramref name="periodFrom"/>, <paramref name="periodTo"/>).
    /// Caller MUST be SystemAdmin or own the tenant — interface trusts caller authorization.
    /// </summary>
    Task<AccessReviewStatusSummary> GetAccessReviewStatusAsync(
        Guid tenantId,
        DateTimeOffset periodFrom,
        DateTimeOffset periodTo,
        CancellationToken ct = default);
}

/// <summary>
/// [RT-12] Class-based DTO for Dapper compatibility.
/// Aggregated access-review snapshot for compliance evidence PDF.
/// </summary>
public sealed class AccessReviewStatusSummary
{
    public int TotalReviewed { get; init; }
    public int Approved { get; init; }
    public int Revoked { get; init; }
    public DateTimeOffset LastReviewDate { get; init; }
}
