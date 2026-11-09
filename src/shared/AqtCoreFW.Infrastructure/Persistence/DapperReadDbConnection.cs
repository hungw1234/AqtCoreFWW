using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Domain;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;

namespace AqtCoreFW.Infrastructure.Persistence;

/// <summary>
/// Dapper-backed read connection for query handlers.
/// Sets sp_set_session_context on every connection open to enable SQL Server RLS
/// as a safety net alongside explicit WHERE TenantId filters.
///
/// ADO.NET connection pool handles physical connection reuse; open/close per call is correct.
/// IMPORTANT: All SQL MUST use @param DynamicParameters — no string interpolation (SQLi prevention).
///
/// Ambient unit-of-work branch: when <see cref="ICrossModuleUnitOfWork.IsActive"/> is true the
/// four standard query methods run on the shared connection + transaction instead of opening a
/// fresh SqlConnection. Session context is already set on that connection by BeginAsync — no
/// duplicate sp_set_session_context call is issued here. The audit-admin-bypass variants always
/// use their own dedicated connection (admin reads are never inside a cross-module write scope).
///
/// Read-path login isolation (ISO-DAPPER): <paramref name="connectionStringOverride"/> selects the
/// login this reader opens connections on. The unkeyed DI registration passes the least-privilege
/// NonAudit login (denied <c>[audit]</c>); the keyed <c>"audit"</c> registration passes the broad
/// Default (audit-owner) login. When null (hand-construction / tests) it falls back to the global
/// <see cref="DatabaseOptions.ConnectionString"/>. The ambient unit-of-work branch is unaffected —
/// it uses the shared <c>uow.Connection</c> regardless of which login this reader was built with.
/// </summary>
public sealed class DapperReadDbConnection(
    IOptionsMonitor<DatabaseOptions> dbOptions,
    ITenantContext tenantContext,
    ICrossModuleUnitOfWork uow,
    string? connectionStringOverride = null) : IReadDbConnection
{
    /// <summary>
    /// The login this reader opens connections on: the per-registration override when supplied, else
    /// the global <see cref="DatabaseOptions.ConnectionString"/>. Both the standard opener and the two
    /// SESSION_CONTEXT bypass openers use this, so the least-privilege boundary holds on every non-UoW
    /// read path (a NonAudit-bound reader cannot reach <c>[audit]</c> even through a bypass variant).
    /// </summary>
    private string ConnectionString => connectionStringOverride ?? dbOptions.CurrentValue.ConnectionString;

    /// <summary>Opens connection and sets SESSION_CONTEXT for RLS enforcement.</summary>
    private async Task<SqlConnection> OpenWithTenantAsync(CancellationToken ct)
    {
        var conn = new SqlConnection(ConnectionString);
        try
        {
            await conn.OpenAsync(ct);
            if (tenantContext.TenantId.HasValue)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "EXEC sp_set_session_context N'TenantId', @tenantId";
                cmd.Parameters.Add(new SqlParameter("@tenantId", tenantContext.TenantId.Value.ToString()));
                await cmd.ExecuteNonQueryAsync(ct);
            }
            return conn;
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Opens connection, sets TenantId SESSION_CONTEXT (if ambient), then sets
    /// SESSION_CONTEXT audit_admin_bypass='true' so the RLS FILTER also exposes
    /// NULL-tenant (system/global) audit rows.
    ///
    /// SAFETY: Do NOT pass @read_only=1 — default ADO.NET pool reset (sp_reset_connection)
    /// clears all session-context values, so the bypass flag never outlives this connection.
    /// Gate every call on tenantContext.IsSystemAdmin == true in the calling handler.
    /// </summary>
    private async Task<SqlConnection> OpenWithAuditAdminBypassAsync(CancellationToken ct)
    {
        var conn = new SqlConnection(ConnectionString);
        try
        {
            await conn.OpenAsync(ct);
            // Set TenantId context first (if present), consistent with standard open path
            if (tenantContext.TenantId.HasValue)
            {
                await using var tenantCmd = conn.CreateCommand();
                tenantCmd.CommandText = "EXEC sp_set_session_context N'TenantId', @tenantId";
                tenantCmd.Parameters.Add(new SqlParameter("@tenantId", tenantContext.TenantId.Value.ToString()));
                await tenantCmd.ExecuteNonQueryAsync(ct);
            }
            // Set the audit_admin_bypass flag — allows RLS FILTER to expose NULL-tenant rows.
            // No @read_only=1: pool reset clears this flag (safety property we rely on).
            await using var bypassCmd = conn.CreateCommand();
            bypassCmd.CommandText = "EXEC sp_set_session_context N'audit_admin_bypass', N'true'";
            await bypassCmd.ExecuteNonQueryAsync(ct);
            return conn;
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }

    public async Task<IEnumerable<T>> QueryAsync<T>(
        string sql, object? param = null, CancellationToken cancellationToken = default, int? commandTimeout = null)
    {
        // Ambient branch: reuse the shared connection + transaction when a unit of work is active.
        // Session context (TenantId) is already set on that connection by BeginAsync — no re-set needed.
        if (uow.IsActive)
            return await uow.Connection.QueryAsync<T>(
                new CommandDefinition(sql, param, transaction: uow.Transaction,
                    cancellationToken: cancellationToken, commandTimeout: commandTimeout));

        await using var conn = await OpenWithTenantAsync(cancellationToken);
        return await conn.QueryAsync<T>(
            new CommandDefinition(sql, param, cancellationToken: cancellationToken, commandTimeout: commandTimeout));
    }

    public async Task<T?> QueryFirstOrDefaultAsync<T>(
        string sql, object? param = null, CancellationToken cancellationToken = default, int? commandTimeout = null)
    {
        if (uow.IsActive)
            return await uow.Connection.QueryFirstOrDefaultAsync<T>(
                new CommandDefinition(sql, param, transaction: uow.Transaction,
                    cancellationToken: cancellationToken, commandTimeout: commandTimeout));

        await using var conn = await OpenWithTenantAsync(cancellationToken);
        return await conn.QueryFirstOrDefaultAsync<T>(
            new CommandDefinition(sql, param, cancellationToken: cancellationToken, commandTimeout: commandTimeout));
    }

    public async Task<T> QuerySingleAsync<T>(
        string sql, object? param = null, CancellationToken cancellationToken = default, int? commandTimeout = null)
    {
        if (uow.IsActive)
            return await uow.Connection.QuerySingleAsync<T>(
                new CommandDefinition(sql, param, transaction: uow.Transaction,
                    cancellationToken: cancellationToken, commandTimeout: commandTimeout));

        await using var conn = await OpenWithTenantAsync(cancellationToken);
        return await conn.QuerySingleAsync<T>(
            new CommandDefinition(sql, param, cancellationToken: cancellationToken, commandTimeout: commandTimeout));
    }

    public async Task<int> ExecuteAsync(
        string sql, object? param = null, CancellationToken cancellationToken = default, int? commandTimeout = null)
    {
        if (uow.IsActive)
            return await uow.Connection.ExecuteAsync(
                new CommandDefinition(sql, param, transaction: uow.Transaction,
                    cancellationToken: cancellationToken, commandTimeout: commandTimeout));

        await using var conn = await OpenWithTenantAsync(cancellationToken);
        return await conn.ExecuteAsync(
            new CommandDefinition(sql, param, cancellationToken: cancellationToken, commandTimeout: commandTimeout));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<T> QueryUnbufferedAsync<T>(
        string sql, object? param = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default, int? commandTimeout = null)
    {
        // G2 (no ambient UoW): a long-lived streaming reader must never run on the shared write
        // connection — it would hold the transaction open row-by-row. Fail loud if misused inside a
        // cross-module write scope rather than silently sharing uow.Connection.
        if (uow.IsActive)
            throw new InvalidOperationException(
                "QueryUnbufferedAsync must not run inside a cross-module unit of work — " +
                "streaming reads open a dedicated connection. Call it outside any BeginAsync scope.");

        // G1 (connection lifetime): open the connection here and keep it inside the await-foreach so the
        // backing SqlDataReader stays valid for the whole stream; `await using` disposes it exactly when
        // the consumer finishes iterating (or breaks early). Never return Dapper's enumerable bare.
        await using var conn = await OpenWithTenantAsync(cancellationToken);

        // Dapper 2.1.66 QueryUnbufferedAsync has NO CommandDefinition/CancellationToken overload — it
        // takes positional (sql, param, transaction, commandTimeout, commandType). Cancellation is
        // therefore observed here between rows, which is sufficient for a row-streamed export.
        await foreach (var row in conn.QueryUnbufferedAsync<T>(sql, param, commandTimeout: commandTimeout))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return row;
        }
    }

    // ── Audit-admin bypass variants ───────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IEnumerable<T>> QueryWithAuditAdminBypassAsync<T>(
        string sql, object? param = null, CancellationToken cancellationToken = default, int? commandTimeout = null)
    {
        // RLS: audit_admin_bypass — admin read of system/global audit rows
        await using var conn = await OpenWithAuditAdminBypassAsync(cancellationToken);
        return await conn.QueryAsync<T>(
            new CommandDefinition(sql, param, cancellationToken: cancellationToken, commandTimeout: commandTimeout));
    }

    /// <inheritdoc/>
    public async Task<T> QuerySingleWithAuditAdminBypassAsync<T>(
        string sql, object? param = null, CancellationToken cancellationToken = default, int? commandTimeout = null)
    {
        // RLS: audit_admin_bypass — admin read of system/global audit rows
        await using var conn = await OpenWithAuditAdminBypassAsync(cancellationToken);
        return await conn.QuerySingleAsync<T>(
            new CommandDefinition(sql, param, cancellationToken: cancellationToken, commandTimeout: commandTimeout));
    }

    // ── Public-forms bypass variant ─────────────────────────────────────────────

    /// <summary>
    /// Opens a dedicated connection and sets SESSION_CONTEXT public_forms_bypass='true'. No ambient
    /// tenant is set — the caller is anonymous; the FormTemplates RLS FILTER carve-out exposes only
    /// IsPublic=1 rows under this flag.
    ///
    /// SAFETY: Do NOT pass @read_only=1 — default ADO.NET pool reset (sp_reset_connection) clears all
    /// session-context values, so the flag never outlives this connection. This variant is only for the
    /// anonymous public-template by-Code resolution and never runs inside a cross-module write scope.
    /// </summary>
    private async Task<SqlConnection> OpenWithPublicFormsBypassAsync(CancellationToken ct)
    {
        var conn = new SqlConnection(ConnectionString);
        try
        {
            await conn.OpenAsync(ct);
            // No TenantId context — the anonymous public path has no ambient tenant. Set the bypass
            // flag only; the carve-out additionally requires the row's IsPublic=1.
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "EXEC sp_set_session_context N'public_forms_bypass', N'true'";
            await cmd.ExecuteNonQueryAsync(ct);
            return conn;
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<T?> QueryFirstOrDefaultWithPublicFormsBypassAsync<T>(
        string sql, object? param = null, CancellationToken cancellationToken = default, int? commandTimeout = null)
    {
        // RLS: public_forms_bypass — anonymous by-Code resolution of an IsPublic=1 public template.
        // Dedicated connection; never inside a cross-module write scope (resolution precedes any BeginAsync).
        await using var conn = await OpenWithPublicFormsBypassAsync(cancellationToken);
        return await conn.QueryFirstOrDefaultAsync<T>(
            new CommandDefinition(sql, param, cancellationToken: cancellationToken, commandTimeout: commandTimeout));
    }
}
