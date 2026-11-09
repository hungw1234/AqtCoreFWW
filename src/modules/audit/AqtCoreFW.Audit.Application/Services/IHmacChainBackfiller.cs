namespace AqtCoreFW.Audit.Application.Services;

/// <summary>
/// Abstracts HMAC chain backfill so the Application layer has no Infrastructure dependency.
/// Infrastructure provides the implementation via <c>HmacChainService</c>.
///
/// The backfill recomputes the full chain for every tenant: resets SequenceId to 1..N and
/// re-signs ALL rows (including previously signed rows) in InternalId order, under the same
/// per-tenant sp_getapplock as the live signer. Any concurrent live signer for the same tenant
/// waits until the backfill releases the lock.
///
/// Returns the total count of rows re-signed across all tenants, so the audit trail entry
/// written by the caller can record the scope of the operation.
/// </summary>
public interface IHmacChainBackfiller
{
    /// <summary>
    /// Recomputes the full HMAC chain for all tenants (including the NULL/system tenant).
    /// Returns the total number of rows re-signed across every tenant chain.
    /// </summary>
    Task<long> BackfillAllTenantsAsync(CancellationToken cancellationToken = default);
}
