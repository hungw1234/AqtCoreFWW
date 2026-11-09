using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.Workflow.Application.DTOs;

namespace AqtCoreFW.Workflow.Application.Commands.CreateNotificationConfig;

/// <summary>
/// Creates a notification config for a workflow definition + action pair.
/// Phase-4b B6a: DefinitionId changed long→Guid (T2 WorkflowDefinition.PublicId);
/// handler resolves internal long FK via IWorkflowDefinitionRepository.GetByIdAsync.
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record CreateNotificationConfigCommand(
    Guid DefinitionId,
    string ActionName,
    string Channel,
    string RecipientType,
    string RecipientValue,
    string Subject,
    string BodyTemplate,
    Guid TenantId,
    Guid CreatedBy) : ICommand<WorkflowNotificationConfigDto>;
