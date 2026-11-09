using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.Identity.Domain.Repositories;

/// <summary>
/// Write-side repository for TenantSignatureConfig (T1 singleton entity per tenant).
/// One row per tenant — use GetByTenantIdAsync for both GET and upsert pre-check.
/// Read-side queries use db.Set&lt;TenantSignatureConfig&gt;() directly in query handlers.
/// </summary>
public interface ITenantSignatureConfigRepository
{
    Task<TenantSignatureConfig?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);
    Task AddAsync(TenantSignatureConfig entity, CancellationToken ct = default);

    /// <summary>
    /// Persists updates to <paramref name="entity"/>.
    /// When <paramref name="expectedRowVersion"/> is non-null the implementation MUST set it
    /// as the EF OriginalValue before SaveChanges — EF appends WHERE RowVersion = @expected
    /// and throws DbUpdateConcurrencyException on stale write (admin UI stale-tab scenario).
    /// GlobalExceptionHandlerMiddleware maps that exception to HTTP 409 Conflict.
    /// </summary>
    Task UpdateAsync(TenantSignatureConfig entity, byte[]? expectedRowVersion = null, CancellationToken ct = default);
}
