using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Workflow.Application.Commands.DeleteWorkflowDefinition;

/// <summary>
/// Soft-deletes a WorkflowDefinition. Fails if active (non-completed) instances exist.
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record DeleteWorkflowDefinitionCommand(
    Guid Id,
    Guid TenantId,
    Guid DeletedBy)
    : ICommand;
