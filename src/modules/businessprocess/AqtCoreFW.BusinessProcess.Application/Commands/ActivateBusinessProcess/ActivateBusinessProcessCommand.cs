using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.BusinessProcess.Application.Commands.ActivateBusinessProcess;

/// <summary>
/// Flip a BusinessProcessDefinition's IsActive false→true. Aggregate-level idempotent: already-active
/// returns success with no event emitted (the only side effect that matters externally is the transition).
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record ActivateBusinessProcessCommand(
    Guid TenantId,
    Guid BusinessProcessPublicId,
    Guid ModifiedBy)
    : ICommand;
