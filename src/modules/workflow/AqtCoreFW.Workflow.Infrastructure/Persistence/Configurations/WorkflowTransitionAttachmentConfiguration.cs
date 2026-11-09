using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Workflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF config for WorkflowTransitionAttachment — files attached during a transition.
/// T1: Entity subclass — EntityTypeConfigurationBase applies universals:
///   bigint PK+Identity, datetime2(3) timestamps, IX_*_IsDeleted, HasQueryFilter(!IsDeleted), PageCompression.
/// </summary>
public sealed class WorkflowTransitionAttachmentConfiguration
    : EntityTypeConfigurationBase<WorkflowTransitionAttachment>
{
    protected override void ConfigureEntity(EntityTypeBuilder<WorkflowTransitionAttachment> builder)
    {
        builder.ToTable("WorkflowTransitionAttachments", "workflow");

        builder.Property(a => a.FileName).HasMaxLength(500).IsRequired();
        builder.Property(a => a.Description).HasMaxLength(1000);
        builder.Property(a => a.FileId).IsRequired();
        builder.Property(a => a.InstanceHistoryId).IsRequired();

        // Index for fast lookup of attachments by history entry
        builder.HasIndex(a => a.InstanceHistoryId)
            .HasDatabaseName("IX_WorkflowTransitionAttachments_InstanceHistoryId");
    }
}
