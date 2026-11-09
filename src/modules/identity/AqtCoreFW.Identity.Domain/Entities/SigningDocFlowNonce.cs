namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Replay-prevention nonce for Tier-3 doc-flow JWT tokens (W3, QĐ 742 mục 5.1).
///
/// <para>Each doc-flow JWT is issued with a unique nonce (Guid). VGCA SignService consumes
/// the nonce twice: once on PDF download (slot 1) and once on signed-PDF upload (slot 2).
/// Any third use returns 401. Rows are cleaned up by a background job after ExpiresAt + 1d.</para>
///
/// <para>PK is the Nonce Guid itself — a standalone POCO (does NOT extend Entity/AuditableEntity
/// because the Guid-PK does not fit the bigint-identity base class pattern).
/// Schema: [identity] (via IdentityDbContext default schema).</para>
/// </summary>
public class SigningDocFlowNonce
{
    /// <summary>Primary key and JWT claim value — generated once at /initiate.</summary>
    public Guid Nonce { get; private set; }

    /// <summary>Tenant owner — used for cross-tenant isolation checks in the handler.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>FK to <see cref="SigningDocFlowRecord"/> this nonce was issued for.</summary>
    public long DocFlowRecordId { get; private set; }

    /// <summary>Number of times this nonce has been consumed (0 → 1 on download; 1 → 2 on upload).</summary>
    public int UseCount { get; private set; }

    /// <summary>Maximum allowed uses before the nonce is considered exhausted.</summary>
    public int MaxUses { get; private set; }

    /// <summary>UTC timestamp after which the nonce is invalid even if UseCount &lt; MaxUses.</summary>
    public DateTime ExpiresAt { get; private set; }

    /// <summary>
    /// Optimistic concurrency token — serialises concurrent consumes of the same nonce.
    /// Two parallel uploads carrying the same JWT both read this row at the same RowVersion,
    /// both call <see cref="TryUse"/> in memory, but only the first SaveChanges wins; the second
    /// fails with <c>DbUpdateConcurrencyException</c> (replay rejected). EF maps this as a
    /// SQL Server rowversion column. <see cref="TryUse"/> alone is an in-memory check and is
    /// NOT sufficient against concurrent callers — this token is what makes consume-once atomic.
    /// </summary>
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    /// <summary>EF Core: private parameterless constructor for materialisation.</summary>
    private SigningDocFlowNonce() { }

    /// <summary>
    /// Factory — creates a fresh nonce with UseCount=0.
    /// <paramref name="ttlSeconds"/> maps to the <c>Signing:DocFlowJwt:TtlSeconds</c> option (default 300).
    /// </summary>
    public static SigningDocFlowNonce Create(
        Guid nonce,
        Guid tenantId,
        long docFlowRecordId,
        int ttlSeconds = 300)
    {
        if (nonce == Guid.Empty)
            throw new ArgumentException("Nonce must not be empty.", nameof(nonce));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId must not be empty.", nameof(tenantId));
        if (docFlowRecordId <= 0)
            throw new ArgumentException("DocFlowRecordId must be a positive identity value.", nameof(docFlowRecordId));
        if (ttlSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(ttlSeconds), "TTL must be positive.");

        return new SigningDocFlowNonce
        {
            Nonce            = nonce,
            TenantId         = tenantId,
            DocFlowRecordId  = docFlowRecordId,
            UseCount         = 0,
            MaxUses          = 2,
            ExpiresAt        = DateTime.UtcNow.AddSeconds(ttlSeconds),
        };
    }

    /// <summary>
    /// Atomically increments <see cref="UseCount"/> and validates the nonce is still usable.
    /// Returns true when the increment succeeds (caller should SaveChanges immediately after).
    /// Returns false when the nonce is already exhausted or expired — caller returns 401.
    /// </summary>
    public bool TryUse()
    {
        if (DateTime.UtcNow >= ExpiresAt)
            return false;
        if (UseCount >= MaxUses)
            return false;

        UseCount++;
        return true;
    }
}
