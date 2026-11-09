using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Single-use possession-proof challenge for self-service signature-cert enrollment. The server issues a
/// random <see cref="Challenge"/>; the enrolling user signs it — remotely via Viettel Mysign (phone OTP)
/// or locally via the VGCA USB plugin — and submits the signature, proving live control of the private
/// key behind the certificate.
///
/// <para>Net-new and deliberately NOT modelled on <c>SigningDocFlowNonce</c>: this nonce is bound to a
/// specific <see cref="UserId"/> (+ <see cref="TenantId"/>) so user B can never replay a nonce issued to
/// user A, has no FK to a doc-flow record, and is single-use (one <see cref="Consume"/>, not a use-count).</para>
///
/// <para>POCO (no audit/soft-delete base) like <c>SigningDocFlowNonce</c>: a nonce is transient
/// infrastructure, not an auditable aggregate. Tenant isolation is enforced by RLS (DB-level) like the
/// other Identity signing tables. <see cref="RowVersion"/> guards the single-use transition against a
/// concurrent double-submit.</para>
/// </summary>
public class SignerEnrollmentNonce : ITenantScoped
{
    /// <summary>Server-generated nonce id (PK) returned to the client and echoed back on submit.</summary>
    public Guid Id { get; private set; }

    /// <inheritdoc/>
    public Guid TenantId { get; private set; }

    /// <summary>The user this nonce was issued to — a submit by any other user is rejected (anti-replay).</summary>
    public Guid UserId { get; private set; }

    /// <summary>Random bytes the client/provider signs (raw, then SHA256 by the verifier). ≥16 bytes.</summary>
    public byte[] Challenge { get; private set; } = [];

    /// <summary>UTC expiry — a submit at/after this instant is rejected.</summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>UTC instant the nonce was consumed; null while unused. Single-use: set once by <see cref="Consume"/>.</summary>
    public DateTime? ConsumedAt { get; private set; }

    /// <summary>EF Core optimistic concurrency token — serialises the consume transition (anti double-submit).</summary>
    public byte[]? RowVersion { get; private set; }

    private SignerEnrollmentNonce() { }

    /// <summary>
    /// Issues a nonce for <paramref name="userId"/> in <paramref name="tenantId"/> valid for
    /// <paramref name="ttlSeconds"/>. The caller supplies the random <paramref name="challenge"/> (generated
    /// with a cryptographic RNG in the application layer).
    /// </summary>
    public static SignerEnrollmentNonce Issue(
        IGuidGenerator gen, Guid tenantId, Guid userId, byte[] challenge, int ttlSeconds = 300)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId must not be empty.", nameof(tenantId));
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId must not be empty.", nameof(userId));
        if (challenge is not { Length: >= 16 })
            throw new ArgumentException("Challenge must be at least 16 bytes.", nameof(challenge));
        if (ttlSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(ttlSeconds), "TTL must be positive.");

        return new SignerEnrollmentNonce
        {
            Id        = gen.NewIdRandom(),
            TenantId  = tenantId,
            UserId    = userId,
            Challenge = challenge,
            ExpiresAt = DateTime.UtcNow.AddSeconds(ttlSeconds),
        };
    }

    /// <summary>True once the nonce has been consumed (single-use).</summary>
    public bool IsConsumed => ConsumedAt.HasValue;

    /// <summary>True when the nonce is at/past its expiry relative to <paramref name="utcNow"/>.</summary>
    public bool IsExpired(DateTime utcNow) => utcNow >= ExpiresAt;

    /// <summary>
    /// Marks the nonce consumed. Throws if already consumed — the caller checks <see cref="IsConsumed"/>
    /// first and returns a clean error; this is the defensive backstop for the single-use invariant.
    /// </summary>
    public void Consume(DateTime utcNow)
    {
        if (IsConsumed)
            throw new InvalidOperationException("Enrollment nonce has already been consumed.");
        ConsumedAt = utcNow;
    }
}
