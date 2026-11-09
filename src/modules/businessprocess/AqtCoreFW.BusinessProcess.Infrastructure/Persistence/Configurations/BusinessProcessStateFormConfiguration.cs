using AqtCoreFW.BusinessProcess.Domain.Entities;
using AqtCoreFW.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AqtCoreFW.BusinessProcess.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping for BusinessProcessStateForm (child of BP). Unique (BpId, StateId, FormId) prevents duplicate bindings.
/// </summary>
public sealed class BusinessProcessStateFormConfiguration
    : EntityTypeConfigurationBase<BusinessProcessStateForm>
{
    protected override void ConfigureEntity(EntityTypeBuilder<BusinessProcessStateForm> builder)
    {
        builder.ToTable("BusinessProcessStateForms");

        builder.Property(sf => sf.TenantId).IsRequired();
        builder.Property(sf => sf.BusinessProcessDefinitionId).IsRequired();
        builder.Property(sf => sf.WorkflowStateId).IsRequired();
        builder.Property(sf => sf.FormTemplateId).IsRequired();
        builder.Property(sf => sf.FormTemplateVersion).IsRequired();
        builder.Property(sf => sf.IsRequired).IsRequired();
        builder.Property(sf => sf.DisplayOrder).IsRequired();

        builder.HasIndex(sf => new { sf.BusinessProcessDefinitionId, sf.WorkflowStateId, sf.FormTemplateId })
            .IsUnique()
            .HasDatabaseName("UX_BusinessProcessStateForms_Bp_State_Form");

        builder.HasIndex(sf => new { sf.BusinessProcessDefinitionId, sf.WorkflowStateId, sf.DisplayOrder })
            .HasDatabaseName("IX_BusinessProcessStateForms_Bp_State_DisplayOrder");
    }
}
