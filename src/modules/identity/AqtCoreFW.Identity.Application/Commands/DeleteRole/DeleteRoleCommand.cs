using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Identity.Application.Commands.DeleteRole;

[AuditCategory(AuditCategory.AccountManagement)]
public sealed record DeleteRoleCommand(
    Guid RoleId,
    Guid ActorId,
    Guid TenantId) : ICommand;
