using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.BusinessProcess.Domain.Entities;

/// <summary>
/// Binds a FormTemplate (at a pinned version) to a WorkflowState — drives which form is shown to the user
/// while the case sits in that state. Child of BusinessProcessDefinition (cascade delete from root).
///
/// TenantId is duplicated from the parent root for RLS defense-in-depth (per Forms P1 pattern) — same value
/// is enforced by repo Pattern A guard at write time + RLS policy at DB level.
/// </summary>
public sealed class BusinessProcessStateForm : AuditableEntity, ITenantScoped, IPublicIdentified
{
    public Guid PublicId { get; private set; }
    public Guid TenantId { get; private set; }

    /// <summary>FK to BusinessProcessDefinition.Id (internal bigint PK).</summary>
    public long BusinessProcessDefinitionId { get; private set; }

    /// <summary>FK to Workflow.WorkflowState.Id (internal bigint PK).</summary>
    public long WorkflowStateId { get; private set; }

    /// <summary>FK to Forms.FormTemplate.Id (internal bigint PK).</summary>
    public long FormTemplateId { get; private set; }

    /// <summary>Pinned form version — paired with FormTemplateId in (FormTemplateId, VersionNumber) snapshot.</summary>
    public int FormTemplateVersion { get; private set; }

    public bool IsRequired { get; private set; }
    public int DisplayOrder { get; private set; }

    private BusinessProcessStateForm() { } // EF Core

    public static BusinessProcessStateForm Create(
        IGuidGenerator gen,
        Guid tenantId,
        long businessProcessDefinitionId,
        long workflowStateId,
        long formTemplateId,
        int formTemplateVersion,
        Guid createdBy,
        bool isRequired = false,
        int displayOrder = 0)
    {
        if (businessProcessDefinitionId <= 0) throw new ArgumentOutOfRangeException(nameof(businessProcessDefinitionId));
        if (workflowStateId <= 0) throw new ArgumentOutOfRangeException(nameof(workflowStateId));
        if (formTemplateId <= 0) throw new ArgumentOutOfRangeException(nameof(formTemplateId));
        if (formTemplateVersion < 1) throw new ArgumentOutOfRangeException(nameof(formTemplateVersion));

        var sf = new BusinessProcessStateForm
        {
            PublicId = gen.NewIdMonotonic(),
            TenantId = tenantId,
            BusinessProcessDefinitionId = businessProcessDefinitionId,
            WorkflowStateId = workflowStateId,
            FormTemplateId = formTemplateId,
            FormTemplateVersion = formTemplateVersion,
            IsRequired = isRequired,
            DisplayOrder = displayOrder
        };
        sf.SetAuditCreate(createdBy);
        return sf;
    }

    public void Update(bool? isRequired, int? displayOrder, Guid modifiedBy)
    {
        if (isRequired.HasValue) IsRequired = isRequired.Value;
        if (displayOrder.HasValue) DisplayOrder = displayOrder.Value;
        SetAuditUpdate(modifiedBy);
    }
}
