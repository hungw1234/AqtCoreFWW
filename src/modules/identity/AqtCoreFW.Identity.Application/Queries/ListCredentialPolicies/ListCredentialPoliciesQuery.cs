using AqtCoreFW.Identity.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Pagination;

namespace AqtCoreFW.Identity.Application.Queries.ListCredentialPolicies;

public sealed record ListCredentialPoliciesQuery(
    Guid TenantId,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<CredentialPolicyDto>>;
