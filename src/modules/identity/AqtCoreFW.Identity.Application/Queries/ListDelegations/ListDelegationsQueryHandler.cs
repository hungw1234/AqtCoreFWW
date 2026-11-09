using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Queries.ListDelegations;

/// <summary>
/// Lists delegations with delegator/delegate display names enriched via IIdentityModuleClient
/// (Cross-Module Layer 2 — Display).
///
/// Authorization scope: a non-privileged caller sees only delegations they are a party to
/// (ActorId = delegator or delegate); an Admin/SystemAdmin may apply the optional DelegatorId/
/// DelegateId filters and sees a tenant-bound view. UserDelegations has no TenantId column, so the
/// tenant bind is via the parties' user rows (a delegation is in-tenant if either the delegator or the
/// delegate belongs to the caller's tenant; the NULL-tenant escape hatch mirrors the codebase RLS
/// convention for global rows). Soft-deleted rows are excluded. Pattern mirrors Files.ListFilesQueryHandler.
/// </summary>
public sealed class ListDelegationsQueryHandler(
    IReadDbConnection db,
    IIdentityModuleClient identityClient,
    ITenantContext tenantContext)
    : IRequestHandler<ListDelegationsQuery, Result<PagedResult<DelegationDto>>>
{
    public async Task<Result<PagedResult<DelegationDto>>> Handle(
        ListDelegationsQuery query, CancellationToken ct)
    {
        // Build safe parameterized SQL — no string interpolation
        var conditions = new List<string> { "d.IsDeleted = 0" };
        var p = new DynamicParameters();

        if (!query.IsPrivileged)
        {
            // Non-privileged caller: restrict to delegations the actor is a party to. Tenant-safe by
            // construction — a user can only ever see their own delegations. The client-supplied
            // DelegatorId/DelegateId filters are ignored here (they are an admin-only affordance).
            conditions.Add("(d.DelegatorId = @ActorId OR d.DelegateId = @ActorId)");
            p.Add("ActorId", query.ActorId);
        }
        else
        {
            // Admin/SystemAdmin: honour the optional explicit filters.
            if (query.DelegatorId.HasValue)
            {
                conditions.Add("d.DelegatorId = @DelegatorId");
                p.Add("DelegatorId", query.DelegatorId.Value);
            }

            if (query.DelegateId.HasValue)
            {
                conditions.Add("d.DelegateId = @DelegateId");
                p.Add("DelegateId", query.DelegateId.Value);
            }

            // Tenant-bind via the parties' user rows (no TenantId column on UserDelegations). A
            // delegation is in-tenant if either party belongs to the caller's tenant; NULL-tenant
            // (global) users are always visible, matching the RLS convention elsewhere. When no tenant
            // is in context (e.g. a SystemAdmin operating system-wide) the bind is skipped.
            if (tenantContext.TenantId.HasValue)
            {
                conditions.Add(
                    """
                    EXISTS (SELECT 1 FROM [identity].AspNetUsers u
                            WHERE u.Id IN (d.DelegatorId, d.DelegateId)
                              AND (u.TenantId = @TenantId OR u.TenantId IS NULL))
                    """);
                p.Add("TenantId", tenantContext.TenantId.Value);
            }
        }

        if (query.ActiveOnly == true)
        {
            // "Active" = the effective set the delegate actually inherits: status Active (not merely
            // not-revoked) AND inside the validity window. Excludes PendingApproval — an on-behalf
            // delegation awaiting approval is not yet effective, so it must not show in the active view.
            conditions.Add("d.Status = @ActiveStatus AND d.ValidFrom <= @Now AND d.ValidTo >= @Now");
            p.Add("ActiveStatus", (int)DelegationStatus.Active);
            p.Add("Now", DateTime.UtcNow);
        }

        var where = string.Join(" AND ", conditions);

        // M-05: server-side pagination with COUNT(*) OVER() for total
        var (page, pageSize) = PaginationDefaults.Normalize(
            query.Page, query.PageSize, PaginationDefaults.AdminMax);
        var offset = (page - 1) * pageSize;
        p.Add("Offset", offset);
        p.Add("PageSize", pageSize);

        // T2 contract: expose PublicId as external Guid Id (internal d.Id is bigint long).
        // Column convention: CreatedAt (no Utc suffix) — matches UserDelegations schema.
        var sql = $"""
            SELECT d.PublicId AS Id, d.DelegatorId, d.DelegateId, d.ValidFrom, d.ValidTo,
                   d.Reason, d.IsRevoked, CAST(NULL AS datetime2) AS RevokedAt, d.Status,
                   COUNT(*) OVER() AS TotalCount
            FROM [identity].UserDelegations d
            WHERE {where}
            ORDER BY d.CreatedAt DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var rows = (await db.QueryAsync<DelegationDtoWithTotal>(sql, p, ct)).ToList();
        var totalCount = rows.FirstOrDefault()?.TotalCount ?? 0;
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        // Cross-Module Layer 2 (Display): batch-resolve delegator + delegate names via Identity client.
        // Distinct() collapses dupes when same user appears as delegator/delegate across rows.
        var userIds = rows.SelectMany(r => new[] { r.DelegatorId, r.DelegateId }).Distinct();
        var userMap = await identityClient.GetUserInfoByIdsAsync(userIds, tenantContext.TenantId, ct);

        var items = rows.Select(r => new DelegationDto(
            r.Id, r.DelegatorId, r.DelegateId, r.ValidFrom, r.ValidTo,
            r.Reason, r.IsRevoked, r.RevokedAt, r.Status.ToString(),
            DelegatorName: userMap.TryGetValue(r.DelegatorId, out var d1) ? (d1.FullName ?? d1.Email) : null,
            DelegateName: userMap.TryGetValue(r.DelegateId, out var d2) ? (d2.FullName ?? d2.Email) : null))
            .ToList();

        return Result.Ok(new PagedResult<DelegationDto>(
            items,
            totalCount,
            new PaginationMeta(page, pageSize, totalPages, null, null, page < totalPages)));
    }

    // Internal DTO that includes TotalCount from COUNT(*) OVER()
    private sealed class DelegationDtoWithTotal
    {
        public Guid Id { get; init; }
        public Guid DelegatorId { get; init; }
        public Guid DelegateId { get; init; }
        public DateTime ValidFrom { get; init; }
        public DateTime ValidTo { get; init; }
        public string? Reason { get; init; }
        public bool IsRevoked { get; init; }
        public DateTime? RevokedAt { get; init; }
        public DelegationStatus Status { get; init; }
        public int TotalCount { get; init; }
    }
}
