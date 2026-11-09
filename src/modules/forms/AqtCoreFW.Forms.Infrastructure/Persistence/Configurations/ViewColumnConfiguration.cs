using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Forms.Infrastructure.Persistence.Configurations;

public sealed class ViewColumnConfiguration : EntityTypeConfigurationBase<ViewColumn>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ViewColumn> builder)
    {
        builder.ToTable("ViewColumns");

        builder.Property(c => c.ViewDefinitionId).IsRequired();
        builder.Property(c => c.FieldName).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Label).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Width).IsRequired(false);
        builder.Property(c => c.Sortable).IsRequired();
        builder.Property(c => c.Filterable).IsRequired();
        builder.Property(c => c.DisplayOrder).IsRequired();

        builder.Property(c => c.Formatter)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // Fast ordering within a view
        builder.HasIndex(c => new { c.ViewDefinitionId, c.DisplayOrder });
    }
}
