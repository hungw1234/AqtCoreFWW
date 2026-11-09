using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Reporting.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Reporting.Infrastructure.Persistence.Configurations;

public sealed class ReportDefinitionConfiguration : EntityTypeConfigurationBase<ReportDefinition>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ReportDefinition> builder)
    {
        builder.ToTable("ReportDefinitions");

        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.Property(r => r.NameVi).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(2000).IsRequired();
        builder.Property(r => r.SqlTemplate).HasMaxLength(8000).IsRequired();
        builder.Property(r => r.ParametersSchema).HasMaxLength(4000).IsRequired();
        builder.Property(r => r.TenantId).HasMaxLength(100);

        builder.Property(r => r.OutputFormat)
            .HasConversion<string>()
            .HasMaxLength(10).IsRequired();

        // Unique: report name per tenant (null tenant = system-wide)
        builder.HasIndex(r => new { r.Name, r.TenantId }).IsUnique();
        builder.HasIndex(r => r.IsActive);
    }
}
