using System.Collections.Concurrent;
using AqtCoreFW.SharedKernel.Events;

namespace AqtCoreFW.Infrastructure.Events;

/// <summary>
/// In-memory catalog of all domain events registered at startup.
/// Thread-safe via ConcurrentDictionary — keyed by event type name (short class name).
/// Register events explicitly in InfrastructureRegistration or each module's startup.
/// </summary>
public interface IEventCatalogService
{
    /// <summary>Register a domain event type with its source module and optional description.</summary>
    void Register<TEvent>(string sourceModule, string? description = null) where TEvent : class;

    /// <summary>Return all registered events ordered by source module then event name.</summary>
    IReadOnlyList<EventCatalogEntry> GetAll();

    /// <summary>Look up a single event by its short class name (e.g. "CaseCreatedEvent").</summary>
    EventCatalogEntry? GetByName(string eventName);
}

/// <inheritdoc />
public sealed class EventCatalogService : IEventCatalogService
{
    // Key: short type name (EventCatalogEntry.EventName), Value: entry
    private readonly ConcurrentDictionary<string, EventCatalogEntry> _catalog = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void Register<TEvent>(string sourceModule, string? description = null) where TEvent : class
    {
        var type = typeof(TEvent);
        var entry = new EventCatalogEntry
        {
            EventName     = type.Name,
            SourceModule  = sourceModule,
            Description   = description,
            EventType     = type,
            SchemaVersion = "1.0"
        };
        // AddOrUpdate is idempotent — safe to call multiple times at startup
        _catalog.AddOrUpdate(type.Name, entry, (_, _) => entry);
    }

    /// <inheritdoc />
    public IReadOnlyList<EventCatalogEntry> GetAll() =>
        _catalog.Values
            .OrderBy(e => e.SourceModule)
            .ThenBy(e => e.EventName)
            .ToList();

    /// <inheritdoc />
    public EventCatalogEntry? GetByName(string eventName) =>
        _catalog.TryGetValue(eventName, out var entry) ? entry : null;
}
