using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Workflow.Domain.Entities;

/// <summary>
/// Defines when and how a workflow task escalates if not acted upon.
/// EscalationAfterMinutes: time-in-state threshold before escalation fires.
/// EscalateTo + EscalateToValue: target resolution (manager role, specific role, or userId).
/// NotifyVia: comma-separated channels (e.g. "Email,Push").
/// StateId nullable — null means rule applies to any state in the definition.
/// T1 entity: non-generic AuditableEntity (long Id DB-assigned).
///   WorkflowDefinitionId: long (FK to T2 WorkflowDefinition.Id).
///   StateId: long? (FK to T1 WorkflowState.Id).
///   TenantId: Guid (FK exception).
/// </summary>
public sealed class EscalationRule : AuditableEntity, ITenantScoped
{
    public long WorkflowDefinitionId { get; private set; }
    public long? StateId { get; private set; }
    public int EscalationAfterMinutes { get; private set; }
    public EscalationTarget EscalateTo { get; private set; }

    /// <summary>Role name or userId string depending on EscalateTo value.</summary>
    public string EscalateToValue { get; private set; } = string.Empty;
    public string NotifyVia { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public Guid TenantId { get; private set; }

    private EscalationRule() { } // EF Core

    public static EscalationRule Create(
        long workflowDefinitionId,
        long? stateId,
        int escalationAfterMinutes,
        EscalationTarget escalateTo,
        string escalateToValue,
        string notifyVia,
        Guid tenantId,
        Guid createdBy)
    {
        var rule = new EscalationRule
        {
            WorkflowDefinitionId = workflowDefinitionId,
            StateId = stateId,
            EscalationAfterMinutes = escalationAfterMinutes,
            EscalateTo = escalateTo,
            EscalateToValue = escalateToValue,
            NotifyVia = notifyVia,
            IsActive = true,
            TenantId = tenantId
        };
        rule.SetAuditCreate(createdBy);
        return rule;
    }

    public void Deactivate(Guid modifiedBy)
    {
        IsActive = false;
        SetAuditUpdate(modifiedBy);
    }

    public void Activate(Guid modifiedBy)
    {
        IsActive = true;
        SetAuditUpdate(modifiedBy);
    }
}

public enum EscalationTarget
{
    Manager = 0,
    Role = 1,
    SpecificUser = 2
}
