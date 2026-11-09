namespace AqtCoreFW.Infrastructure.Persistence.Outbox;

/// <summary>
/// Resolves an Outbox row's <c>EventType</c> token back to a CLR <see cref="Type"/>.
///
/// The stable token is <see cref="Type.FullName"/> — namespace + type name only, with
/// no assembly version / culture / public-key-token component. Unlike
/// <see cref="Type.AssemblyQualifiedName"/>, it survives an assembly version bump between
/// the time a row is written and the time it's dispatched, so an in-flight deploy no
/// longer silently dead-letters (and loses) every pending event.
/// </summary>
public interface IOutboxEventTypeResolver
{
    /// <summary>
    /// Resolves <paramref name="token"/> to a CLR type, or null when unresolvable.
    /// Callers MUST treat null as fatal for the row (dead-letter) — see
    /// <see cref="OutboxDispatcher"/>.
    /// </summary>
    Type? Resolve(string token);
}
