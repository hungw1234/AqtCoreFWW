using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Identity.Application.Commands.UpdateExternalIdentity;

[AuditCategory(AuditCategory.AccountManagement)]
public sealed record UpdateExternalIdentityCommand(
    long Id,
    string? DisplayName,
    string? Email,
    string? Metadata,
    Guid ActorId,
    // Tenant scope (JWT-derived, set by the controller — never from the request body).
    // IsPrivileged == SystemAdmin: the sole role allowed to mutate identities across tenants.
    Guid? CallerTenantId,
    bool IsPrivileged) : ICommand;
