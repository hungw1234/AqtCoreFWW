using AqtCoreFW.Identity.Application.DTOs;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Pagination;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Queries.ListUsers;

/// <summary>Read-side query via Dapper — bypasses UserManager for performance.</summary>
public sealed class ListUsersQueryHandler : IRequestHandler<ListUsersQuery, Result<PagedResult<UserDto>>>
{
    private readonly IReadDbConnection _db;
    private readonly IEncryptedFieldDecryptor _decryptor;

    public ListUsersQueryHandler(IReadDbConnection db, IEncryptedFieldDecryptor decryptor)
    {
        _db = db;
        _decryptor = decryptor;
    }

    public async Task<Result<PagedResult<UserDto>>> Handle(ListUsersQuery query, CancellationToken ct)
    {
        var (page, pageSize) = PaginationDefaults.Normalize(query.Page, query.PageSize, PaginationDefaults.AdminMax);
        var offset = (page - 1) * pageSize;

        var sql = """
            SELECT u.Id, u.FullName, u.Email, u.DepartmentCode, u.ClearanceLevel,
                   u.IsActive, u.TenantId, u.CreatedAtUtc, u.LastLoginAt, u.PasswordExpiresAt,
                   COUNT(*) OVER() AS TotalCount
            FROM [identity].AspNetUsers u
            WHERE (@TenantId IS NULL OR u.TenantId = @TenantId)
              AND (@IsActive IS NULL OR u.IsActive = @IsActive)
              AND (@DepartmentCode IS NULL OR u.DepartmentCode = @DepartmentCode)
              -- FullName-only substring search. Email is AES-GCM-encrypted at rest, so a plaintext
              -- LIKE against u.Email never matches (compares typed plaintext to ciphertext) — it was
              -- dead and only misled. Equality email lookups go through NormalizedEmailHash elsewhere.
              AND (@Search IS NULL OR u.FullName LIKE @Search ESCAPE '\')
            ORDER BY u.CreatedAtUtc DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var rows = await _db.QueryAsync<UserRow>(sql, new
        {
            TenantId = query.TenantId,
            IsActive = query.IsActive,
            DepartmentCode = query.DepartmentCode,
            Search = query.SearchTerm is not null ? $"%{query.SearchTerm.EscapeSqlLike()}%" : null,
            Offset = offset,
            PageSize = pageSize
        });

        var list = rows.ToList();
        var total = list.Count > 0 ? list[0].TotalCount : 0;

        // Batch-load roles for all users in one query (avoid N+1)
        var userIds = list.Select(r => r.Id).ToList();
        var roleMap = new Dictionary<Guid, List<string>>();
        if (userIds.Count > 0)
        {
            var rolesSql = """
                SELECT ur.UserId, r.Name
                FROM [identity].AspNetUserRoles ur
                JOIN [identity].AspNetRoles r ON ur.RoleId = r.Id
                WHERE ur.UserId IN @UserIds
                """;
            var roleRows = await _db.QueryAsync<UserRoleRow>(rolesSql, new { UserIds = userIds }, ct);
            foreach (var rr in roleRows)
            {
                if (!roleMap.ContainsKey(rr.UserId)) roleMap[rr.UserId] = [];
                roleMap[rr.UserId].Add(rr.Name);
            }
        }

        var dtos = list.Select(r => new UserDto(
            r.Id, r.FullName, _decryptor.Decrypt(r.Email) ?? string.Empty, r.DepartmentCode,
            r.ClearanceLevel, r.IsActive, r.TenantId,
            r.CreatedAtUtc, r.LastLoginAt, r.PasswordExpiresAt,
            roleMap.TryGetValue(r.Id, out var roles) ? roles : [])).ToList();

        var totalPages = (int)Math.Ceiling((double)total / pageSize);
        var meta = new PaginationMeta(page, pageSize, totalPages, null, null, page < totalPages);
        return Result.Ok(new PagedResult<UserDto>(dtos, total, meta));
    }

}

internal sealed record UserRoleRow(Guid UserId, string Name);

// internal so test project (via InternalsVisibleTo) can mock IReadDbConnection.QueryAsync<UserRow>
internal sealed record UserRow(
    Guid Id, string FullName, string Email, string? DepartmentCode,
    AqtCoreFW.SharedKernel.Domain.ClassificationLevel ClearanceLevel,
    bool IsActive, Guid? TenantId, DateTime CreatedAtUtc,
    DateTime? LastLoginAt, DateTime? PasswordExpiresAt, int TotalCount);
