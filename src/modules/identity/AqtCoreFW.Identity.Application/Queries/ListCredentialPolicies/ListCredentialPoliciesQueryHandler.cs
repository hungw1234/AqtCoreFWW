using AqtCoreFW.Identity.Application.DTOs;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Queries.ListCredentialPolicies;

/// <summary>Read-side query via Dapper — follows ListUsersQueryHandler pattern.</summary>
public sealed class ListCredentialPoliciesQueryHandler(IReadDbConnection db)
    : IRequestHandler<ListCredentialPoliciesQuery, Result<PagedResult<CredentialPolicyDto>>>
{
    public async Task<Result<PagedResult<CredentialPolicyDto>>> Handle(
        ListCredentialPoliciesQuery query, CancellationToken ct)
    {
        var (page, pageSize) = PaginationDefaults.Normalize(query.Page, query.PageSize, PaginationDefaults.AdminMax);
        var offset = (page - 1) * pageSize;

        var sql = """
            SELECT p.Id, p.Name, p.TenantId,
                   p.MinLength, p.MaxLength,
                   p.RequireUppercase, p.RequireLowercase,
                   p.RequireDigit, p.RequireSpecialChar,
                   p.RotationDays, p.MaxFailedAttempts,
                   p.LockoutMinutes, p.PasswordHistoryCount, p.IsDefault,
                   p.EscalationLockoutCount, p.EscalationWindowHours, p.ShowRemainingAttempts,
                   COUNT(*) OVER() AS TotalCount
            FROM [identity].CredentialPolicies p
            WHERE p.TenantId = @TenantId
              AND p.IsDeleted = 0
            ORDER BY p.IsDefault DESC, p.Name ASC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var rows = await db.QueryAsync<CredentialPolicyRow>(sql, new
        {
            query.TenantId,
            Offset = offset,
            PageSize = pageSize
        });

        var list = rows.ToList();
        var total = list.Count > 0 ? list[0].TotalCount : 0;

        var dtos = list.Select(r => new CredentialPolicyDto(
            r.Id, r.Name, r.TenantId,
            r.MinLength, r.MaxLength,
            r.RequireUppercase, r.RequireLowercase,
            r.RequireDigit, r.RequireSpecialChar,
            r.RotationDays, r.MaxFailedAttempts,
            r.LockoutMinutes, r.PasswordHistoryCount,
            r.IsDefault,
            r.EscalationLockoutCount, r.EscalationWindowHours,
            r.ShowRemainingAttempts)).ToList();

        var totalPages = (int)Math.Ceiling((double)total / pageSize);
        var meta = new PaginationMeta(page, pageSize, totalPages, null, null, page < totalPages);
        return Result.Ok(new PagedResult<CredentialPolicyDto>(dtos, total, meta));
    }
}

internal sealed record CredentialPolicyRow(
    long Id, string Name, Guid TenantId,
    int MinLength, int MaxLength,
    bool RequireUppercase, bool RequireLowercase,
    bool RequireDigit, bool RequireSpecialChar,
    int RotationDays, int MaxFailedAttempts,
    int LockoutMinutes, int PasswordHistoryCount,
    bool IsDefault,
    int EscalationLockoutCount, int EscalationWindowHours,
    bool ShowRemainingAttempts, int TotalCount);
