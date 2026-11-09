using AqtCoreFW.BusinessProcess.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Pagination;
using FluentResults;
using MediatR;

namespace AqtCoreFW.BusinessProcess.Application.Queries.ListBusinessProcesses;

/// <summary>
/// Paged list of BusinessProcessDefinitions for the caller's tenant. SystemAdmin may target a different tenant.
/// IsActive=null returns both; IsActive=true / false filters accordingly.
/// </summary>
public sealed record ListBusinessProcessesQuery(
    Guid TenantId,
    string? Search,
    bool? IsActive,
    int Page,
    int PageSize)
    : IRequest<Result<PagedResult<BusinessProcessListItemDto>>>;
