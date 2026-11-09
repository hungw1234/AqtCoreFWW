using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Workflow.Application.Commands.ToggleDefinitionActive;

/// <summary>Activates or deactivates a workflow definition.</summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record ToggleDefinitionActiveCommand(
    Guid DefinitionId,
    Guid TenantId,
    Guid ModifiedBy,
    bool IsActive) : ICommand;
