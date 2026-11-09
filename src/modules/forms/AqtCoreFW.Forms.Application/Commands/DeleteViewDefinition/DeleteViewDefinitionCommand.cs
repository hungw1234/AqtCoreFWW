using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Forms.Application.Commands.DeleteViewDefinition;

/// <summary>Soft-deletes a ViewDefinition by ID within the given tenant.</summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record DeleteViewDefinitionCommand(
    long Id,
    Guid TenantId) : ICommand;
