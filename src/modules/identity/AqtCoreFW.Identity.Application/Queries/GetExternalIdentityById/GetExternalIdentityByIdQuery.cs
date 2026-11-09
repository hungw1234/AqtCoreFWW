using AqtCoreFW.Identity.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Identity.Application.Queries.GetExternalIdentityById;

public sealed record GetExternalIdentityByIdQuery(
    long Id,
    Guid? CallerTenantId,
    bool IsPrivileged) : IQuery<ExternalIdentityDto>;
