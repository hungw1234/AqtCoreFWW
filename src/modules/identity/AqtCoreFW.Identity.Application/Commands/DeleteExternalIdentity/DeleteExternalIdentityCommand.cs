using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Identity.Application.Commands.DeleteExternalIdentity;

[AuditCategory(AuditCategory.AccountManagement)]
public sealed record DeleteExternalIdentityCommand(
    long Id,
    Guid ActorId,
    // Tenant scope (JWT-derived, set by the controller — never from the request body).
    // IsPrivileged == SystemAdmin: the sole role allowed to remove identities across tenants.
    Guid? CallerTenantId,
    bool IsPrivileged) : ICommand;
