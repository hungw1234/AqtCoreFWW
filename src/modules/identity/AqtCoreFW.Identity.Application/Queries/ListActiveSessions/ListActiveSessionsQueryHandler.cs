using AqtCoreFW.Identity.Application.DTOs;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application.Pagination;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Queries.ListActiveSessions;

public sealed class ListActiveSessionsQueryHandler
    : IRequestHandler<ListActiveSessionsQuery, Result<PagedResult<ActiveSessionDto>>>
{
    private readonly ISessionRepository _repo;

    public ListActiveSessionsQueryHandler(ISessionRepository repo) => _repo = repo;

    public async Task<Result<PagedResult<ActiveSessionDto>>> Handle(
        ListActiveSessionsQuery request,
        CancellationToken cancellationToken)
    {
        var (page, pageSize) = PaginationDefaults.Normalize(request.Page, request.PageSize, PaginationDefaults.AdminMax);
        var (tokens, totalCount) = await _repo.ListActiveAsync(
            request.UserId, page, pageSize, cancellationToken);

        var dtos = tokens
            .Select(t => new ActiveSessionDto(
                t.TokenId, t.UserId, t.UserEmail,
                t.IssuedAt, t.ExpiresAt, t.IpAddress, t.ClientId))
            .ToList();

        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
        var meta = new PaginationMeta(
            page, pageSize, totalPages,
            null, null, page < totalPages);

        return Result.Ok(new PagedResult<ActiveSessionDto>(dtos, totalCount, meta));
    }
}
