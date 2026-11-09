using AqtCoreFW.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for SigningDocFlowRecord ([identity] schema, W3 QĐ 742 mục 5.1).
///
/// Standalone configuration (does NOT extend EntityTypeConfigurationBase) because
/// SigningDocFlowRecord uses the long bigint PK from AuditableEntity/Entity base class
/// and requires explicit table-name + enum-as-int storage that the base class already
/// provides via convention — no additional PK wiring needed here.
///
/// Indexes:
///   IX_SigningDocFlowRecord_TenantId_DocId — composite lookup for per-tenant doc status check.
///   IX_SigningDocFlowRecord_JwtNonce       — 1:1 FK lookup from nonce validation path (unique).
///   IX_SigningDocFlowRecord_Status         — supports expiry sweep query.
/// </summary>
public sealed class SigningDocFlowRecordConfiguration : IEntityTypeConfiguration<SigningDocFlowRecord>
{
    public void Configure(EntityTypeBuilder<SigningDocFlowRecord> builder)
    {
        builder.ToTable("SigningDocFlowRecords");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.TenantId)
            .IsRequired();

        builder.Property(r => r.DocId)
            .IsRequired();

        builder.Property(r => r.SignType)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(r => r.Status)
            .HasConversion<int>()
            .IsRequired()
            .HasDefaultValueSql("0") // Initiated
            // Sentinel = a value the enum never holds, so EF always emits the explicit status on
            // INSERT instead of treating Initiated (the CLR-default 0) as "unset" and deferring to
            // the DB default. Behaviour is unchanged today; this only removes the model ambiguity.
            .HasSentinel((DocFlowStatus)(-1));

        builder.Property(r => r.OriginalStorageKey)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(r => r.SignedStorageKey)
            .HasMaxLength(512)
            .IsRequired(false);

        // Incremental multi-signer chaining: the signed PDF produced by signer N-1 that signer N
        // downloads and appends to. Null for sequence 1 (first signer signs the original PDF).
        builder.Property(r => r.PreviousSignedStorageKey)
            .HasMaxLength(512)
            .IsRequired(false);

        // Signer position in an ordered multi-signer chain. Default 1 covers all existing single-signer
        // rows. Combined with (TenantId, DocId) uniquely identifies a round in the chain.
        builder.Property(r => r.SignerSequenceNumber)
            .IsRequired()
            .HasDefaultValueSql("1")
            .HasSentinel(-1); // sentinel so EF always emits an explicit value on INSERT

        builder.Property(r => r.JwtNonce)
            .IsRequired();

        builder.Property(r => r.InitiatedBy)
            .IsRequired();

        builder.Property(r => r.InitiatedAt)
            .IsRequired();

        builder.Property(r => r.CompletedAt)
            .IsRequired(false);

        builder.Property(r => r.FailureReason)
            .HasMaxLength(1024)
            .IsRequired(false);

        // Optimistic concurrency — maps to SQL Server rowversion. Guards the Downloaded/Completed
        // status transitions against concurrent callers (TOCTOU on the in-memory status check).
        builder.Property(r => r.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        // Inherited audit columns from AuditableEntity / Entity
        builder.Property(r => r.CreatedBy).IsRequired();
        builder.Property(r => r.ModifiedBy).IsRequired(false);
        builder.Property(r => r.CreatedAt).IsRequired();
        builder.Property(r => r.UpdatedAt).IsRequired(false);
        // Sentinel = true: a row is never inserted already-deleted (soft-delete happens via Update),
        // so marking true as the "unset" value makes EF always emit the explicit false on INSERT.
        builder.Property(r => r.IsDeleted).IsRequired().HasDefaultValueSql("0").HasSentinel(true);

        // Soft-delete global query filter (mirrors other Identity entities)
        builder.HasQueryFilter(r => !r.IsDeleted);

        // Composite lookup — most common query path: load by tenant + doc
        builder.HasIndex(r => new { r.TenantId, r.DocId, r.Status })
            .HasDatabaseName("IX_SigningDocFlowRecord_TenantId_DocId_Status");

        // Filtered active-only unique index: prevents two concurrent active (non-superseded, non-terminal)
        // records for the same (TenantId, DocId, SignerSequenceNumber). Uniqueness is restricted to rows
        // that are not soft-deleted AND in a non-terminal status — the active set is enumerated explicitly
        // (Initiated=0, Downloaded=1) because SQL Server filtered-index predicates do not accept NOT IN;
        // terminal statuses (Completed=2, Failed=3, Expired=4) are therefore outside the constraint.
        // This allows a fresh attempt at sequence N after the prior record is superseded/terminal —
        // i.e. a retry-after-JWT-expiry no longer collides. Two concurrent ACTIVE inits at the same
        // sequence still produce a unique-violation (SQL error 2601/2627); the loser catches
        // DbUpdateException and returns a typed conflict result (→ 409), never a 500.
        builder.HasIndex(r => new { r.TenantId, r.DocId, r.SignerSequenceNumber })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0 AND [Status] IN (0, 1)")
            .HasDatabaseName("UX_SigningDocFlowRecord_TenantId_DocId_SequenceNumber");

        // Nonce lookup — unique: one nonce per doc-flow record
        builder.HasIndex(r => r.JwtNonce)
            .IsUnique()
            .HasDatabaseName("IX_SigningDocFlowRecord_JwtNonce");

        // Expiry sweep
        builder.HasIndex(r => r.Status)
            .HasDatabaseName("IX_SigningDocFlowRecord_Status");
    }
}
