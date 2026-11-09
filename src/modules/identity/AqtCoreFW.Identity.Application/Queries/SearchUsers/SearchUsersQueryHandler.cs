using AqtCoreFW.SharedKernel.Application.Data;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Queries.SearchUsers;

/// <summary>
/// Searches users in tenant by FullName substring (LIKE wildcards escaped).
/// Excludes the calling user. Caps page size at 50.
/// Email is intentionally NOT searched: the column is AES-GCM-encrypted at rest, so a plaintext
/// LIKE never matches — it was dead. Equality email lookups use NormalizedEmailHash elsewhere.
/// </summary>
public sealed class SearchUsersQueryHandler(IReadDbConnection db)
    : IRequestHandler<SearchUsersQuery, Result<IReadOnlyList<UserSearchResultDto>>>
{
    public async Task<Result<IReadOnlyList<UserSearchResultDto>>> Handle(
        SearchUsersQuery request,
        CancellationToken cancellationToken)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, 50);

        // Escape LIKE wildcards to prevent SQL-LIKE injection via special chars
        var escaped = request.Q!
            .Replace("[", "[[]")
            .Replace("%", "[%]")
            .Replace("_", "[_]");

        const string sql = """
            SELECT TOP(@PageSize) Id, FullName, DepartmentCode
            FROM [identity].AspNetUsers
            WHERE TenantId = @TenantId AND IsActive = 1
              AND Id != @CurrentUserId
              AND FullName LIKE '%' + @Q + '%'
            ORDER BY FullName
            """;

        var rows = await db.QueryAsync<UserSearchResultDto>(sql, new
        {
            PageSize = pageSize,
            TenantId = request.TenantId.ToString(),
            Q = escaped,
            CurrentUserId = request.CurrentUserId.ToString()
        }, cancellationToken);

        return Result.Ok<IReadOnlyList<UserSearchResultDto>>(rows.ToList());
    }
}
