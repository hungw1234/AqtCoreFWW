using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Workflow.Domain.Entities;

/// <summary>
/// Per-definition, per-action notification config.
/// When a transition with matching ActionName fires, send notification via configured channel.
/// T1 entity: non-generic AuditableEntity (long Id DB-assigned).
///   DefinitionId: long (FK to T2 WorkflowDefinition.Id).
///   TenantId: Guid (FK exception).
/// </summary>
public sealed class WorkflowNotificationConfig : AuditableEntity, ITenantScoped
{
    /// <summary>Workflow definition this config belongs to.</summary>
    public long DefinitionId { get; private set; }

    /// <summary>Transition action name that triggers the notification.</summary>
    public string ActionName { get; private set; } = string.Empty;

    /// <summary>Delivery channel: "email" | "sms" | "inapp".</summary>
    public string Channel { get; private set; } = string.Empty;

    /// <summary>How recipient is resolved: "role" | "user" | "creator".</summary>
    public string RecipientType { get; private set; } = string.Empty;

    /// <summary>Role code, user GUID string, or "creator" depending on RecipientType.</summary>
    public string RecipientValue { get; private set; } = string.Empty;

    /// <summary>Notification subject template.</summary>
    public string Subject { get; private set; } = string.Empty;

    /// <summary>Notification body template (Liquid-compatible).</summary>
    public string BodyTemplate { get; private set; } = string.Empty;

    public bool IsActive { get; private set; } = true;
    public Guid TenantId { get; private set; }

    private WorkflowNotificationConfig() { } // EF Core

    public static WorkflowNotificationConfig Create(
        long definitionId,
        string actionName,
        string channel,
        string recipientType,
        string recipientValue,
        string subject,
        string bodyTemplate,
        Guid tenantId,
        Guid createdBy)
    {
        var config = new WorkflowNotificationConfig
        {
            DefinitionId = definitionId,
            ActionName = actionName,
            Channel = channel,
            RecipientType = recipientType,
            RecipientValue = recipientValue,
            Subject = subject,
            BodyTemplate = bodyTemplate,
            IsActive = true,
            TenantId = tenantId
        };
        config.SetAuditCreate(createdBy);
        return config;
    }

    public void Deactivate(Guid modifiedBy)
    {
        IsActive = false;
        SetAuditUpdate(modifiedBy);
    }
}
