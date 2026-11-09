using AqtCoreFW.Identity.Application.DTOs;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Queries.ListExternalIdentities;

/// <summary>Read-side query via Dapper — follows ListUsersQueryHandler pattern.</summary>
public sealed class ListExternalIdentitiesQueryHandler(IReadDbConnection db)
    : IRequestHandler<ListExternalIdentitiesQuery, Result<PagedResult<ExternalIdentityDto>>>
{
    public async Task<Result<PagedResult<ExternalIdentityDto>>> Handle(
        ListExternalIdentitiesQuery query, CancellationToken ct)
    {
        var (page, pageSize) = PaginationDefaults.Normalize(query.Page, query.PageSize, PaginationDefaults.AdminMax);
        var offset = (page - 1) * pageSize;

        // Tenant isolation: ExternalIdentities has no TenantId column and no RLS policy,
        // so a non-privileged caller is bound to links whose owning user lives in their tenant.
        // SystemAdmin (@IsPrivileged = 1) sees all tenants.
        var sql = """
            SELECT e.Id, e.UserId, e.Provider, e.ExternalId, e.DisplayName, e.Email,
                   e.LinkedAt, e.LastSyncAt, e.IsActive,
                   COUNT(*) OVER() AS TotalCount
            FROM [identity].ExternalIdentities e
            WHERE e.UserId = @UserId
              AND e.IsDeleted = 0
              AND (@IsPrivileged = 1 OR EXISTS (
                    SELECT 1 FROM [identity].AspNetUsers u
                    WHERE u.Id = e.UserId AND u.TenantId = @CallerTenantId))
            ORDER BY e.LinkedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var rows = await db.QueryAsync<ExternalIdentityRow>(sql, new
        {
            query.UserId,
            IsPrivileged = query.IsPrivileged ? 1 : 0,
            query.CallerTenantId,
            Offset = offset,
            PageSize = pageSize
        });

        var list = rows.ToList();
        var total = list.Count > 0 ? list[0].TotalCount : 0;

        var dtos = list.Select(r => new ExternalIdentityDto(
            r.Id, r.UserId, (ExternalIdentityProvider)r.Provider,
            r.ExternalId, r.DisplayName, r.Email,
            r.LinkedAt, r.LastSyncAt, r.IsActive)).ToList();

        var totalPages = (int)Math.Ceiling((double)total / pageSize);
        var meta = new PaginationMeta(page, pageSize, totalPages, null, null, page < totalPages);
        return Result.Ok(new PagedResult<ExternalIdentityDto>(dtos, total, meta));
    }
}

internal sealed record ExternalIdentityRow(
    long Id, Guid UserId, int Provider, string ExternalId,
    string? DisplayName, string? Email,
    DateTime LinkedAt, DateTime? LastSyncAt, bool IsActive, int TotalCount);
