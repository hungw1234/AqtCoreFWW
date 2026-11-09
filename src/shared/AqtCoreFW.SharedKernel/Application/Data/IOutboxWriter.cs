using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.SharedKernel.Application.Data;

/// <summary>
/// Module-agnostic escape hatch for enqueuing an <see cref="IExternalDomainEvent"/> into the
/// Outbox when the producing entity is NOT (or is not yet) an
/// <see cref="AqtCoreFW.SharedKernel.Domain.IAggregateRoot"/> captured by the EF
/// <c>OutboxInterceptor</c>. The row is written through the SAME ambient
/// <see cref="ICrossModuleUnitOfWork"/> connection + transaction as the caller's business write,
/// so a rollback of that write also drops the outbox row — no event is ever emitted for a write
/// that never committed.
///
/// Reused across modules (Forms Slice 1B-1, and later Audit/Lockout sub-slices) — callers pass
/// their own module's outbox schema name (e.g. "forms").
/// </summary>
public interface IOutboxWriter
{
    /// <summary>
    /// Persists <paramref name="evt"/> as a pending row in <paramref name="schemaName"/>'s
    /// <c>OutboxMessages</c> table, riding the currently-active <see cref="ICrossModuleUnitOfWork"/>
    /// transaction.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No unit of work is active (caller must call
    /// <see cref="ICrossModuleUnitOfWork.BeginAsync(System.Threading.CancellationToken)"/> first —
    /// this writer is intentionally unusable outside an explicit transaction boundary, since
    /// an event enqueued without one could commit even if the accompanying business write fails), or
    /// <paramref name="schemaName"/> is not registered via <c>AddOutboxSchema(...)</c> (the dispatcher
    /// would never poll it, silently stranding the event forever).
    /// </exception>
    Task EnqueueAsync(IExternalDomainEvent evt, string schemaName, CancellationToken ct = default);
}
