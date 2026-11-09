using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Organization.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Organization.Infrastructure.Persistence.Configurations;

public sealed class StaffPositionHistoryConfiguration : EntityTypeConfigurationBase<StaffPositionHistory>
{
    protected override void ConfigureEntity(EntityTypeBuilder<StaffPositionHistory> e)
    {
        e.ToTable("StaffPositionHistories");
        e.Property(x => x.PositionTitle).HasMaxLength(200).IsRequired();
        e.HasIndex(x => new { x.TenantId, x.UserId });
        e.HasIndex(x => new { x.TenantId, x.OrgUnitId });
    }
}
