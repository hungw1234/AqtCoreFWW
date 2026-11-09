using AqtCoreFW.SharedKernel.Extensions;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Infrastructure.Extensions;

/// <summary>
/// Runs all handlers registered for a given extension point key in priority order.
/// Per-handler isolation: each handler gets its own timeout + exception boundary.
/// Failing or timing-out handlers are logged and skipped; remaining handlers still execute.
/// </summary>
public sealed class ExtensionExecutor : IExtensionExecutor
{
    private readonly ExtensionRegistry _registry;
    private readonly ILogger<ExtensionExecutor> _logger;
    private readonly int _handlerTimeoutSeconds;

    public ExtensionExecutor(
        ExtensionRegistry registry,
        IConfiguration configuration,
        ILogger<ExtensionExecutor> logger)
    {
        _registry = registry;
        _logger = logger;
        _handlerTimeoutSeconds = configuration.GetValue<int>("Extensions:HandlerTimeoutSeconds", 5);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TOutput>> ExecuteAsync<TInput, TOutput>(
        string key,
        TInput input,
        CancellationToken ct = default)
    {
        var handlers = _registry.GetHandlers<TInput, TOutput>(key);

        if (handlers.Count == 0)
            return [];

        var results = new List<TOutput>(handlers.Count);

        foreach (var handler in handlers)
        {
            var result = await RunHandlerAsync(handler, input, key, ct);
            if (result.HasValue)
                results.Add(result.Value);
        }

        return results;
    }

    // --- private ---

    private async Task<Optional<TOutput>> RunHandlerAsync<TInput, TOutput>(
        IExtensionHandler<TInput, TOutput> handler,
        TInput input,
        string key,
        CancellationToken callerCt)
    {
        // Each handler gets its own combined timeout + caller cancellation token
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_handlerTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(callerCt, timeoutCts.Token);

        try
        {
            var value = await handler.HandleAsync(input, linkedCts.Token);
            return Optional<TOutput>.Some(value);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _logger.LogWarning(
                "ExtensionExecutor: handler {Handler} for key '{Key}' timed out after {Timeout}s — skipping",
                handler.GetType().Name, key, _handlerTimeoutSeconds);
            return Optional<TOutput>.None;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ExtensionExecutor: handler {Handler} for key '{Key}' threw — skipping",
                handler.GetType().Name, key);
            return Optional<TOutput>.None;
        }
    }
}

// Minimal value-type optional to avoid nullable ambiguity with struct TOutput
internal readonly struct Optional<T>
{
    private readonly T _value;
    public bool HasValue { get; }
    public T Value => _value;

    private Optional(T value) { _value = value; HasValue = true; }

    public static Optional<T> Some(T value) => new(value);
    public static Optional<T> None => default;
}
