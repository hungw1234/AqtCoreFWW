using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.ValueObjects;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Identity.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for Tenant entity.
/// - Table: [identity].[Tenants]
/// - Code is unique (immutable post-create)
/// - RowVersion for optimistic concurrency
/// - Status stored as int
/// </summary>
public sealed class TenantConfiguration : EntityTypeConfigurationBase<Tenant>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");

        builder.Property(t => t.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(t => t.Code)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(t => t.ContactEmail)
            .HasMaxLength(200);

        builder.Property(t => t.Status)
            .HasConversion<int>()
            .IsRequired();

        // Optimistic concurrency token
        builder.Property(t => t.RowVersion)
            .IsRowVersion();

        // Unique index on Code — Code is immutable post-create
        builder.HasIndex(t => t.Code)
            .IsUnique();

        // Ignore DomainEvents navigation (not persisted)
        builder.Ignore(t => t.DomainEvents);
    }
}
