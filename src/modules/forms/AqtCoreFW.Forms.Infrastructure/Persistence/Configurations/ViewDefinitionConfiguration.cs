using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Forms.Infrastructure.Persistence.Configurations;

public sealed class ViewDefinitionConfiguration : EntityTypeConfigurationBase<ViewDefinition>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ViewDefinition> builder)
    {
        builder.ToTable("ViewDefinitions");

        builder.Property(v => v.Name).HasMaxLength(200).IsRequired();
        builder.Property(v => v.EntityType).HasMaxLength(200).IsRequired();

        builder.Property(v => v.Type)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(v => v.DefaultSortField).HasMaxLength(200).IsRequired(false);
        builder.Property(v => v.DefaultSortDir).HasMaxLength(4).IsRequired(false);
        builder.Property(v => v.PageSize).IsRequired();
        builder.Property(v => v.IsDefault).IsRequired();

        // Common query: find views by tenant+entityType, prefer default
        builder.HasIndex(v => new { v.TenantId, v.EntityType });
        builder.HasIndex(v => new { v.TenantId, v.IsDefault });

        // Columns navigation — owned by ViewDefinition
        builder.HasMany(v => v.Columns)
            .WithOne()
            .HasForeignKey(c => c.ViewDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        // Filters navigation — owned by ViewDefinition
        builder.HasMany(v => v.Filters)
            .WithOne()
            .HasForeignKey(f => f.ViewDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
