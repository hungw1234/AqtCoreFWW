using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Collaboration.Application.Commands.UpdatePresence;

[AuditCategory(AuditCategory.AppAccess)]
public sealed record UpdatePresenceCommand(
    Guid UserId,
    string Status,
    Guid TenantId) : ICommand;
