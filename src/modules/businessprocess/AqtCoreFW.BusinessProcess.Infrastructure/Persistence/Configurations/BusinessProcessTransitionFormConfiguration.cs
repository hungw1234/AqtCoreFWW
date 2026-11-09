using AqtCoreFW.BusinessProcess.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.BusinessProcess.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping for BusinessProcessTransitionForm (child of BP). Unique (BpId, TransitionId, FormId).
/// </summary>
public sealed class BusinessProcessTransitionFormConfiguration
    : EntityTypeConfigurationBase<BusinessProcessTransitionForm>
{
    protected override void ConfigureEntity(EntityTypeBuilder<BusinessProcessTransitionForm> builder)
    {
        builder.ToTable("BusinessProcessTransitionForms");

        builder.Property(tf => tf.TenantId).IsRequired();
        builder.Property(tf => tf.BusinessProcessDefinitionId).IsRequired();
        builder.Property(tf => tf.WorkflowTransitionId).IsRequired();
        builder.Property(tf => tf.FormTemplateId).IsRequired();
        builder.Property(tf => tf.FormTemplateVersion).IsRequired();
        builder.Property(tf => tf.IsRequired).IsRequired();
        builder.Property(tf => tf.DisplayOrder).IsRequired();

        builder.HasIndex(tf => new { tf.BusinessProcessDefinitionId, tf.WorkflowTransitionId, tf.FormTemplateId })
            .IsUnique()
            .HasDatabaseName("UX_BusinessProcessTransitionForms_Bp_Transition_Form");

        builder.HasIndex(tf => new { tf.BusinessProcessDefinitionId, tf.WorkflowTransitionId, tf.DisplayOrder })
            .HasDatabaseName("IX_BusinessProcessTransitionForms_Bp_Transition_DisplayOrder");
    }
}
