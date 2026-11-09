namespace AqtCoreFW.Workflow.Contracts.Validation;

/// <summary>
/// Marker interface for modules to contribute their entity status enums to the workflow validation registry.
/// Each module registers one contributor per entity type it owns.
/// Example: a permits module could register a contributor for EntityType="BuildingPermit".
/// The framework ships <c>GenericEntityStatusEnumContributor</c> ("Generic") as the default.
/// Lives in Workflow.Contracts so consumer modules depend on the contract assembly only,
/// preserving module isolation (NoModule_Should_DirectlyReference_AnotherModule).
/// </summary>
public interface IEntityStatusEnumContributor
{
    /// <summary>
    /// The entity type this contributor provides state names for (e.g., "Generic").
    /// </summary>
    string EntityType { get; }

    /// <summary>
    /// Returns all valid state names for this entity type (from the domain enum).
    /// Must be case-sensitive and match exactly what workflow states should use.
    /// </summary>
    IReadOnlyCollection<string> GetStateNames();
}
