namespace AqtCoreFW.Infrastructure.Persistence.Outbox;

/// <summary>
/// Allowlist of module schemas that own an <c>OutboxMessages</c> table.
/// Populated at startup via <see cref="OutboxSchemaRegistryExtensions.AddOutboxSchema"/>.
/// The dispatcher reads this list to know which schemas to poll.
/// Schema names are interpolated into raw SQL — registry doubles as injection guard.
/// </summary>
public interface IOutboxSchemaRegistry
{
    /// <summary>Distinct, ordered, lower-cased schema names registered for outbox dispatch.</summary>
    IReadOnlyList<string> Schemas { get; }
}
