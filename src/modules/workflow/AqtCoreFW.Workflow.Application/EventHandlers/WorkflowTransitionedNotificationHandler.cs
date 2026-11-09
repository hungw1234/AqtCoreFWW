using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Workflow.Domain.Events;
using MediatR;

namespace AqtCoreFW.Workflow.Application.EventHandlers;

/// <summary>
/// MediatR notification handler for WorkflowTransitionedEvent.
/// Looks up active notification configs for the definition + action, resolves each
/// recipient, then sends via INotificationModuleClient (in-process in monolith).
/// Role-based recipient resolution is deferred — only "user" and "creator" types supported.
/// Phase-4b B4c: WorkflowTransitionedEvent.InstancePublicId (Guid, T2 AK). SQL filters by PublicId.
///               DefinitionId in Dapper context is long (internal FK, used only for config lookup).
/// </summary>
public sealed class WorkflowTransitionedNotificationHandler(
    IReadDbConnection db,
    INotificationModuleClient notificationClient)
    : INotificationHandler<WorkflowTransitionedEvent>
{
    public async Task Handle(WorkflowTransitionedEvent notification, CancellationToken cancellationToken)
    {
        // Fetch instance context (DefinitionId long internal FK, TenantId) and latest history ActionName.
        // Filter by PublicId (Guid AK) since InstancePublicId is what the event carries.
        const string instanceSql = """
            SELECT i.DefinitionId, i.TenantId, h.ActionName
            FROM workflow.WorkflowInstances i
            INNER JOIN workflow.WorkflowInstanceHistories h ON h.InstanceId = i.Id
            WHERE i.PublicId = @InstancePublicId
            ORDER BY h.TransitionedAt DESC
            OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY
            """;

        var ctx = await db.QueryFirstOrDefaultAsync<TransitionContext>(
            instanceSql,
            new { notification.InstancePublicId },
            cancellationToken);

        if (ctx is null) return;

        // Load active notification configs matching this definition + action.
        // DefinitionId is long (internal FK) — used for DB join, not exposed externally.
        const string configSql = """
            SELECT Id, Channel, RecipientType, RecipientValue, Subject, BodyTemplate
            FROM workflow.WorkflowNotificationConfigs
            WHERE DefinitionId = @DefinitionId
              AND ActionName = @ActionName
              AND IsActive = 1
              AND IsDeleted = 0
            """;

        var configs = await db.QueryAsync<NotificationConfigRow>(
            configSql,
            new { ctx.DefinitionId, ctx.ActionName },
            cancellationToken);

        foreach (var config in configs)
        {
            var recipientId = ResolveRecipient(config.RecipientType, config.RecipientValue, notification);
            if (recipientId == Guid.Empty) continue;

            await notificationClient.SendAsync(
                new SendNotificationRequest(
                    RecipientUserId: recipientId,
                    TenantId: ctx.TenantId,
                    Subject: config.Subject,
                    Body: config.BodyTemplate,
                    Channel: config.Channel,
                    CorrelationId: $"wf:{notification.InstancePublicId}:{ctx.ActionName}:{config.Channel}"),
                cancellationToken);
        }
    }

    /// <summary>
    /// Resolves recipient user ID from config type/value.
    /// "creator" → actor who fired the transition (simplified proxy).
    /// "user"    → explicit user GUID stored in RecipientValue.
    /// "role"    → deferred (requires IUserService — not yet available cross-module).
    /// </summary>
    private static Guid ResolveRecipient(
        string type, string value, WorkflowTransitionedEvent evt) =>
        type.ToLowerInvariant() switch
        {
            "creator" => evt.TransitionedBy,
            "user" => Guid.TryParse(value, out var uid) ? uid : Guid.Empty,
            _ => Guid.Empty  // "role" resolution deferred
        };

    // Internal DTOs for Dapper projection — not exposed outside handler.
    // DefinitionId is long (internal FK); not the public AK.
    internal sealed class TransitionContext
    {
        public long DefinitionId { get; set; }
        public Guid TenantId { get; set; }
        public string ActionName { get; set; } = string.Empty;
    }

    internal sealed record NotificationConfigRow(
        long Id, string Channel, string RecipientType, string RecipientValue,
        string Subject, string BodyTemplate);
}
