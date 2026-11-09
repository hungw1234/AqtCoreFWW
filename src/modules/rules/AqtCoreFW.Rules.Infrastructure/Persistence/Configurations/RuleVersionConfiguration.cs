using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Rules.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Rules.Infrastructure.Persistence.Configurations;

public sealed class RuleVersionConfiguration : EntityTypeConfigurationBase<RuleVersion>
{
    protected override void ConfigureEntity(EntityTypeBuilder<RuleVersion> builder)
    {
        builder.ToTable("RuleVersions");

        builder.Property(v => v.Snapshot).HasColumnType("nvarchar(max)").IsRequired();

        builder.HasIndex(v => new { v.RuleSetId, v.VersionNumber }).IsUnique();
        builder.HasIndex(v => v.ActivatedAt);
    }
}
