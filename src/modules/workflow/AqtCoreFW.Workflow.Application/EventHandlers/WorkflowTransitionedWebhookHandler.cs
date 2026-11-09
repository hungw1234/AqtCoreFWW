using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Application.Webhooks;
using AqtCoreFW.Workflow.Application.DTOs;
using AqtCoreFW.Workflow.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Workflow.Application.EventHandlers;

/// <summary>
/// Dispatches outbound webhooks on workflow transitions via IWebhookService.
/// Event type: "workflow.transitioned" — subscribers filter by this string.
/// Fire-and-forget: IWebhookService enqueues Hangfire jobs for delivery.
/// Exceptions are swallowed to prevent webhook failures from blocking transitions.
/// Phase-4b B4c: WorkflowTransitionedEvent.InstancePublicId (Guid T2 AK), NewStateInternalId (long T1 PK).
///               SQL filters by PublicId; d.PublicId projected as DefinitionPublicId for webhook payload.
///               FromStateId: long (T1 WorkflowState.Id — no Guid AK); ToStateId: NewStateInternalId (long).
/// </summary>
public sealed class WorkflowTransitionedWebhookHandler(
    IReadDbConnection db,
    IWebhookService webhookService,
    ILogger<WorkflowTransitionedWebhookHandler>? logger = null)
    : INotificationHandler<WorkflowTransitionedEvent>
{
    private const string EventType = "workflow.transitioned";

    public async Task Handle(WorkflowTransitionedEvent notification, CancellationToken cancellationToken)
    {
        // Entire body wrapped in try-catch: this handler is fire-and-forget.
        // DB query failures must NOT propagate and block the parent workflow transition.
        try
        {
            // Filter by PublicId (Guid AK) — InstancePublicId is what the event carries.
            // d.Id = i.DefinitionId is long-on-long (both internal bigint FKs) — join is fine.
            // FromStateId projected as long (T1 WorkflowState.Id — no Guid AK).
            // d.PublicId selected as DefinitionPublicId — T2 entity Guid AK surfaced in payload.
            const string sql = """
                SELECT
                    d.PublicId AS DefinitionPublicId,
                    i.TenantId,
                    h.ActionName,
                    h.FromStateId,
                    d.DefinitionKey,
                    i.EntityType,
                    i.EntityId
                FROM workflow.WorkflowInstances i
                INNER JOIN workflow.WorkflowInstanceHistories h ON h.InstanceId = i.Id
                INNER JOIN workflow.WorkflowDefinitions d ON d.Id = i.DefinitionId
                WHERE i.PublicId = @InstancePublicId
                ORDER BY h.TransitionedAt DESC
                OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY
                """;

            var ctx = await db.QueryFirstOrDefaultAsync<WebhookTransitionContext>(
                sql,
                new { notification.InstancePublicId },
                cancellationToken);

            if (ctx is null) return;

            var payload = new WorkflowTransitionWebhookPayload(
                InstanceId: notification.InstancePublicId,       // T2 PublicId (Guid)
                DefinitionId: ctx.DefinitionPublicId,            // T2 PublicId (Guid AK)
                DefinitionKey: ctx.DefinitionKey,
                FromStateId: ctx.FromStateId,                // long (T1 state — no Guid AK)
                ToStateId: notification.NewStateInternalId,  // long (T1 state — no Guid AK)
                ActionName: ctx.ActionName,
                TransitionedBy: notification.TransitionedBy,
                Timestamp: DateTimeOffset.UtcNow,
                EntityType: ctx.EntityType,
                EntityId: ctx.EntityId);

            await webhookService.DispatchAsync(EventType, payload, ctx.TenantId, cancellationToken);
        }
        catch (Exception ex)
        {
            // Fire-and-forget: log but never propagate — webhook failure must not block transitions
            logger?.LogWarning(ex, "Webhook dispatch failed for instance {InstancePublicId}, event {EventType}",
                notification.InstancePublicId, EventType);
        }
    }

    /// <summary>
    /// Dapper projection for transition context lookup. Internal for test access.
    /// DefinitionPublicId: Guid (T2 WorkflowDefinition.PublicId — surfaced in webhook payload).
    /// FromStateId: long (T1 WorkflowState.Id — no Guid AK).
    /// </summary>
    internal sealed class WebhookTransitionContext
    {
        public Guid DefinitionPublicId { get; set; }
        public Guid TenantId { get; set; }
        public string ActionName { get; set; } = string.Empty;
        public long FromStateId { get; set; }
        public string DefinitionKey { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public Guid EntityId { get; set; }
    }
}
