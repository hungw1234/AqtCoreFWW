namespace AqtCoreFW.SharedKernel.Extensions;

/// <summary>
/// Executes all registered handlers for a given extension point key.
/// Handlers run in priority order (ascending). Per-handler exceptions and timeouts
/// are isolated — a failing handler does not block the remaining ones.
/// </summary>
public interface IExtensionExecutor
{
    /// <summary>
    /// Runs every handler registered under <paramref name="key"/> in priority order.
    /// Returns results from handlers that completed successfully.
    /// Failed or timed-out handlers are skipped silently (logged internally).
    /// </summary>
    Task<IReadOnlyList<TOutput>> ExecuteAsync<TInput, TOutput>(
        string key,
        TInput input,
        CancellationToken ct = default);
}
