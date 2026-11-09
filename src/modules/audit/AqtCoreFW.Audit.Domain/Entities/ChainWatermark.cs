namespace AqtCoreFW.Audit.Domain.Entities;

/// <summary>
/// Records the highest SequenceId a signing pass has attested for one tenant's HMAC chain, together
/// with the signature of the row at that SequenceId and an HMAC over both — computed under the same
/// key as the chain itself. This is the tail-attestation artifact that lets verify detect a key-less
/// attacker deleting the newest rows of an otherwise internally-consistent chain: the per-row chain
/// alone has no notion of "how long the chain should be", only this watermark does.
///
/// One row per tenant; <c>TenantId</c> NULL represents the system/tenant-independent chain. The
/// unique index is filtered (<c>WHERE TenantId IS NOT NULL</c>), so the DB enforces single-row only
/// for named tenants; the single system/NULL row is guaranteed instead by the per-tenant advisory
/// lock that serializes every signing pass (the same lock keyed on <c>"system"</c> for the NULL
/// chain), so an update-else-insert cannot race into a duplicate. Written/updated in place at the end
/// of every signing pass (incremental and backfill),
/// including passes that sign zero rows — a dormant chain with no new appends still gets its
/// watermark healed by the recurring chain-sweep.
///
/// Not RLS-gated (metadata table, no tenant-facing reader) and not bound to the append-only trigger
/// (that trigger targets <c>[audit].[AuditLogEntries]</c> only) — the signer updates this table via
/// raw SQL inside the same per-tenant advisory lock that serializes signing, so no concurrency
/// control beyond that lock is needed here.
///
/// Private setters only: there are no domain mutators because nothing calls into this entity through
/// EF SaveChanges. The DbSet exists for schema discoverability and migration generation; the signer
/// and (future) verifier read/write it exclusively through raw SQL, mirroring how HmacChainService
/// already treats AuditLogEntries on its hot path.
/// </summary>
public sealed class ChainWatermark
{
    private ChainWatermark() { }

    /// <summary>bigint identity, DB-assigned. Surrogate PK — TenantId is nullable so it cannot be the key.</summary>
    public long Id { get; private set; }

    /// <summary>Null = the system/tenant-independent chain.</summary>
    public Guid? TenantId { get; private set; }

    /// <summary>Highest SequenceId attested by the signing pass that last wrote this row.</summary>
    public long LastSequenceId { get; private set; }

    /// <summary>HMAC signature of the row at <see cref="LastSequenceId"/> (64-hex, same alphabet as AuditLogEntry.HmacSignature).</summary>
    public string ChainHead { get; private set; } = string.Empty;

    /// <summary>HMAC-SHA256 (hex) over the watermark payload, computed with the same key as the chain itself.</summary>
    public string WatermarkHmac { get; private set; } = string.Empty;

    /// <summary>UTC timestamp of the last upsert.</summary>
    public DateTimeOffset UpdatedAtUtc { get; private set; }
}
