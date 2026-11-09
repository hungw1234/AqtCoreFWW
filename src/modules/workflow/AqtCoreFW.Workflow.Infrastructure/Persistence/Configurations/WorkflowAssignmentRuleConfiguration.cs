using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Workflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF config for WorkflowAssignmentRule.
/// Composite index on resolution lookup columns with INCLUDE for covering index —
/// avoids key-lookup for the common ResolveAsync query path.
/// </summary>
public sealed class WorkflowAssignmentRuleConfiguration
    : EntityTypeConfigurationBase<WorkflowAssignmentRule>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WorkflowAssignmentRule> builder)
    {
        builder.ToTable("WorkflowAssignmentRules");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.TenantId).IsRequired();
        builder.Property(r => r.WorkflowDefinitionId).IsRequired();
        builder.Property(r => r.EntityType).HasMaxLength(200);
        builder.Property(r => r.EntitySubType).HasMaxLength(200);
        builder.Property(r => r.Specificity).IsRequired();
        builder.Property(r => r.Priority).IsRequired().HasDefaultValue(0);
        builder.Property(r => r.IsActive).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(500);
        builder.Property(r => r.EffectiveFrom).IsRequired();
        builder.Property(r => r.EffectiveUntil);

        // Covering index for the ResolveAsync Dapper query:
        // WHERE TenantId = @t AND (EntityType = @e OR EntityType IS NULL)
        //   AND (EntitySubType = @s OR EntitySubType IS NULL) AND IsActive = 1
        // ORDER BY Specificity DESC, Priority DESC
        builder.HasIndex(r => new { r.TenantId, r.EntityType, r.EntitySubType, r.IsActive })
            .HasDatabaseName("IX_WorkflowAssignmentRules_TenantId_EntityType_SubType_IsActive")
            .IncludeProperties(r => new
            {
                r.WorkflowDefinitionId,
                r.Specificity,
                r.Priority,
                r.EffectiveFrom,
                r.EffectiveUntil
            });
    }
}
