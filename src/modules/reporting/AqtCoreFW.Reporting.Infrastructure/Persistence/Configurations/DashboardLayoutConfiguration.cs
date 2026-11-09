using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Reporting.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Reporting.Infrastructure.Persistence.Configurations;

public sealed class DashboardLayoutConfiguration : EntityTypeConfigurationBase<DashboardLayout>
{
    protected override void ConfigureEntity(EntityTypeBuilder<DashboardLayout> builder)
    {
        builder.ToTable("DashboardLayouts");

        builder.Property(d => d.Name).HasMaxLength(200).IsRequired();
        builder.Property(d => d.OwnerId).IsRequired();
        builder.Property(d => d.TenantId).IsRequired();
        builder.Property(d => d.IsDefault).IsRequired();

        // WidgetPositions is a JSON column — can be large for complex dashboards
        builder.Property(d => d.WidgetPositions).HasMaxLength(8000).IsRequired();

        // Queries by owner within a tenant
        builder.HasIndex(d => new { d.OwnerId, d.TenantId });
        // Unique: layout name per owner
        builder.HasIndex(d => new { d.Name, d.OwnerId }).IsUnique();
    }
}
