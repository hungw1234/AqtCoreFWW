using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Rules.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Rules.Infrastructure.Persistence.Configurations;

public sealed class RuleConfiguration : EntityTypeConfigurationBase<Rule>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Rule> builder)
    {
        builder.ToTable("Rules");

        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Expression).HasMaxLength(4000).IsRequired();
        builder.Property(r => r.ErrorMessage).HasMaxLength(500);

        builder.Property(r => r.FailureAction)
            .HasConversion<string>()
            .HasMaxLength(20).IsRequired();

        builder.HasIndex(r => new { r.RuleSetId, r.Priority });
        builder.HasIndex(r => r.Enabled);
    }
}
