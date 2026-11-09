using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Workflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF config for EscalationRule.
/// Active rules indexed for efficient escalation job polling by definition and state.
/// EscalateToValue is a free-form string (role name or userId) — capped at 256 chars.
/// </summary>
public sealed class EscalationRuleConfiguration
    : EntityTypeConfigurationBase<EscalationRule>
{
    protected override void ConfigureEntity(EntityTypeBuilder<EscalationRule> builder)
    {
        builder.ToTable("EscalationRules");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.WorkflowDefinitionId).IsRequired();
        builder.Property(r => r.EscalationAfterMinutes).IsRequired();
        builder.Property(r => r.EscalateTo).IsRequired();
        builder.Property(r => r.EscalateToValue).HasMaxLength(256).IsRequired();
        builder.Property(r => r.NotifyVia).HasMaxLength(256).IsRequired();
        builder.Property(r => r.IsActive).IsRequired();
        builder.Property(r => r.TenantId).IsRequired();

        // Escalation job: load active rules per definition
        builder.HasIndex(r => new { r.WorkflowDefinitionId, r.IsActive })
            .HasDatabaseName("IX_EscalationRules_DefinitionId_IsActive");
    }
}
