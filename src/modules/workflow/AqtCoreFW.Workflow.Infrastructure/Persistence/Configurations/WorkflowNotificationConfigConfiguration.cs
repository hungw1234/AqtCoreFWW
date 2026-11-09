using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Workflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF config for WorkflowNotificationConfig.
/// Indexed for efficient lookup by DefinitionId + ActionName + IsActive (used by event handler).
/// </summary>
public sealed class WorkflowNotificationConfigConfiguration
    : EntityTypeConfigurationBase<WorkflowNotificationConfig>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WorkflowNotificationConfig> builder)
    {
        builder.ToTable("WorkflowNotificationConfigs");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.DefinitionId).IsRequired();
        builder.Property(c => c.ActionName).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Channel).HasMaxLength(50).IsRequired();
        builder.Property(c => c.RecipientType).HasMaxLength(50).IsRequired();
        builder.Property(c => c.RecipientValue).HasMaxLength(256).IsRequired();
        builder.Property(c => c.Subject).HasMaxLength(500).IsRequired();
        builder.Property(c => c.BodyTemplate).HasMaxLength(4000).IsRequired();
        builder.Property(c => c.IsActive).IsRequired();
        builder.Property(c => c.TenantId).IsRequired();

        // Event handler hot path: match by definition + action + active status
        builder.HasIndex(c => new { c.DefinitionId, c.ActionName, c.IsActive })
            .HasDatabaseName("IX_NotificationConfigs_DefId_Action_Active");
    }
}
