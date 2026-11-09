namespace AqtCoreFW.Workflow.Application.Services;

/// <summary>
/// Distributed advisory lock on a source workflow definition ID.
/// Prevents concurrent migrations from the same source definition running in parallel.
/// Implementation wraps SQL Server sp_getapplock (Exclusive, Transaction scope).
/// Lock is automatically released when the ambient transaction commits or rolls back.
/// </summary>
public interface ISourceDefinitionLockService
{
    /// <summary>
    /// Attempts to acquire an exclusive advisory lock on <paramref name="sourceDefinitionId"/>.
    /// Resource name: <c>wf-migration-src-{sourceDefinitionId}</c>.
    /// Lock is held for the lifetime of the current transaction.
    /// </summary>
    /// <param name="sourceDefinitionId">Definition ID to lock.</param>
    /// <param name="timeout">How long to wait before giving up.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the lock was acquired; false if another holder owns it within timeout.</returns>
    Task<bool> AcquireAsync(Guid sourceDefinitionId, TimeSpan timeout, CancellationToken ct);
}
