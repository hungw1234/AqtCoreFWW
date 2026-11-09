using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Integration.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Integration.Infrastructure.Persistence.Configurations;

public sealed class PartnerConfiguration : EntityTypeConfigurationBase<Partner>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Partner> builder)
    {
        builder.ToTable("Partners");

        // T2 AK — external addressing key; UNIQUE index mirrors IPublicIdentified contract.
        builder.Property(p => p.PublicId).IsRequired();
        builder.HasIndex(p => p.PublicId).IsUnique().HasDatabaseName("AK_Partner_PublicId");

        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Code).HasMaxLength(50).IsRequired();
        builder.Property(p => p.ContactEmail).HasMaxLength(256);
        builder.Property(p => p.ContactPhone).HasMaxLength(30);
        builder.Property(p => p.Endpoint).HasMaxLength(500);
        builder.Property(p => p.AuthScheme).HasMaxLength(100);

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(30).IsRequired();

        builder.HasIndex(p => new { p.TenantId, p.Status });
        builder.HasIndex(p => new { p.TenantId, p.Code }).IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.Ignore(p => p.DomainEvents);
    }
}
