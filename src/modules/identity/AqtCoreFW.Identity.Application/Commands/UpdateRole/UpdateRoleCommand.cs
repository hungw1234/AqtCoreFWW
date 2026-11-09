using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Identity.Application.Commands.UpdateRole;

[AuditCategory(AuditCategory.AccountManagement)]
public sealed record UpdateRoleCommand(
    Guid RoleId,
    string Name,
    string? Description,
    Guid ActorId,
    Guid TenantId) : ICommand;
