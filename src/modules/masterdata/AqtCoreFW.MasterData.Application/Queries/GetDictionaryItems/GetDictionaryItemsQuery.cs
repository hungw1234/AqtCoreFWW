using AqtCoreFW.MasterData.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Pagination;

namespace AqtCoreFW.MasterData.Application.Queries.GetDictionaryItems;

public sealed record GetDictionaryItemsQuery(
    Guid DictionaryId,
    bool ActiveOnly = true,
    int Page = 1,
    int PageSize = 100) : IQuery<PagedResult<DictionaryItemDto>>;
