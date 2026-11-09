using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Contracts.Events;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Audit.Application.EventHandlers;

/// <summary>
/// MassTransit consumer for <see cref="AuditLogAppendedIntegrationEvent"/>.
///
/// CURRENT BEHAVIOR — NOOP. The sync write path in <c>AuditService.LogAsync</c> is
/// authoritative; this event is dual-written purely as preparation for future
/// microservice extraction. Once Audit becomes a separate service, the sync caller
/// disappears and this consumer becomes the read-side projection writer.
///
/// Idempotency: <c>AuditEntryId</c> is the dedup key. Today there is nothing to
/// dedup against (no projection table). When this consumer is activated, it MUST
/// look up the audit entry by id and skip if already projected.
///
/// Logging at Debug level — every audit log entry triggers this consumer, so Info
/// would dominate logs. Switch to Information once activated for read-side projection.
/// </summary>
public sealed class AuditLogAppendedConsumer(
    IProcessedMessageStore dedup,
    ILogger<AuditLogAppendedConsumer> logger)
    : IConsumer<AuditLogAppendedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<AuditLogAppendedIntegrationEvent> context)
    {
        var evt = context.Message;

        // Idempotency ledger (Slice 1C): claim AuditEntryId — the contract's documented dedup key — so a
        // redelivery is a no-op. The projection body is a NOOP today (the sync AuditService write is
        // authoritative), so this claim currently only records the ledger entry; when this consumer is
        // activated as the read-side projection writer it will already de-dup by this same key.
        if (!await dedup.TryClaimAsync(
                nameof(AuditLogAppendedConsumer), evt.AuditEntryId.ToString(), context.CancellationToken))
            return;

        logger.LogDebug(
            "Received AuditLogAppendedIntegrationEvent (noop). AuditEntryId={AuditEntryId}, Action={Action}, Module={Module}, ResourceType={ResourceType}.",
            evt.AuditEntryId, evt.Action, evt.ModuleName, evt.ResourceType);
    }
}
