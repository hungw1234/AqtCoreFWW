using AqtCoreFW.Identity.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Pagination;
using FluentResults;

namespace AqtCoreFW.Identity.Application.Queries.ListActiveSessions;

/// <summary>Returns active token metadata for session management admin view, paginated.</summary>
public sealed record ListActiveSessionsQuery(
    Guid? UserId,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<ActiveSessionDto>>;
