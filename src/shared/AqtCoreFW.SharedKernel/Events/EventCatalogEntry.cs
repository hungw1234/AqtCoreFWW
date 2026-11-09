namespace AqtCoreFW.SharedKernel.Events;

/// <summary>
/// Describes a domain event registered in the event catalog at startup.
/// POCO — not persisted; scanned assemblies or manually registered on app start.
/// Used by EventCatalogService for discoverability and documentation of cross-module events.
/// </summary>
public sealed class EventCatalogEntry
{
    public string EventName { get; init; } = default!;       // e.g. "CaseCreatedEvent"
    public string SourceModule { get; init; } = default!;    // e.g. "Cases"
    public string? Description { get; init; }
    public Type EventType { get; init; } = default!;
    public string SchemaVersion { get; init; } = "1.0";
}
