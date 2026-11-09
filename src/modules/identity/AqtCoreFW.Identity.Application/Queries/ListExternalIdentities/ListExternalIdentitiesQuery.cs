using AqtCoreFW.Identity.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Pagination;

namespace AqtCoreFW.Identity.Application.Queries.ListExternalIdentities;

public sealed record ListExternalIdentitiesQuery(
    Guid UserId,
    Guid? CallerTenantId,
    bool IsPrivileged,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<ExternalIdentityDto>>;
