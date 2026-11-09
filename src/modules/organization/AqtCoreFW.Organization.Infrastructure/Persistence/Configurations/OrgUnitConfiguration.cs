using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Organization.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Organization.Infrastructure.Persistence.Configurations;

public sealed class OrgUnitConfiguration : EntityTypeConfigurationBase<OrgUnit>
{
    protected override void ConfigureEntity(EntityTypeBuilder<OrgUnit> e)
    {
        e.ToTable("OrgUnits");
        e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        e.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        e.Property(x => x.Code).HasMaxLength(50).IsRequired();
        e.Property(x => x.Level).IsRequired();
        e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        e.HasIndex(x => new { x.TenantId, x.ParentId });
        e.HasOne<OrgUnit>()
            .WithMany()
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
