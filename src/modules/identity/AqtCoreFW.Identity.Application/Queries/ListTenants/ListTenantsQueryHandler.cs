using AqtCoreFW.SharedKernel.Application.Data;
using Dapper;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Identity.Application.Queries.ListTenants;

/// <summary>
/// Read-side query — returns tenants for SystemAdmin tenant-switcher dropdown.
/// Source-of-truth: [identity].[Tenants] canonical table; the tenant's own Name is the display
/// label (no cross-module org enrichment — Identity.Application stays decoupled from
/// OrgUnit.Contracts). User count per tenant comes from an own-schema Dapper query.
///
/// Behavior: excludes Archived tenants by default, returns all Active/Suspended.
/// </summary>
public sealed class ListTenantsQueryHandler(IReadDbConnection db)
    : IRequestHandler<ListTenantsQuery, Result<IReadOnlyList<TenantSummaryDto>>>
{
    public async Task<Result<IReadOnlyList<TenantSummaryDto>>> Handle(
        ListTenantsQuery query, CancellationToken ct)
    {
        // Step 1: Load canonical tenants from [identity].[Tenants] (exclude Archived)
        // [identity] schema must be bracket-escaped — T-SQL reserved keyword.
        // T2: select PublicId (Guid AK) — internal `Id` is long PK, but external
        // addressing key is `PublicId`. AspNetUsers.TenantId stores PublicId, so
        // downstream joins/lookups must use it. Aliased AS Id to fit DTO shape.
        const string tenantsSql = """
            SELECT PublicId AS Id, Name, Code
            FROM [identity].[Tenants]
            WHERE Status != 3
            ORDER BY Name
            """;

        var tenants = (await db.QueryAsync<TenantRow>(tenantsSql, new { }, ct)).ToList();

        if (tenants.Count == 0)
        {
            return Result.Ok<IReadOnlyList<TenantSummaryDto>>([]);
        }

        // Step 2: Enrich with per-tenant user count (own-schema Dapper query)
        var tenantIds = tenants.Select(t => t.Id).ToArray();
        var userCounts = await LoadUserCountsAsync(tenantIds, ct);

        // Step 3: Map to DTOs — tenant.Name is the authoritative display label
        var merged = tenants
            .Select(t =>
            {
                var count = userCounts.TryGetValue(t.Id, out var c) ? c : 0;
                return new TenantSummaryDto
                {
                    TenantId = t.Id,
                    TenantName = t.Name,
                    UserCount = count
                };
            })
            .OrderByDescending(t => t.UserCount)
            .ThenBy(t => t.TenantName)
            .ToList();

        return Result.Ok<IReadOnlyList<TenantSummaryDto>>(merged);
    }

    private async Task<IReadOnlyDictionary<Guid, int>> LoadUserCountsAsync(
        Guid[] tenantIds,
        CancellationToken ct)
    {
        // [identity] must be bracket-escaped — T-SQL reserved keyword
        const string sql = """
            SELECT TenantId, COUNT(*) AS UserCount
            FROM [identity].[AspNetUsers]
            WHERE TenantId IN @TenantIds
            GROUP BY TenantId
            """;

        var rows = await db.QueryAsync<TenantUserCountRow>(sql, new { TenantIds = tenantIds }, ct);
        return rows.ToDictionary(r => r.TenantId, r => r.UserCount);
    }

    // Class-based DTOs for Dapper compatibility
    private sealed class TenantRow
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string Code { get; init; } = string.Empty;
    }

    private sealed class TenantUserCountRow
    {
        public Guid TenantId { get; init; }
        public int UserCount { get; init; }
    }
}
