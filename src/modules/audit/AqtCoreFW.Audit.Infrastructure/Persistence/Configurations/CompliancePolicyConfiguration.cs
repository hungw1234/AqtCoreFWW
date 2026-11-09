using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Audit.Infrastructure.Persistence.Configurations;

public sealed class CompliancePolicyConfiguration : EntityTypeConfigurationBase<CompliancePolicy>
{
    protected override void ConfigureEntity(EntityTypeBuilder<CompliancePolicy> b)
    {
        b.ToTable("CompliancePolicies");

        b.Property(e => e.Name).HasMaxLength(256).IsRequired();
        b.Property(e => e.Rules).HasColumnType("nvarchar(max)").IsRequired();
        b.Property(e => e.Category).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(e => e.Enforcement).HasConversion<string>().HasMaxLength(32).IsRequired();

        b.HasIndex(e => e.IsEnabled);
        b.HasIndex(e => e.Category);
    }
}
