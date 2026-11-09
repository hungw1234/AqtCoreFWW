using AqtCoreFW.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for SigningDocFlowNonce ([identity] schema, W3 QĐ 742 mục 5.1).
///
/// Standalone configuration — SigningDocFlowNonce is a replay-prevention POCO with a Guid PK,
/// not a business entity extending AuditableEntity/Entity (which force bigint Id).
///
/// No soft-delete: nonces are cleaned up by background sweep (delete WHERE ExpiresAt &lt; now - 1d).
/// RowVersion (optimistic concurrency): the unique PK only stops duplicate INSERTs — it does NOT
/// serialise concurrent read-modify-write of UseCount. Parallel uploads with the same JWT would all
/// pass the in-memory TryUse() gate and double-spend the nonce. The rowversion token makes the
/// consume atomic: the first SaveChanges wins, concurrent ones throw DbUpdateConcurrencyException.
/// FK to SigningDocFlowRecords.Id — cascade delete so nonces are removed when the parent record is hard-deleted.
/// </summary>
public sealed class SigningDocFlowNonceConfiguration : IEntityTypeConfiguration<SigningDocFlowNonce>
{
    public void Configure(EntityTypeBuilder<SigningDocFlowNonce> builder)
    {
        builder.ToTable("SigningDocFlowNonces");

        builder.HasKey(n => n.Nonce);

        builder.Property(n => n.Nonce)
            .ValueGeneratedNever(); // Client-generated Guid — no DB default

        builder.Property(n => n.TenantId)
            .IsRequired();

        builder.Property(n => n.DocFlowRecordId)
            .IsRequired();

        builder.Property(n => n.UseCount)
            .IsRequired()
            .HasDefaultValueSql("0");

        builder.Property(n => n.MaxUses)
            .IsRequired()
            .HasDefaultValueSql("2");

        builder.Property(n => n.ExpiresAt)
            .IsRequired();

        // Optimistic concurrency — maps to SQL Server rowversion (8-byte auto-updated timestamp).
        // Serialises concurrent nonce consumes (anti-replay). Mirrors SigningPendingTransaction.
        builder.Property(n => n.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        // FK: cascade delete when parent SigningDocFlowRecord is hard-deleted
        builder.HasOne<SigningDocFlowRecord>()
            .WithOne()
            .HasForeignKey<SigningDocFlowNonce>(n => n.DocFlowRecordId)
            .OnDelete(DeleteBehavior.Cascade);

        // Fast expiry sweep index — background job: DELETE WHERE ExpiresAt < DATEADD(day,-1,GETUTCDATE())
        builder.HasIndex(n => n.ExpiresAt)
            .HasDatabaseName("IX_SigningDocFlowNonce_ExpiresAt");
    }
}
