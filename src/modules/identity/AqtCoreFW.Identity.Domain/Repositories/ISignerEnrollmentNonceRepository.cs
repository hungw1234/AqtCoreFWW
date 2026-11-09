using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.Identity.Domain.Repositories;

/// <summary>
/// Store for <see cref="SignerEnrollmentNonce"/> — the single-use possession-proof challenges used by
/// self-service signature-cert enrollment. Scoped to (tenantId, id); a nonce never resolves outside its
/// issuing tenant.
/// </summary>
public interface ISignerEnrollmentNonceRepository
{
    /// <summary>Persists a freshly issued nonce (insert + SaveChanges).</summary>
    Task AddAsync(SignerEnrollmentNonce nonce, CancellationToken ct = default);

    /// <summary>
    /// Loads a nonce by (tenantId, id) for mutation (tracked) — returns null if it does not exist in the
    /// tenant. The caller additionally verifies the nonce belongs to the submitting user (anti-replay).
    /// </summary>
    Task<SignerEnrollmentNonce?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Flushes pending mutations on tracked nonces (e.g. after Consume).</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}
