using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Identity.Application.Commands.CreateExternalIdentity;

[AuditCategory(AuditCategory.AccountManagement)]
public sealed record CreateExternalIdentityCommand(
    Guid UserId,
    ExternalIdentityProvider Provider,
    string ExternalId,
    string? DisplayName,
    string? Email,
    Guid ActorId,
    // Tenant scope (JWT-derived, set by the controller — never from the request body).
    // IsPrivileged == SystemAdmin: the sole role allowed to link identities across tenants.
    Guid? CallerTenantId,
    bool IsPrivileged) : ICommand<long>;
