using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Contracts.Events;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Notifications.Application.EventHandlers;

/// <summary>
/// MassTransit consumer for <see cref="FormSubmissionAcknowledgedIntegrationEvent"/>. The event lives in
/// <c>SharedKernel.Contracts/Events</c>, so neither Forms nor Notifications needs a cross-module project
/// reference.
///
/// Anonymous public submissions (<c>SubmittedBy = Guid.Empty</c>) are skipped — there is no
/// recipient user to notify. Authenticated submissions receive an in-app acknowledgement.
///
/// Delivery: at-least-once — the event is routed through the Outbox (Slice 1B-1), so a redelivery is
/// possible; this consumer de-duplicates on SubmissionId via <see cref="IProcessedMessageStore"/> (Slice 1C).
///
/// Fire-and-log: notification-client failures are caught and logged. Throwing would trigger MassTransit
/// retry (UseMessageRetry); for a transient Notifications outage we prefer lossy behavior over a retry
/// storm against a degraded dependency.
/// </summary>
public sealed class FormSubmissionAcknowledgedConsumer(
    INotificationModuleClient notificationClient,
    IProcessedMessageStore dedup,
    ILogger<FormSubmissionAcknowledgedConsumer> logger)
    : IConsumer<FormSubmissionAcknowledgedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<FormSubmissionAcknowledgedIntegrationEvent> context)
    {
        var evt = context.Message;

        // Anonymous public submissions have no submitter — skip notification dispatch. No effect, so
        // there is nothing to de-duplicate: claim BELOW this early-return so the anon path never writes
        // a ledger row.
        if (evt.SubmittedBy == Guid.Empty)
        {
            logger.LogInformation(
                "Skipping FormSubmissionAcknowledged notification — anonymous public submission. SubmissionId={SubmissionId}.",
                evt.SubmissionId);
            return;
        }

        // Idempotency (Slice 1C): at-least-once delivery may replay this message. Claim SubmissionId (the
        // contract's documented dedup key) before dispatching so a redelivery does not send a second
        // notification. Claim-first is atomic — no double-send even if two deliveries race. The trade-off:
        // a crash between this claim and the send drops the ack (at-most-once), accepted here for a
        // low-materiality in-app acknowledgement.
        if (!await dedup.TryClaimAsync(
                nameof(FormSubmissionAcknowledgedConsumer), evt.SubmissionId.ToString(), context.CancellationToken))
        {
            logger.LogInformation(
                "Skipping FormSubmissionAcknowledged notification — already processed (duplicate delivery). SubmissionId={SubmissionId}.",
                evt.SubmissionId);
            return;
        }

        try
        {
            await notificationClient.SendAsync(new SendNotificationRequest(
                RecipientUserId: evt.SubmittedBy,
                TenantId: evt.TenantId,
                Subject: "Biểu mẫu đã được tiếp nhận",
                Body: "Biểu mẫu của bạn đã được gửi thành công và đang chờ xử lý.",
                Channel: "inapp",
                CorrelationId: evt.SubmissionId.ToString()),
                context.CancellationToken);
        }
        catch (Exception ex)
        {
            // Fire-and-log: throwing replays the message; transient outages would storm.
            logger.LogWarning(ex,
                "Failed to dispatch FormSubmissionAcknowledged notification. SubmissionId={SubmissionId}, TemplateId={TemplateId}.",
                evt.SubmissionId, evt.FormTemplateId);
        }
    }
}
