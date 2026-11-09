using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Pagination;
using AqtCoreFW.Integration.Application.DTOs;

namespace AqtCoreFW.Integration.Application.Queries.ListContracts;

/// <summary>PartnerPublicId is Guid? — T2 Partner external AK; handler resolves to internal FK for SQL.</summary>
public sealed record ListContractsQuery(
    Guid TenantId, Guid? PartnerPublicId = null, string? SearchTerm = null,
    int Page = 1, int PageSize = 20) : IQuery<PagedResult<ContractDto>>;
