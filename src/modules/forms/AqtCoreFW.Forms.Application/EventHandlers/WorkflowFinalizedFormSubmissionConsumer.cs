using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Contracts.Events;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Forms.Application.EventHandlers;

/// <summary>
/// MassTransit consumer for <see cref="WorkflowInstanceFinalizedIntegrationEvent"/>. The event lives
/// in <c>SharedKernel.Contracts/Events</c>, so neither Workflow nor Forms needs a cross-module project
/// reference. Replaces the former in-process <c>WorkflowTransitionedFormSubmissionHandler</c> MediatR
/// notification handler — this consumer applies approve/reject via the outbox instead, so a broker
/// outage or process crash between the transition commit and the status update no longer silently
/// strands a FormSubmission in Pending.
///
/// Delivery: at-least-once — the event is routed through the Outbox off the <c>WorkflowInstance</c>
/// aggregate (captured by <c>OutboxInterceptor</c>), so a redelivery is possible.
///
/// Idempotency by effect, NOT by ledger: this consumer deliberately does NOT use
/// <c>IProcessedMessageStore</c>. A claim-first ledger commits in its own (Messaging) DbContext before
/// the Forms status write runs in a separate context — so if the status write fails after the claim
/// commits, the retry would find the message already-claimed and skip it, silently re-stranding the
/// submission in Pending (the exact R1 failure this change removes). Instead we rely on the effect being
/// naturally idempotent: <see cref="IFormSubmissionStatusClient"/>'s Approve/Reject no-op an
/// already-applied submission (the domain throws, the client swallows it, and no duplicate event is
/// published). So a redelivery after success is a harmless no-op, and — crucially — an infra failure is
/// allowed to propagate so MassTransit's <c>UseMessageRetry</c> re-applies until it succeeds; exhausted
/// retries dead-letter the message (visible), never silently drop the decision.
/// </summary>
public sealed class WorkflowFinalizedFormSubmissionConsumer(
    IFormSubmissionStatusClient statusClient,
    ILogger<WorkflowFinalizedFormSubmissionConsumer> logger)
    : IConsumer<WorkflowInstanceFinalizedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<WorkflowInstanceFinalizedIntegrationEvent> context)
    {
        var evt = context.Message;

        // Defensive — the producer (WorkflowInstance.RaiseFinalizationEvent) already guards this, but
        // a future producer change or hand-crafted message must not silently misroute a non-form event.
        if (evt.EntityType != "FormSubmission")
            return;

        var stateLower = evt.FinalStateName.ToLowerInvariant();

        // Infra exceptions from the status client are intentionally NOT caught — they propagate so
        // MassTransit retries this delivery (durable at-least-once). The client swallows only the benign
        // already-applied domain race internally, so a retry after a partial failure is safe.
        if (stateLower.StartsWith("approved") || stateLower == "approve")
        {
            await statusClient.ApproveSubmissionAsync(
                evt.EntityId, evt.TransitionedBy,
                $"Auto-approved via workflow (state: {evt.FinalStateName})", context.CancellationToken);
        }
        else if (stateLower.StartsWith("rejected") || stateLower == "reject")
        {
            await statusClient.RejectSubmissionAsync(
                evt.EntityId, evt.TransitionedBy,
                $"Rejected via workflow (state: {evt.FinalStateName})", context.CancellationToken);
        }
        else
        {
            logger.LogWarning(
                "Workflow instance {InstancePublicId} completed with unrecognized final state '{StateName}' for FormSubmission {EntityId}. Leaving Pending.",
                evt.InstancePublicId, evt.FinalStateName, evt.EntityId);
        }
    }
}
