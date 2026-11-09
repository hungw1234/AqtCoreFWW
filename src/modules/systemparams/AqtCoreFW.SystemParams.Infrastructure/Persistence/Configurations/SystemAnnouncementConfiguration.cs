using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.SystemParams.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.SystemParams.Infrastructure.Persistence.Configurations;

public sealed class SystemAnnouncementConfiguration : EntityTypeConfigurationBase<SystemAnnouncement>
{
    protected override void ConfigureEntity(EntityTypeBuilder<SystemAnnouncement> e)
    {
        e.ToTable("SystemAnnouncements");
        e.Property(x => x.Title).HasMaxLength(200).IsRequired();
        e.Property(x => x.Content).HasMaxLength(2000).IsRequired();
        e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.TargetRoles).HasMaxLength(500);
    }
}
