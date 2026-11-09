using AqtCoreFW.MasterData.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Pagination;

namespace AqtCoreFW.MasterData.Application.Queries.GetExternalMappings;

public sealed record GetExternalMappingsQuery(
    Guid TenantId,
    string? ExternalSystem = null,
    string? InternalCode = null,
    bool ActiveOnly = true,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<ExternalMappingDto>>;
