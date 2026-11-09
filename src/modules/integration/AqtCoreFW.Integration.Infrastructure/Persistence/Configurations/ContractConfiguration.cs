using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Integration.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Integration.Infrastructure.Persistence.Configurations;

public sealed class ContractConfiguration : EntityTypeConfigurationBase<Contract>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Contract> builder)
    {
        builder.ToTable("Contracts");

        builder.Property(c => c.Title).HasMaxLength(300).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(2000);
        builder.Property(c => c.DataScopeJson).HasColumnType("nvarchar(max)");

        builder.Property(c => c.Status)
            .HasConversion<string>()
            .HasMaxLength(30).IsRequired();

        builder.HasIndex(c => new { c.TenantId, c.PartnerId });
        builder.HasIndex(c => new { c.TenantId, c.Status });

        // FK to Partner — restrict delete (cannot delete partner with contracts)
        builder.HasOne<Partner>()
            .WithMany()
            .HasForeignKey(c => c.PartnerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
