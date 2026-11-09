using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Workflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Configurations;

/// <summary>EF config for WorkflowState — child of WorkflowDefinition.</summary>
public sealed class WorkflowStateConfiguration : EntityTypeConfigurationBase<WorkflowState>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WorkflowState> builder)
    {
        builder.ToTable("WorkflowStates");

        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.Property(s => s.DisplayNameVi).HasMaxLength(200).IsRequired();
        builder.Property(s => s.DisplayNameEn).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Color).HasMaxLength(10).IsRequired();
        builder.Property(s => s.SortOrder).IsRequired();
        builder.Property(s => s.IsInitial).IsRequired();
        builder.Property(s => s.IsFinal).IsRequired();
        builder.Property(s => s.SlaHours);                     // nullable int — per-state SLA
        builder.Property(s => s.AutoTransitionOnTimeoutId);    // nullable Guid — auto-transition target

        // Unique: only one initial state per definition enforced at domain level;
        // index aids fast lookup for CreateWorkflowInstance
        builder.HasIndex(s => new { s.DefinitionId, s.Name })
            .IsUnique()
            .HasDatabaseName("UX_WorkflowStates_DefinitionId_Name");

        builder.HasIndex(s => new { s.DefinitionId, s.IsInitial })
            .HasDatabaseName("IX_WorkflowStates_DefinitionId_IsInitial");
    }
}
