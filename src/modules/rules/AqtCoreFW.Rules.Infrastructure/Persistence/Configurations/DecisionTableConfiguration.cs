using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Rules.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Rules.Infrastructure.Persistence.Configurations;

public sealed class DecisionTableConfiguration : EntityTypeConfigurationBase<DecisionTable>
{
    protected override void ConfigureEntity(EntityTypeBuilder<DecisionTable> builder)
    {
        builder.ToTable("DecisionTables");

        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        // JSON columns stored as nvarchar(max) — validated at application layer
        builder.Property(t => t.InputColumns).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(t => t.OutputColumns).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(t => t.Rows).HasColumnType("nvarchar(max)").IsRequired();

        builder.HasIndex(t => t.RuleSetId);
        builder.HasIndex(t => new { t.TenantId, t.RuleSetId });
    }
}
