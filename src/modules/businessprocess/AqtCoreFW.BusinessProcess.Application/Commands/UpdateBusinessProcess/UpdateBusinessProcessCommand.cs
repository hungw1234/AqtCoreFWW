using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.BusinessProcess.Application.Commands.UpdateBusinessProcess;

/// <summary>
/// Update mutable metadata (Name, Description) of an existing BusinessProcessDefinition.
/// Slice 2A-Write scope: metadata only. State/transition form-map mutations defer to a later slice
/// (Admin UI P7 will surface them once the cross-module Forms/Workflow client contracts ship).
/// Null Name preserves existing; null Description leaves it alone; whitespace Name is a no-op.
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdateBusinessProcessCommand(
    Guid TenantId,
    Guid BusinessProcessPublicId,
    string? Name,
    string? Description,
    Guid ModifiedBy)
    : ICommand;
