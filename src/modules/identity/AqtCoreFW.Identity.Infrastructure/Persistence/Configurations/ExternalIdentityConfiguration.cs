using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Configurations;

public sealed class ExternalIdentityConfiguration : EntityTypeConfigurationBase<ExternalIdentity>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ExternalIdentity> builder)
    {
        builder.Property(e => e.ExternalId).HasMaxLength(500).IsRequired();
        builder.Property(e => e.DisplayName).HasMaxLength(200);
        builder.Property(e => e.Email).HasMaxLength(254);
        builder.Property(e => e.Metadata).HasColumnType("nvarchar(max)");
        builder.Property(e => e.Provider).IsRequired();

        // One user can have at most one ACTIVE external identity per provider. The filter scopes
        // uniqueness to non-deleted rows so a soft-deleted (unlinked) link does not occupy the slot
        // — otherwise re-linking the same provider after an unlink fails with a duplicate-key error.
        builder.HasIndex(e => new { e.UserId, e.Provider }).IsUnique().HasFilter("[IsDeleted] = 0");
        // External ID unique within a provider — likewise scoped to active rows so an unlinked
        // external account can be re-linked later.
        builder.HasIndex(e => new { e.Provider, e.ExternalId }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(e => e.UserId);

        // Relationship: ExternalIdentity → ApplicationUser (many-to-one)
        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
