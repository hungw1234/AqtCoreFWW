using AqtCoreFW.MasterData.Application.DTOs;
using AqtCoreFW.MasterData.Domain.Entities;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Pagination;

namespace AqtCoreFW.MasterData.Application.Queries.GetDictionaries;

public sealed record GetDictionariesQuery(
    Guid TenantId,
    DictionaryStatus? Status = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<DictionaryDto>>;
