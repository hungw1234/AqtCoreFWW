using AqtCoreFW.Identity.Domain.Models;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Identity.Application.Queries.GetUserEffectivePermissions;

/// <summary>
/// Returns the full effective permission summary for a user:
/// direct roles + group-inherited roles + active delegations + resolved data scope.
/// Result is Redis-cached (TTL 10 min). Use to power the admin "effective permission viewer".
/// </summary>
public sealed record GetUserEffectivePermissionsQuery(Guid UserId)
    : IQuery<EffectivePermissionSummary>;
