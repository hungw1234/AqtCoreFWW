using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Pagination;
using AqtCoreFW.Integration.Application.DTOs;

namespace AqtCoreFW.Integration.Application.Queries.ListPartners;

public sealed record ListPartnersQuery(
    Guid TenantId, string? SearchTerm = null,
    int Page = 1, int PageSize = 20) : IQuery<PagedResult<PartnerDto>>;
