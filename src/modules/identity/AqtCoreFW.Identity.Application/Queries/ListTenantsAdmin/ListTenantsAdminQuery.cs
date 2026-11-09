using AqtCoreFW.Identity.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Application.Pagination;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Queries.ListTenantsAdmin;

/// <summary>
/// Query to list tenants with pagination, search, and status filter.
/// For admin tenant management UI.
/// </summary>
public sealed record ListTenantsAdminQuery(
    string? Search,
    TenantStatus? Status,
    int Page = 1,
    int PageSize = 20) : IRequest<Result<PagedResult<TenantAdminDto>>>;
