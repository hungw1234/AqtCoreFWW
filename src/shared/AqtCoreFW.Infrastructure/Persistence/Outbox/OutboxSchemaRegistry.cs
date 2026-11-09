using System.Text.RegularExpressions;

namespace AqtCoreFW.Infrastructure.Persistence.Outbox;

/// <summary>
/// Mutable singleton implementation of <see cref="IOutboxSchemaRegistry"/>.
/// Modules call <see cref="OutboxSchemaRegistryExtensions.AddOutboxSchema"/> at startup;
/// after host build the list is effectively immutable (no late mutation expected).
/// </summary>
public sealed class OutboxSchemaRegistry : IOutboxSchemaRegistry
{
    // Lower-case alphanumeric + underscore. Defence-in-depth even though
    // the only writers are trusted module registrations.
    private static readonly Regex SafeSchemaName = new(
        "^[a-z][a-z0-9_]{0,29}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly SortedSet<string> _schemas = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Schemas => _schemas.ToList();

    /// <summary>
    /// Adds a schema to the dispatch allowlist. Idempotent.
    /// Throws when the schema name fails the safety regex (prevents SQL injection
    /// via schema-name interpolation in the dispatcher).
    /// </summary>
    public void Register(string schemaName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);

        var normalised = schemaName.Trim().ToLowerInvariant();
        if (!SafeSchemaName.IsMatch(normalised))
            throw new ArgumentException(
                $"Outbox schema name '{schemaName}' is invalid. Allowed: ^[a-z][a-z0-9_]{{0,29}}$.",
                nameof(schemaName));

        _schemas.Add(normalised);
    }
}
