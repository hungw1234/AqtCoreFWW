using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Forms.Infrastructure.Persistence.Configurations;

public sealed class ViewFilterConfiguration : EntityTypeConfigurationBase<ViewFilter>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ViewFilter> builder)
    {
        builder.ToTable("ViewFilters");

        builder.Property(f => f.ViewDefinitionId).IsRequired();
        builder.Property(f => f.FieldName).HasMaxLength(200).IsRequired();

        builder.Property(f => f.Operator)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(f => f.DefaultValue).HasMaxLength(500).IsRequired(false);
        builder.Property(f => f.IsRequired).IsRequired();

        builder.HasIndex(f => f.ViewDefinitionId);
    }
}
