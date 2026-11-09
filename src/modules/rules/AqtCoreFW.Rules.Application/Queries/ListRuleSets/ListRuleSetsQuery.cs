using AqtCoreFW.Rules.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Pagination;

namespace AqtCoreFW.Rules.Application.Queries.ListRuleSets;

public sealed record ListRuleSetsQuery(
    Guid TenantId,
    string? Search = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<RuleSetListItemDto>>;
