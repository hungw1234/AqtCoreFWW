using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.Workflow.Application.DTOs;

namespace AqtCoreFW.Workflow.Application.Commands.UpdateWorkflowDefinition;

/// <summary>
/// Updates the metadata (name, description) of an existing WorkflowDefinition.
/// Only the latest version can be updated directly; older versions must be cloned first.
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdateWorkflowDefinitionCommand(
    Guid Id,
    string Name,
    string Description,
    Guid TenantId,
    Guid ModifiedBy)
    : ICommand<WorkflowDefinitionDto>;
