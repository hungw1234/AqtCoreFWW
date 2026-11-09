using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.BusinessProcess.Application.Commands.DeactivateBusinessProcess;

/// <summary>
/// Flip a BusinessProcessDefinition's IsActive true→false. Aggregate-level idempotent: already-inactive
/// returns success with no event emitted. In-flight WorkflowInstances continue; only new BP-Run intake stops.
/// </summary>
[AuditCategory(AuditCategory.ConfigChange)]
public sealed record DeactivateBusinessProcessCommand(
    Guid TenantId,
    Guid BusinessProcessPublicId,
    Guid ModifiedBy)
    : ICommand;
