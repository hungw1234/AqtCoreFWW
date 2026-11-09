using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.Workflow.Application.DTOs;

namespace AqtCoreFW.Workflow.Application.Commands.CloneWorkflowDefinition;

/// <summary>
/// Clones a WorkflowDefinition as a new version (Version+1).
/// The source definition is marked as no longer latest; the clone becomes the new latest.
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record CloneWorkflowDefinitionCommand(
    Guid SourceId,
    Guid TenantId,
    Guid CreatedBy)
    : ICommand<WorkflowDefinitionDto>;
