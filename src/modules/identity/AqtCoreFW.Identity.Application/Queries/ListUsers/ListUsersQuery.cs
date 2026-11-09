using AqtCoreFW.Identity.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Pagination;
using FluentResults;

namespace AqtCoreFW.Identity.Application.Queries.ListUsers;

public sealed record ListUsersQuery(
    Guid? TenantId,
    string? SearchTerm,
    string? DepartmentCode,
    bool? IsActive,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<UserDto>>;
