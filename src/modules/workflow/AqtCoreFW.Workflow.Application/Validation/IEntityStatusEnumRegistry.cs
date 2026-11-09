using AqtCoreFW.Workflow.Contracts.Validation;

namespace AqtCoreFW.Workflow.Application.Validation;

/// <summary>
/// Registry that maps entity types (e.g., "Generic") to their allowed status/state names.
/// Used by workflow validators to ensure state and transition names are valid for the target entity.
/// Singleton — built at startup from all <see cref="IEntityStatusEnumContributor"/> registrations.
/// </summary>
public interface IEntityStatusEnumRegistry
{
    /// <summary>
    /// Returns true if the entity type has a registered contributor.
    /// </summary>
    bool IsKnownEntityType(string entityType);

    /// <summary>
    /// Returns the allowed state names for the given entity type.
    /// Returns empty collection if entity type is unknown.
    /// </summary>
    IReadOnlyCollection<string> GetAllowedStateNames(string entityType);

    /// <summary>
    /// Returns all registered entity types.
    /// </summary>
    IReadOnlyCollection<string> GetKnownEntityTypes();
}
