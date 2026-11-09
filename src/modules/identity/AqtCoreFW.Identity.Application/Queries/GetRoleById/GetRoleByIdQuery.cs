using AqtCoreFW.Identity.Application.Queries.GetRoles;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Identity.Application.Queries.GetRoleById;

public sealed record GetRoleByIdQuery(Guid RoleId, Guid TenantId) : IQuery<RoleDefinitionDto>;
