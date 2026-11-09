using System.Collections.Concurrent;
using AqtCoreFW.SharedKernel.Extensions;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Infrastructure.Extensions;

/// <summary>
/// DI-scanning registry for extension handlers.
/// On first use, resolves all IExtensionHandler&lt;,&gt; services from DI,
/// groups them by ExtensionPointKey, and caches the ordered lookup.
/// Thread-safe via Lazy initialization.
/// </summary>
public sealed class ExtensionRegistry
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ExtensionRegistry> _logger;

    // Key = ExtensionPointKey; Value = ordered list of handler instances (boxed as object)
    // Populated lazily on first call to GetHandlers.
    private readonly Lazy<ConcurrentDictionary<string, IReadOnlyList<object>>> _index;

    public ExtensionRegistry(IServiceProvider serviceProvider, ILogger<ExtensionRegistry> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _index = new Lazy<ConcurrentDictionary<string, IReadOnlyList<object>>>(
            BuildIndex, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Returns all handlers registered for <paramref name="key"/> cast to
    /// IExtensionHandler&lt;TInput, TOutput&gt;, ordered by Priority ascending.
    /// Returns empty list when no handlers are registered.
    /// </summary>
    public IReadOnlyList<IExtensionHandler<TInput, TOutput>> GetHandlers<TInput, TOutput>(string key)
    {
        if (!_index.Value.TryGetValue(key, out var boxed))
            return [];

        // Filter to handlers that actually match the requested generic types
        var typed = boxed
            .OfType<IExtensionHandler<TInput, TOutput>>()
            .ToList();

        return typed;
    }

    // --- private ---

    private ConcurrentDictionary<string, IReadOnlyList<object>> BuildIndex()
    {
        var dict = new ConcurrentDictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);

        // Walk all service registrations to find IExtensionHandler<,> implementations
        // We enumerate via GetServices on the open generic's closed variants
        // Since we can't enumerate open-generic registrations directly, we ask each
        // registered IExtensionHandler<,> to self-identify via the base object approach.
        // Instead: scan all registered services for types implementing IExtensionHandler<,>
        var allHandlers = ResolveAllHandlers();

        foreach (var handler in allHandlers)
        {
            var key = handler.Key;
            dict.AddOrUpdate(
                key,
                _ => [handler.Instance],
                (_, list) => { list.Add(handler.Instance); return list; });

            _logger.LogDebug(
                "ExtensionRegistry: registered handler {Type} for key '{Key}' priority {Priority}",
                handler.Instance.GetType().Name, key, handler.Priority);
        }

        // Sort each bucket by priority ascending, then freeze
        var frozen = new ConcurrentDictionary<string, IReadOnlyList<object>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, list) in dict)
        {
            var ordered = list
                .OrderBy(h => GetPriority(h))
                .ToList();
            frozen[k] = ordered;
        }

        _logger.LogInformation(
            "ExtensionRegistry: indexed {KeyCount} extension point(s) from DI",
            frozen.Count);

        return frozen;
    }

    /// <summary>
    /// Resolves all objects from DI that implement the marker interface IExtensionHandlerMarker.
    /// Handlers must also implement IExtensionHandlerMarker (non-generic) to be discoverable.
    /// </summary>
    private IEnumerable<(string Key, int Priority, object Instance)> ResolveAllHandlers()
    {
        var handlers = _serviceProvider.GetServices<IExtensionHandlerMarker>();
        return handlers.Select(h => (h.ExtensionPointKey, h.Priority, (object)h));
    }

    private static int GetPriority(object handler) =>
        handler is IExtensionHandlerMarker m ? m.Priority : 100;
}
