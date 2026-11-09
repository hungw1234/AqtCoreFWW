using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.BusinessProcess.Domain.Entities;

/// <summary>
/// Binds a FormTemplate (at a pinned version) to a WorkflowTransition — drives which form must be filled out
/// when the user performs that transition. Child of BusinessProcessDefinition (cascade delete from root).
///
/// TenantId duplicated from parent root for RLS defense-in-depth (per Forms P1 pattern).
/// </summary>
public sealed class BusinessProcessTransitionForm : AuditableEntity, ITenantScoped, IPublicIdentified
{
    public Guid PublicId { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>FK to BusinessProcessDefinition.Id (internal bigint PK).</summary>
    public long BusinessProcessDefinitionId { get; private set; }

    /// <summary>FK to Workflow.WorkflowTransition.Id (internal bigint PK).</summary>
    public long WorkflowTransitionId { get; private set; }

    /// <summary>FK to Forms.FormTemplate.Id (internal bigint PK).</summary>
    public long FormTemplateId { get; private set; }

    /// <summary>Pinned form version.</summary>
    public int FormTemplateVersion { get; private set; }

    public bool IsRequired { get; private set; }
    public int DisplayOrder { get; private set; }

    private BusinessProcessTransitionForm() { } // EF Core

    public static BusinessProcessTransitionForm Create(
        IGuidGenerator gen,
        Guid tenantId,
        long businessProcessDefinitionId,
        long workflowTransitionId,
        long formTemplateId,
        int formTemplateVersion,
        Guid createdBy,
        bool isRequired = false,
        int displayOrder = 0)
    {
        if (businessProcessDefinitionId <= 0) throw new ArgumentOutOfRangeException(nameof(businessProcessDefinitionId));
        if (workflowTransitionId <= 0) throw new ArgumentOutOfRangeException(nameof(workflowTransitionId));
        if (formTemplateId <= 0) throw new ArgumentOutOfRangeException(nameof(formTemplateId));
        if (formTemplateVersion < 1) throw new ArgumentOutOfRangeException(nameof(formTemplateVersion));

        var tf = new BusinessProcessTransitionForm
        {
            PublicId = gen.NewIdMonotonic(),
            TenantId = tenantId,
            BusinessProcessDefinitionId = businessProcessDefinitionId,
            WorkflowTransitionId = workflowTransitionId,
            FormTemplateId = formTemplateId,
            FormTemplateVersion = formTemplateVersion,
            IsRequired = isRequired,
            DisplayOrder = displayOrder
        };
        tf.SetAuditCreate(createdBy);
        return tf;
    }

    public void Update(bool? isRequired, int? displayOrder, Guid modifiedBy)
    {
        if (isRequired.HasValue) IsRequired = isRequired.Value;
        if (displayOrder.HasValue) DisplayOrder = displayOrder.Value;
        SetAuditUpdate(modifiedBy);
    }
}
