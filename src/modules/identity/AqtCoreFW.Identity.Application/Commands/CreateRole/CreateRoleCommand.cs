using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Identity.Application.Commands.CreateRole;

[AuditCategory(AuditCategory.AccountManagement)]
public sealed record CreateRoleCommand(
    string Name,
    string Code,
    string? Description,
    RoleType RoleType,
    Guid TenantId,
    Guid ActorId) : ICommand<Guid>;
