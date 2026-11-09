using System.Collections.Frozen;
using AqtCoreFW.Workflow.Application.Validation;
using AqtCoreFW.Workflow.Contracts.Validation;

namespace AqtCoreFW.Workflow.Infrastructure.Validation;

/// <summary>
/// Singleton registry that collects all <see cref="IEntityStatusEnumContributor"/> instances at construction time
/// and builds an immutable lookup table mapping entity types to their allowed state names.
/// Thread-safe via FrozenDictionary (immutable after construction).
/// </summary>
public sealed class EntityStatusEnumRegistry : IEntityStatusEnumRegistry
{
    private readonly FrozenDictionary<string, IReadOnlyCollection<string>> _registry;
    private readonly IReadOnlyCollection<string> _knownEntityTypes;

    public EntityStatusEnumRegistry(IEnumerable<IEntityStatusEnumContributor> contributors)
    {
        var dict = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);

        foreach (var contributor in contributors)
        {
            if (string.IsNullOrWhiteSpace(contributor.EntityType))
                continue;

            // Last-write-wins if duplicate entity types (shouldn't happen in practice)
            dict[contributor.EntityType] = contributor.GetStateNames();
        }

        _registry = dict.ToFrozenDictionary(StringComparer.Ordinal);
        _knownEntityTypes = _registry.Keys.ToArray();
    }

    public bool IsKnownEntityType(string entityType)
    {
        if (string.IsNullOrWhiteSpace(entityType))
            return false;

        return _registry.ContainsKey(entityType);
    }

    public IReadOnlyCollection<string> GetAllowedStateNames(string entityType)
    {
        if (string.IsNullOrWhiteSpace(entityType))
            return Array.Empty<string>();

        return _registry.TryGetValue(entityType, out var names)
            ? names
            : Array.Empty<string>();
    }

    public IReadOnlyCollection<string> GetKnownEntityTypes() => _knownEntityTypes;
}
