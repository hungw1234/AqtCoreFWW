using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Identity.Application.Commands.ActivateRole;

[AuditCategory(AuditCategory.AccountManagement)]
public sealed record ActivateRoleCommand(
    Guid RoleId,
    Guid ActorId,
    Guid TenantId) : ICommand;
