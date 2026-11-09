using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Reporting.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Reporting.Infrastructure.Persistence.Configurations;

public sealed class ReportExecutionConfiguration : EntityTypeConfigurationBase<ReportExecution>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ReportExecution> builder)
    {
        builder.ToTable("ReportExecutions");

        builder.Property(r => r.Parameters).HasMaxLength(4000).IsRequired();
        builder.Property(r => r.ResultFilePath).HasMaxLength(500);
        builder.Property(r => r.ErrorMessage).HasMaxLength(2000);

        builder.Property(r => r.Status)
            .HasConversion<string>()
            .HasMaxLength(20).IsRequired();

        // FK to ReportDefinition
        builder.HasOne<ReportDefinition>()
            .WithMany()
            .HasForeignKey(r => r.ReportDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes for common queries
        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.ReportDefinitionId);
        builder.HasIndex(r => r.RequestedBy);
    }
}
