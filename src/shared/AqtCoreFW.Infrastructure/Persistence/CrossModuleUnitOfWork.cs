using System.Data;
using System.Data.Common;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Infrastructure.Persistence;

/// <summary>
/// Scoped implementation of <see cref="ICrossModuleUnitOfWork"/>. Owns ONE <see cref="SqlConnection"/>
/// and ONE <see cref="SqlTransaction"/> for the lifetime of an HTTP request.
///
/// Why one shared connection: when two DbContexts each open their own SqlConnection inside a
/// System.Transactions TransactionScope, ADO.NET promotes to a distributed transaction (DTC).
/// DTC is not available on Linux. By sharing a single open connection across all enlisted
/// DbContexts, all writes participate in one LOCAL SQL transaction — no DTC promotion.
///
/// Session context (sp_set_session_context N'TenantId') is set ONCE on Begin so that every
/// subsequent Dapper read and EF command on the same connection inherits RLS automatically.
/// Pool reset (sp_reset_connection) on connection return clears the session context — the
/// safety net that prevents tenant leakage across pooled connections.
/// </summary>
public sealed class CrossModuleUnitOfWork : ICrossModuleUnitOfWork, IAsyncDisposable, IDisposable
{
    private readonly IOptionsMonitor<DatabaseOptions> _dbOptions;
    private readonly ITenantContext _tenantContext;

    private SqlConnection? _connection;
    private SqlTransaction? _transaction;

    // Tracks DbContext instances already enlisted so EnlistAsync is idempotent.
    // Exposed internally for the Infrastructure extension method.
    internal readonly HashSet<object> EnlistedContexts = new(ReferenceEqualityComparer.Instance);

    public CrossModuleUnitOfWork(
        IOptionsMonitor<DatabaseOptions> dbOptions,
        ITenantContext tenantContext)
    {
        _dbOptions = dbOptions;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc/>
    public bool IsActive { get; private set; }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">When <see cref="IsActive"/> is false.</exception>
    public DbConnection Connection =>
        IsActive && _connection is not null
            ? _connection
            : throw new InvalidOperationException(
                "CrossModuleUnitOfWork is not active. Call BeginAsync before accessing Connection.");

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">When <see cref="IsActive"/> is false.</exception>
    public DbTransaction Transaction =>
        IsActive && _transaction is not null
            ? _transaction
            : throw new InvalidOperationException(
                "CrossModuleUnitOfWork is not active. Call BeginAsync before accessing Transaction.");

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">When unit of work is already active (double-Begin
    /// is a caller bug; silent no-op would hide it).</exception>
    /// <remarks>
    /// H2 fix (Slice 1B-2, audit-outbox atomicity): the class doc originally assumed "call
    /// BeginAsync once per request", but AuditBehavior runs AFTER the inner handler returns — so a
    /// request whose handler already opened+committed its OWN cycle (e.g. SubmitForm) then triggers
    /// a SECOND, sequential (never concurrent — the guard above handles that) cycle when
    /// AuditService enlists the SAME scoped instance to enqueue its outbox row. Before this fix,
    /// every Begin unconditionally opened a BRAND NEW physical <see cref="SqlConnection"/> and
    /// overwrote <c>_connection</c>, orphaning the previous (already-committed, still-open)
    /// connection — never disposed, since nothing else held a reference to it — leaking one
    /// physical connection per audited command until GC finalization eventually returned it to the
    /// pool. This override instead REUSES the already-open connection from a prior completed cycle
    /// — per the class remarks, the connection is meant to live for the whole request scope while
    /// only the transaction is cycle-scoped — and starts a fresh transaction on it: no second
    /// connection, no leak, and enlisted contexts from the prior cycle keep the SAME underlying
    /// connection object (only their transaction binding was reset in
    /// <see cref="ResetEnlistedContextsAsync"/>, exactly so this reuse is safe).
    /// </remarks>
    public Task BeginAsync(CancellationToken ct = default) => BeginCoreAsync(null, ct);

    /// <inheritdoc/>
    /// <remarks>
    /// Tenant-override variant (tenant-agnostic write paths — e.g. anonymous public-form submit): pins
    /// SESSION_CONTEXT to <paramref name="tenantOverride"/> instead of the ambient ITenantContext (which
    /// is null for an anonymous caller). Identical reuse / leak / disposal semantics as the parameterless
    /// overload — see its remarks.
    /// </remarks>
    public Task BeginAsync(Guid tenantOverride, CancellationToken ct = default) =>
        BeginCoreAsync(tenantOverride, ct);

    /// <summary>
    /// Shared Begin implementation. <paramref name="explicitTenant"/> null → pin SESSION_CONTEXT from the
    /// ambient <see cref="ITenantContext"/> (standard authenticated / SystemAdmin path); non-null → pin
    /// that server-resolved tenant (tenant-agnostic path). See the public overloads for intent.
    /// </summary>
    private async Task BeginCoreAsync(Guid? explicitTenant, CancellationToken ct)
    {
        if (IsActive)
            throw new InvalidOperationException(
                "CrossModuleUnitOfWork.BeginAsync called while already active. " +
                "A unit of work may be started only once per request scope.");

        // A prior cycle's SqlTransaction (already committed/rolled back by CommitAsync/
        // RollbackAsync, which complete the ADO.NET object but deliberately do not dispose it —
        // that stays this class's job) is about to be replaced. Dispose it now instead of only at
        // scope end, so a second (or third) sequential cycle in this scope doesn't accumulate
        // undisposed completed-transaction objects.
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }

        // A prior cycle in this same scope already committed/rolled back (IsActive is false) but
        // left the connection open and referenced — reuse it instead of opening a second physical
        // connection. Falls through to opening a fresh one if there is none yet, or the previous
        // one is no longer usable (e.g. broken by a prior network drop).
        if (_connection is { State: ConnectionState.Open } existing)
        {
            // Clear enlistment tracking from the prior cycle: EnlistAsync's `Add` idempotency
            // check would otherwise skip re-binding a context instance re-enlisted in this new
            // cycle, leaving it on no-transaction instead of the new one (silently defeating
            // atomicity for that context in this cycle).
            EnlistedContexts.Clear();

            // The reused connection still carries the SESSION_CONTEXT set by the prior cycle. Only
            // RE-pin when THIS cycle supplies an explicit tenant override — leaving the null-override
            // (ambient) reuse path byte-identical to the pre-override H2 behavior the audit
            // second-cycle relies on (it inherits the request's tenant from cycle 1).
            if (explicitTenant.HasValue)
                await SetTenantSessionContextAsync(existing, explicitTenant.Value, ct);

            var reusedTx = (SqlTransaction)await existing.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            _transaction = reusedTx;
            IsActive = true;
            return;
        }

        // Fall-through: no reusable OPEN connection. If a prior cycle left a non-open (broken/closed
        // after a mid-cycle network drop) connection, dispose it before opening a fresh one —
        // otherwise `_connection = conn` below overwrites the reference and the broken connection
        // leaks until GC finalization. Same leak class the reuse branch eliminates, on the error path.
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }

        var conn = new SqlConnection(_dbOptions.CurrentValue.ConnectionString);
        try
        {
            await conn.OpenAsync(ct);

            // Set TenantId session context ONCE so all subsequent commands on this connection are
            // tenant-scoped by RLS — mirrors DapperReadDbConnection.OpenWithTenantAsync. An explicit
            // override (tenant-agnostic path) wins over the ambient ITenantContext, which is null for
            // an anonymous caller.
            var pinTenant = explicitTenant ?? _tenantContext.TenantId;
            if (pinTenant.HasValue)
                await SetTenantSessionContextAsync(conn, pinTenant.Value, ct);

            // ReadCommitted matches the isolation level used by the individual DbContext
            // SaveChanges calls this UoW replaces — no isolation escalation.
            var tx = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);

            _connection = conn;
            _transaction = tx;
            IsActive = true;
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Sets SQL Server SESSION_CONTEXT N'TenantId' on <paramref name="conn"/> so the RLS predicate
    /// functions filter to <paramref name="tenantId"/>. Parameterised — never string-interpolated (SQLi).
    /// </summary>
    private static async Task SetTenantSessionContextAsync(
        SqlConnection conn, Guid tenantId, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXEC sp_set_session_context N'TenantId', @tenantId";
        cmd.Parameters.Add(new SqlParameter("@tenantId", tenantId.ToString()));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc/>
    public async Task CommitAsync(CancellationToken ct = default)
    {
        EnsureActive(nameof(CommitAsync));
        // try/finally: if commit throws (deadlock victim, timeout, network drop) the transaction
        // is no longer usable — flip IsActive=false so the state machine matches reality and the
        // safety rollback in DisposeAsync does not attempt a second commit/rollback on a dead tx.
        try { await _transaction!.CommitAsync(ct); }
        finally
        {
            IsActive = false;
            // H1 fix: CommitAsync completes the raw SqlTransaction directly — EF is never told,
            // so every enlisted DbContext's Database.CurrentTransaction still points at the now-
            // dead transaction. Detach it so a later SaveChanges on the SAME enlisted context (a
            // deliberately-outside-the-span, post-commit write — e.g. SubmitFormCommandHandler's
            // workflow-link update) opens its OWN fresh transaction instead of reusing the
            // completed one and throwing. Runs whether commit succeeded or threw — either way the
            // transaction is no longer usable by EF.
            await ResetEnlistedContextsAsync();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Idempotent: a no-op when no transaction is active. This lets a handler's catch-all safely
    /// call RollbackAsync even when an earlier CommitAsync already failed (CommitAsync flips
    /// IsActive=false in its finally) — without that no-op, the rollback would throw and mask the
    /// original commit exception. The dispose safety-rollback is the backstop for an aborted tx.
    /// </remarks>
    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (!IsActive) return;
        try { await _transaction!.RollbackAsync(ct); }
        finally
        {
            IsActive = false;
            // H1 fix: same reasoning as CommitAsync — detach every enlisted context from the now-
            // dead transaction so it can open its own on the (still-open) shared connection later.
            await ResetEnlistedContextsAsync();
        }
    }

    /// <summary>
    /// Detaches every enlisted <see cref="DbContext"/>'s transaction binding after the shared
    /// <see cref="SqlTransaction"/> completes (commit or rollback). <see cref="DatabaseFacade"/>
    /// tracks <c>CurrentTransaction</c> independently of the ADO.NET object's actual server-side
    /// state — committing/rolling back <see cref="_transaction"/> directly (as Commit/RollbackAsync
    /// do, to keep every enlisted context on ONE local SQL transaction — see class remarks) never
    /// updates that EF-side pointer. <c>UseTransactionAsync(null, ...)</c> clears it; EF then opens
    /// a brand-new transaction on the connection (still open — the UoW owns its lifetime until its
    /// own DisposeAsync) the next time <c>SaveChangesAsync</c> runs on that context, instead of
    /// reusing — and throwing on — the one that just completed.
    ///
    /// Uses <see cref="CancellationToken.None"/>: this is unconditional post-commit/rollback
    /// cleanup, not part of the caller's operation — it must run even if the caller's token was
    /// already canceled. Defensive: a context that faulted or was disposed by its own scope must
    /// not block resetting its siblings, nor propagate out of Commit/RollbackAsync.
    /// </summary>
    private async Task ResetEnlistedContextsAsync()
    {
        foreach (var enlisted in EnlistedContexts)
        {
            if (enlisted is not DbContext ctx)
                continue;

            try
            {
                await ctx.Database.UseTransactionAsync(null, CancellationToken.None);
            }
            catch
            {
                // Swallow — a single faulted/disposed enlisted context must not block resetting
                // the rest, nor mask the Commit/Rollback outcome already captured above.
            }
        }
    }

    /// <summary>
    /// Rolls back any uncommitted transaction and releases the connection back to the pool.
    /// Pool reset (sp_reset_connection) clears session_context — no tenant leak across requests.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (IsActive && _transaction is not null)
        {
            // Safety rollback: handler failed without explicit RollbackAsync (e.g. exception).
            // Swallow — disposal must not throw; the original exception is already propagating.
            try { await _transaction.RollbackAsync(); }
            catch { /* intentionally swallowed — original exception takes precedence */ }
        }

        IsActive = false;

        if (_transaction is not null)
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }

        // Clear enlisted-context tracking so a future reuse (if Scoped lifetime is ever shared
        // across requests by mistake) cannot silently skip re-enlisting a context that has lost
        // its connection binding. Enlisted DbContexts MUST NOT be used after this dispose —
        // their SetDbConnection pointer is now stale (disposed conn).
        EnlistedContexts.Clear();
    }

    /// <summary>
    /// Synchronous dispose. Required IN ADDITION to <see cref="DisposeAsync"/>: the DI container
    /// disposes a scope synchronously in some host paths (e.g. an IHostedService that calls
    /// scope.Dispose()), and it throws if a scoped service implements ONLY IAsyncDisposable.
    /// Mirrors the async path: safety-rollback any open transaction, then dispose conn + tx.
    /// </summary>
    public void Dispose()
    {
        if (IsActive && _transaction is not null)
        {
            try { _transaction.Rollback(); }
            catch { /* intentionally swallowed — original exception takes precedence */ }
        }

        IsActive = false;

        _transaction?.Dispose();
        _transaction = null;

        _connection?.Dispose();
        _connection = null;

        EnlistedContexts.Clear();
    }

    private void EnsureActive(string operation)
    {
        if (!IsActive)
            throw new InvalidOperationException(
                $"CrossModuleUnitOfWork.{operation} called but no transaction is active. " +
                "Call BeginAsync first.");
    }
}
