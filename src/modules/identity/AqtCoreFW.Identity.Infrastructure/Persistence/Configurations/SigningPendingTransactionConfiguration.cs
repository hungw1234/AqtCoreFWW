using AqtCoreFW.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for SigningPendingTransaction ([identity] schema, W6 async-callback).
///
/// PK: string TransactionId (Viettel-assigned UUID string — not a Guid).
/// No audit base class — plain entity with ITenantScoped.
///
/// Indexes:
///   IX_SigningPendingTransaction_TransactionId — unique (is PK, but explicit for clarity).
///   IX_SigningPendingTransaction_TenantId_UserId — per-user pending lookup.
///   IX_SigningPendingTransaction_Status_ExpiresAt — cleanup job query.
/// </summary>
public sealed class SigningPendingTransactionConfiguration
    : IEntityTypeConfiguration<SigningPendingTransaction>
{
    public void Configure(EntityTypeBuilder<SigningPendingTransaction> builder)
    {
        builder.ToTable("SigningPendingTransactions");

        builder.HasKey(t => t.TransactionId);

        builder.Property(t => t.TransactionId)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(t => t.TenantId)
            .IsRequired();

        builder.Property(t => t.UserId)
            .IsRequired();

        builder.Property(t => t.RequestId)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(t => t.DocumentId)
            .HasMaxLength(256)
            .IsRequired(false);

        builder.Property(t => t.InitiatedAt)
            .IsRequired();

        builder.Property(t => t.ExpiresAt)
            .IsRequired();

        builder.Property(t => t.Status)
            .HasConversion<int>()
            .IsRequired()
            .HasDefaultValueSql("0") // Initiated
            // Sentinel = a value the enum never holds, so EF always emits the explicit status on
            // INSERT instead of treating Initiated (the CLR-default 0) as "unset".
            .HasSentinel((PendingTransactionStatus)(-1));

        // Signature bytes: max ~8KB for an RSA-4096 raw signature
        builder.Property(t => t.Signature)
            .HasColumnType("varbinary(8000)")
            .IsRequired(false);

        builder.Property(t => t.FailureReason)
            .HasMaxLength(1024)
            .IsRequired(false);

        builder.Property(t => t.CompletedAt)
            .IsRequired(false);

        // ── PAdES async-poll resume state (nullable — only set for server-side PAdES transactions) ──
        builder.Property(t => t.PadesPlaceholderStorageKey)
            .HasMaxLength(512)
            .IsRequired(false);

        builder.Property(t => t.PadesContentsGapStart)
            .IsRequired(false);

        builder.Property(t => t.PadesContentsHexLength)
            .IsRequired(false);

        // signedAttributes DER is small (~hundreds of bytes); 4000 leaves generous headroom.
        builder.Property(t => t.PadesSignedAttributesDer)
            .HasColumnType("varbinary(4000)")
            .IsRequired(false);

        // A signer leaf cert DER is ~1.5–2 KB; 8000 is the inline varbinary ceiling before MAX.
        builder.Property(t => t.PadesSignerCertDer)
            .HasColumnType("varbinary(8000)")
            .IsRequired(false);

        builder.Property(t => t.PadesSignedStorageKey)
            .HasMaxLength(512)
            .IsRequired(false);

        // Optimistic concurrency — maps to SQL Server rowversion (8-byte auto-updated timestamp).
        builder.Property(t => t.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        // Index: per-user pending query (e.g. list pending for a user)
        builder.HasIndex(t => new { t.TenantId, t.UserId })
            .HasDatabaseName("IX_SigningPendingTransaction_TenantId_UserId");

        // Index: cleanup job — queries Status=Initiated + ExpiresAt < threshold
        builder.HasIndex(t => new { t.Status, t.ExpiresAt })
            .HasDatabaseName("IX_SigningPendingTransaction_Status_ExpiresAt");
    }
}
