using AqtCoreFW.Identity.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Pagination;

namespace AqtCoreFW.Identity.Application.Queries.ListJitProviderConfigs;

public sealed record ListJitProviderConfigsQuery(
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<JitProviderConfigDto>>;
