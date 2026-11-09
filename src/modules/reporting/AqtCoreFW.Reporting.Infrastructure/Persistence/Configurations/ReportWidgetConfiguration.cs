using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Reporting.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Reporting.Infrastructure.Persistence.Configurations;

public sealed class ReportWidgetConfiguration : EntityTypeConfigurationBase<ReportWidget>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ReportWidget> builder)
    {
        builder.ToTable("ReportWidgets");

        builder.Property(w => w.Name).HasMaxLength(200).IsRequired();
        builder.Property(w => w.DataQuery).HasMaxLength(8000).IsRequired();
        builder.Property(w => w.Config).HasMaxLength(4000);
        builder.Property(w => w.RefreshSeconds).IsRequired();
        builder.Property(w => w.TenantId).IsRequired();

        builder.Property(w => w.Type)
            .HasConversion<string>()
            .HasMaxLength(20).IsRequired();

        // Base provides (TenantId, Id) + (TenantId, CreatedAt) for ITenantScoped — manual TenantId index dropped.
        // Unique: widget name per tenant
        builder.HasIndex(w => new { w.Name, w.TenantId }).IsUnique();
    }
}
