namespace AqtCoreFW.Workflow.Application.DTOs;

/// <summary>
/// Read model for WorkflowNotificationConfig — returned by queries and create command.
/// Phase-4b B4c: T1 entity — Id and DefinitionId are long (bigint identity PKs).
/// </summary>
public sealed record WorkflowNotificationConfigDto(
    long Id,
    long DefinitionId,
    string ActionName,
    string Channel,
    string RecipientType,
    string RecipientValue,
    string Subject,
    string BodyTemplate,
    bool IsActive);
