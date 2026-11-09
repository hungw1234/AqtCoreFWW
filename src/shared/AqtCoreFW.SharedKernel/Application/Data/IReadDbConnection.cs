namespace AqtCoreFW.SharedKernel.Application.Data;

/// <summary>
/// Read-side Dapper abstraction for query handlers.
/// Query handlers use this directly — bypass IRepository entirely.
/// Backed by a read replica connection in production.
/// IMPORTANT: All SQL must use @param DynamicParameters (no string interpolation) to prevent SQLi.
/// </summary>
public interface IReadDbConnection
{
    // E-03: commandTimeout added — callers can set per-query limits (report SQL defaults to 60s)
    Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null, CancellationToken cancellationToken = default, int? commandTimeout = null);
    Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null, CancellationToken cancellationToken = default, int? commandTimeout = null);
    Task<T> QuerySingleAsync<T>(string sql, object? param = null, CancellationToken cancellationToken = default, int? commandTimeout = null);
    Task<int> ExecuteAsync(string sql, object? param = null, CancellationToken cancellationToken = default, int? commandTimeout = null);

    /// <summary>
    /// Streams rows one-at-a-time WITHOUT buffering the whole result set in memory — the backing
    /// SqlDataReader stays open and the owning connection is disposed only when the returned
    /// enumerator is fully iterated (or disposed early via <c>break</c>). Use for large exports where
    /// materializing every row (<see cref="QueryAsync{T}"/>) would hold tens of MB at once.
    ///
    /// LIFETIME (critical): the connection lives for the duration of the <c>await foreach</c>, so the
    /// consumer MUST iterate to completion or dispose the enumerator; do not capture the enumerable and
    /// enumerate it after the calling scope closes.
    ///
    /// NO ambient unit-of-work branch: unlike the four buffered methods above, this always opens a
    /// dedicated connection and MUST NOT be called inside a cross-module write scope (it never shares
    /// <c>uow.Connection</c> — a long-lived reader on the write connection would block the transaction).
    /// Tenant isolation still holds: the dedicated connection sets the same TenantId SESSION_CONTEXT as
    /// the buffered path, and callers should additionally pin tenant with an explicit @param WHERE.
    /// </summary>
    IAsyncEnumerable<T> QueryUnbufferedAsync<T>(string sql, object? param = null, CancellationToken cancellationToken = default, int? commandTimeout = null);

    // ── Audit-admin bypass variants (PHASE6-RLS-NULLABLE-TENANT) ─────────────
    // Opens ONE physical connection, sets SESSION_CONTEXT TenantId (if ambient) +
    // SESSION_CONTEXT audit_admin_bypass='true', runs the query, then closes.
    // Do NOT pass @read_only=1 — default pool reset clears the flag (no leak).
    // Gate every call on tenantContext.IsSystemAdmin == true in the calling handler.

    /// <summary>
    /// Runs a SELECT on a single connection that has audit_admin_bypass='true' set,
    /// so RLS FILTER also exposes NULL-tenant (system) audit rows.
    /// </summary>
    Task<IEnumerable<T>> QueryWithAuditAdminBypassAsync<T>(
        string sql, object? param = null, CancellationToken cancellationToken = default, int? commandTimeout = null);

    /// <summary>
    /// Runs a scalar SELECT (e.g. COUNT) on a single connection with audit_admin_bypass='true'.
    /// </summary>
    Task<T> QuerySingleWithAuditAdminBypassAsync<T>(
        string sql, object? param = null, CancellationToken cancellationToken = default, int? commandTimeout = null);

    // ── Public-forms bypass variant (PUBLIC-FORMS-LEAST-PRIV) ────────────────
    // Opens ONE physical connection, sets SESSION_CONTEXT public_forms_bypass='true' (no ambient
    // tenant — the caller is anonymous), runs the query, then closes. No @read_only=1 so the pool
    // reset clears the flag (no leak). The FormTemplates RLS FILTER carve-out then exposes an
    // IsPublic=1 row to the by-Code resolution. Use ONLY for anonymous public-template by-Code
    // lookups; the SQL MUST still constrain IsPublic=1.

    /// <summary>
    /// Runs a by-Code SELECT on a single connection with public_forms_bypass='true' set, so the
    /// FormTemplates RLS FILTER also exposes IsPublic=1 rows to an anonymous (no-tenant) caller.
    /// Also used by the authenticated public-Code-uniqueness guard, which needs the same cross-tenant
    /// IsPublic=1 visibility the resolution has (a tenant-pinned admin read cannot see another tenant's
    /// public template under least-priv). The SQL MUST still constrain IsPublic=1.
    /// </summary>
    Task<T?> QueryFirstOrDefaultWithPublicFormsBypassAsync<T>(
        string sql, object? param = null, CancellationToken cancellationToken = default, int? commandTimeout = null);
}
