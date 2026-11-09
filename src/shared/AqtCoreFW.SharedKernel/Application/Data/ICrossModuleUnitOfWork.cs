using System.Data.Common;

namespace AqtCoreFW.SharedKernel.Application.Data;

/// <summary>
/// Request-scoped ambient unit of work that owns ONE open <see cref="DbConnection"/> and ONE
/// <see cref="DbTransaction"/>. Multiple module DbContexts and Dapper reads can enlist onto
/// the same connection + transaction so all writes commit or roll back atomically — without
/// System.Transactions or DTC, which promote to a distributed transaction on Linux when a
/// second physical connection is opened.
///
/// One shared connection = one local SQL transaction = no DTC promotion on Linux.
///
/// Lifecycle: call <see cref="BeginAsync(CancellationToken)"/> once per request; enlist each
/// DbContext (Infrastructure-side helper); call <see cref="CommitAsync"/> on success. Dispose at
/// request end is a safety net: it rolls back ONLY if the transaction is still open
/// (i.e. neither Commit nor Rollback was called). Successful commits stay committed.
///
/// ADDITIVE — dormant until a handler explicitly calls <see cref="BeginAsync(CancellationToken)"/>.
/// All existing code paths that never call BeginAsync are unaffected.
/// </summary>
/// <remarks>
/// Abstraction uses <see cref="DbConnection"/> / <see cref="DbTransaction"/> (System.Data.Common)
/// so SharedKernel stays free of Microsoft.Data.SqlClient and Microsoft.EntityFrameworkCore.
/// EnlistAsync for EFCore DbContext lives as an extension method in the Infrastructure layer.
/// </remarks>
public interface ICrossModuleUnitOfWork
{
    /// <summary>True after <see cref="BeginAsync(CancellationToken)"/> and before
    /// <see cref="CommitAsync"/> or <see cref="RollbackAsync"/>.</summary>
    bool IsActive { get; }

    /// <summary>The shared connection. Only valid when <see cref="IsActive"/> is true.</summary>
    DbConnection Connection { get; }

    /// <summary>The active transaction. Only valid when <see cref="IsActive"/> is true.</summary>
    DbTransaction Transaction { get; }

    /// <summary>
    /// Opens the shared connection, sets SESSION_CONTEXT TenantId for RLS from the ambient
    /// <c>ITenantContext</c>, and begins a ReadCommitted transaction. Throws
    /// <see cref="InvalidOperationException"/> if already active.
    /// </summary>
    Task BeginAsync(CancellationToken ct = default);

    /// <summary>
    /// Same as <see cref="BeginAsync(CancellationToken)"/> but pins SESSION_CONTEXT to an EXPLICIT
    /// tenant instead of the ambient <c>ITenantContext</c>. For tenant-agnostic write paths whose
    /// tenant is resolved from a persisted entity rather than the principal — e.g. an
    /// <c>[AllowAnonymous]</c> public-form submission whose tenant comes from the template row. The
    /// caller owns tenant safety: <paramref name="tenantOverride"/> MUST be the server-resolved tenant,
    /// never a client-supplied value.
    /// </summary>
    /// <param name="tenantOverride">The server-resolved tenant to pin for RLS on this unit of work.</param>
    Task BeginAsync(Guid tenantOverride, CancellationToken ct = default);

    /// <summary>Commits the active transaction.</summary>
    Task CommitAsync(CancellationToken ct = default);

    /// <summary>Rolls back the active transaction. Idempotent — a no-op when not active, so a
    /// caller's catch-all can roll back safely even after a failed Commit without masking the
    /// original exception.</summary>
    Task RollbackAsync(CancellationToken ct = default);
}
