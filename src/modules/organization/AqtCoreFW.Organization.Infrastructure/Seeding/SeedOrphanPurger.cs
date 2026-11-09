using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Organization.Infrastructure.Seeding;

/// <summary>
/// Self-healing purge for orphan rows in organization tables whose UserId no longer
/// matches any identity.AspNetUsers row. Extracted from the assignment seeder so it can
/// be tested in isolation (no IServiceScopeFactory / IHostedService deps required).
///
/// Predicate is symmetric on BOTH UserId AND TenantId — defends cross-tenant ID
/// collision (P0 multi-tenant isolation). Aborts if orphan count exceeds ceiling
/// (config drift guard).
///
/// Concurrency: count + DELETE are NOT wrapped in a transaction. Safe by contract
/// because this runs only inside the assignment seeder's StartAsync, which is gated by
/// IsDevelopment() and executes BEFORE the HTTP listener accepts traffic — no
/// concurrent inserts can race the count→delete window. Do NOT call this method
/// from request-path code without adding a transaction first.
/// </summary>
internal static class SeedOrphanPurger
{
    private const string CountSql = """
        SELECT COUNT(*) FROM [organization].UserOrgUnitAssignments a
        WHERE a.TenantId = @TenantId
          AND NOT EXISTS (
            SELECT 1 FROM [identity].AspNetUsers u
            WHERE u.Id = a.UserId AND u.TenantId = a.TenantId
          );
        """;

    private const string PurgeSql = """
        DELETE a FROM [organization].UserOrgUnitAssignments a
        WHERE a.TenantId = @TenantId
          AND NOT EXISTS (
            SELECT 1 FROM [identity].AspNetUsers u
            WHERE u.Id = a.UserId AND u.TenantId = a.TenantId
          );

        DELETE h FROM [organization].StaffPositionHistories h
        WHERE h.TenantId = @TenantId
          AND NOT EXISTS (
            SELECT 1 FROM [identity].AspNetUsers u
            WHERE u.Id = h.UserId AND u.TenantId = h.TenantId
          );
        """;

    /// <summary>
    /// Counts orphan assignments + purges both assignments and histories for the tenant.
    /// Returns rows affected by purge (0 if no orphans). Throws InvalidOperationException
    /// if orphan count exceeds <paramref name="ceiling"/>.
    /// </summary>
    public static async Task<int> PurgeAsync(
        string connStr,
        Guid tenantId,
        int ceiling,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        await using var conn = new SqlConnection(connStr);
        var orphanCount = await conn.ExecuteScalarAsync<int>(
            new CommandDefinition(CountSql, new { TenantId = tenantId }, cancellationToken: ct));

        if (orphanCount == 0) return 0;

        if (orphanCount > ceiling)
        {
            logger?.LogError(
                "[SeedOrphanPurger] orphan count {Orphan} exceeds ceiling {Ceiling} — refusing automatic purge. Manual investigation required.",
                orphanCount, ceiling);
            throw new InvalidOperationException(
                $"Orphan purge ceiling exceeded: {orphanCount} > {ceiling}");
        }

        var purged = await conn.ExecuteAsync(
            new CommandDefinition(PurgeSql, new { TenantId = tenantId }, cancellationToken: ct));

        logger?.LogInformation(
            "[SeedOrphanPurger] purged {Purged} orphan rows (assignments + histories) tenant-scoped",
            purged);

        return purged;
    }
}
