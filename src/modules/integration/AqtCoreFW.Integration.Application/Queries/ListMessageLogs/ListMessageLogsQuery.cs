using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Pagination;
using AqtCoreFW.Integration.Application.DTOs;

namespace AqtCoreFW.Integration.Application.Queries.ListMessageLogs;

/// <summary>
/// PartnerPublicId is Guid? — T2 Partner external AK; handler resolves to internal FK for SQL.
/// ContractPublicId is Guid? — T2 Contract external AK; handler resolves to internal FK for SQL.
/// </summary>
public sealed record ListMessageLogsQuery(
    Guid TenantId, Guid? PartnerPublicId = null, Guid? ContractPublicId = null,
    string? SearchTerm = null, int Page = 1, int PageSize = 20) : IQuery<PagedResult<MessageLogDto>>;
