using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Rules.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Rules.Infrastructure.Persistence.Configurations;

public sealed class RuleTestCaseConfiguration : EntityTypeConfigurationBase<RuleTestCase>
{
    protected override void ConfigureEntity(EntityTypeBuilder<RuleTestCase> builder)
    {
        builder.ToTable("RuleTestCases");

        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.InputData).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(t => t.ExpectedOutput).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(t => t.LastRunResult).HasColumnType("nvarchar(max)");

        builder.HasIndex(t => t.RuleSetId);
        builder.HasIndex(t => t.LastRunAt);
    }
}
