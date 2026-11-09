using AqtCoreFW.Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping for <see cref="SignerEnrollmentNonce"/>. Standalone configuration (the entity is a POCO,
/// not an auditable/soft-deletable aggregate). Tenant isolation is RLS-backed (policy added in the
/// migration via <c>RlsMigrationHelper</c>); the (TenantId, UserId) index speeds the scoped lookup and
/// the ExpiresAt index supports an eventual expiry sweep.
/// </summary>
public sealed class SignerEnrollmentNonceConfiguration : IEntityTypeConfiguration<SignerEnrollmentNonce>
{
    public void Configure(EntityTypeBuilder<SignerEnrollmentNonce> builder)
    {
        builder.ToTable("SignerEnrollmentNonces");

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever(); // server-generated Guid, not DB identity

        builder.Property(n => n.TenantId).IsRequired();
        builder.Property(n => n.UserId).IsRequired();
        builder.Property(n => n.Challenge).IsRequired().HasMaxLength(64);
        builder.Property(n => n.ExpiresAt).IsRequired();
        builder.Property(n => n.ConsumedAt);

        // Optimistic concurrency — serialises the single-use Consume transition (anti double-submit).
        builder.Property(n => n.RowVersion).IsRowVersion().IsConcurrencyToken();

        builder.HasIndex(n => new { n.TenantId, n.UserId })
            .HasDatabaseName("IX_SignerEnrollmentNonce_TenantId_UserId");
        builder.HasIndex(n => n.ExpiresAt)
            .HasDatabaseName("IX_SignerEnrollmentNonce_ExpiresAt");
    }
}
