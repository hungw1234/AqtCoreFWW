using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Infrastructure.Alerting;

/// <summary>
/// EF config for AlertRule — metric-based alert rules evaluated by AlertEvaluationJob.
/// T1: AuditableEntity : Entity subclass — EntityTypeConfigurationBase applies universals:
///   bigint PK+Identity, datetime2(3) timestamps, IX_AlertRule_IsDeleted, HasQueryFilter(!IsDeleted), PageCompression.
/// AuditableEntity-specific columns (CreatedBy, ModifiedBy, ClassificationLevel) configured here
/// as they are not handled by EntityTypeConfigurationBase.
/// </summary>
public sealed class AlertRuleConfiguration : EntityTypeConfigurationBase<AlertRule>
{
    protected override void ConfigureEntity(EntityTypeBuilder<AlertRule> builder)
    {
        builder.ToTable("AlertRules");

        // Domain properties — string cols need explicit max lengths
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.MetricName).HasMaxLength(300).IsRequired();
        builder.Property(e => e.Condition).HasMaxLength(10).IsRequired();
        builder.Property(e => e.NotifyChannel).HasMaxLength(50).IsRequired();
        builder.Property(e => e.NotifyTarget).HasMaxLength(500);

        // AuditableEntity-specific columns — not handled by base
        builder.Property(e => e.CreatedBy).IsRequired();
        builder.Property(e => e.ModifiedBy);

        // Business indexes — preserved from original inline config
        builder.HasIndex(e => e.Enabled);
        builder.HasIndex(e => e.MetricName);
    }
}
