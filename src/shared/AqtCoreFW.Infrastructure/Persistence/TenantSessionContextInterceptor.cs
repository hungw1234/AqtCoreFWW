using System.Data.Common;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Infrastructure.Persistence;

/// <summary>
/// EF DbConnectionInterceptor — sets SQL Server SESSION_CONTEXT on every connection open.
/// Executes: EXEC sp_set_session_context 'TenantId', @tenantId
///
/// This feeds the SQL Server Row-Level Security (RLS) function fn_TenantFilter(),
/// which reads SESSION_CONTEXT(N'TenantId') to enforce tenant isolation at DB engine level.
/// Works on top of (not replacing) EF global query filters in ModuleDbContext.
///
/// Registration: add to DbContextOptionsBuilder.AddInterceptors() alongside other interceptors.
/// Stateless per-request (ITenantContext is scoped) — register as Scoped.
/// </summary>
public sealed class TenantSessionContextInterceptor(
    ITenantContext tenantContext,
    ILogger<TenantSessionContextInterceptor> logger) : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await SetSessionContextAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        SetSessionContextSync(connection);
        base.ConnectionOpened(connection, eventData);
    }

    // ── Async path ────────────────────────────────────────────────────────────

    private async Task SetSessionContextAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId;
        if (tenantId is null)
        {
            // System admin or background job — SESSION_CONTEXT left unset → RLS blocks nothing
            logger.LogDebug("TenantSessionContext: no tenant — skipping sp_set_session_context");
            return;
        }

        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "EXEC sp_set_session_context N'TenantId', @tenantId";
            var param = new SqlParameter("@tenantId", tenantId.Value.ToString());
            cmd.Parameters.Add(param);
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            logger.LogDebug("TenantSessionContext: set TenantId={TenantId}", tenantId.Value);
        }
        catch (Exception ex)
        {
            // Log but do not swallow — RLS without SESSION_CONTEXT would be a security gap
            logger.LogError(ex, "Failed to set session context for tenant {TenantId}", tenantId.Value);
            throw;
        }
    }

    // ── Sync path (migration tooling, design-time) ────────────────────────────

    private void SetSessionContextSync(DbConnection connection)
    {
        var tenantId = tenantContext.TenantId;
        if (tenantId is null) return;

        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "EXEC sp_set_session_context N'TenantId', @tenantId";
            var param = new SqlParameter("@tenantId", tenantId.Value.ToString());
            cmd.Parameters.Add(param);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to set session context (sync) for tenant {TenantId}", tenantId.Value);
            throw;
        }
    }
}
