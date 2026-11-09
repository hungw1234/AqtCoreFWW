using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application.Data;
using Dapper;

namespace AqtCoreFW.Identity.Infrastructure.Persistence;

/// <summary>
/// Reads active token metadata from the OpenIddict token store via Dapper.
/// Bypasses EF DbSet to avoid generic type registration issues.
/// "Active" = status is "valid" and expiry is in the future.
/// </summary>
public sealed class SessionRepository(IReadDbConnection db) : ISessionRepository
{
    public async Task<(IReadOnlyList<TokenInfo> Items, int TotalCount)> ListActiveAsync(
        Guid? userId, int page, int pageSize, CancellationToken ct = default)
    {
        var p = new DynamicParameters();
        p.Add("Now", DateTime.UtcNow);
        p.Add("Offset", (page - 1) * pageSize);
        p.Add("PageSize", pageSize);

        // OpenIddict stores tokens with Status ('valid','redeemed','revoked','inactive')
        // Show all valid, non-expired tokens (access + refresh) as active sessions
        var whereClause = "Status = N'valid' AND ExpirationDate IS NOT NULL AND ExpirationDate > @Now";

        if (userId.HasValue)
        {
            p.Add("Subject", userId.Value.ToString());
            whereClause += " AND Subject = @Subject";
        }

        var countSql = $"SELECT COUNT(*) FROM [identity].OpenIddictTokens WHERE {whereClause}";
        var dataSql = $"""
            SELECT t.Id, t.Subject, t.CreationDate, t.ExpirationDate,
                   a.ClientId AS AppClientId,
                   u.Email AS UserEmail, u.FullName AS UserName
            FROM [identity].OpenIddictTokens t
            LEFT JOIN [identity].AspNetUsers u ON t.Subject = CAST(u.Id AS NVARCHAR(50))
            LEFT JOIN [identity].OpenIddictApplications a ON t.ApplicationId = a.Id
            WHERE {whereClause}
            ORDER BY t.CreationDate DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY
            """;

        var totalCount = await db.QuerySingleAsync<int>(countSql, p, ct);
        var rows = await db.QueryAsync<TokenRow>(dataSql, p, ct);

        var items = rows.Select(r =>
        {
            Guid.TryParse(r.Subject, out var parsedUserId);
            return new TokenInfo(
                TokenId: r.Id ?? string.Empty,
                UserId: parsedUserId,
                UserEmail: r.UserEmail ?? r.UserName,
                IssuedAt: r.CreationDate ?? DateTime.MinValue,
                ExpiresAt: r.ExpirationDate ?? DateTime.MinValue,
                IpAddress: null,
                ClientId: r.AppClientId);
        }).ToList();

        return (items, totalCount);
    }

    private sealed record TokenRow(
        string? Id, string? Subject, DateTime? CreationDate,
        DateTime? ExpirationDate, string? AppClientId,
        string? UserEmail, string? UserName);
}
