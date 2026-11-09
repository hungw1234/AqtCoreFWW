using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Workflow.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Workflow.Application.EventHandlers;

/// <summary>
/// Sends notifications when parallel approval tasks are auto-created.
/// Matches notification configs with ActionName = "BranchApprovalCreated".
/// If task has AssigneeId, notifies assignee. Otherwise uses config's RecipientType/Value.
/// Phase-4b B4c: ParallelApprovalTaskCreatedEvent.InstancePublicId (Guid T2 AK), TaskInternalId (long T1 PK).
///               SQL filters WorkflowInstances by PublicId column.
/// </summary>
public sealed class ParallelApprovalTaskNotificationHandler(
    IReadDbConnection db,
    INotificationModuleClient notificationClient,
    ILogger<ParallelApprovalTaskNotificationHandler> logger)
    : INotificationHandler<ParallelApprovalTaskCreatedEvent>
{
    public async Task Handle(ParallelApprovalTaskCreatedEvent notification, CancellationToken ct)
    {
        // Find the DefinitionId (long internal FK) for this instance by PublicId (Guid AK)
        const string instanceSql = """
            SELECT DefinitionId FROM workflow.WorkflowInstances WHERE PublicId = @InstancePublicId
            """;

        var definitionId = await db.QueryFirstOrDefaultAsync<long?>(
            instanceSql, new { notification.InstancePublicId }, ct);

        if (definitionId is null) return;

        // Load notification configs for "BranchApprovalCreated" action
        const string configSql = """
            SELECT Channel, RecipientType, RecipientValue, Subject, BodyTemplate
            FROM workflow.WorkflowNotificationConfigs
            WHERE DefinitionId = @DefinitionId
              AND ActionName = 'BranchApprovalCreated'
              AND IsActive = 1 AND IsDeleted = 0
            """;

        var configs = (await db.QueryAsync<ConfigRow>(
            configSql, new { DefinitionId = definitionId.Value }, ct)).ToList();

        if (configs.Count == 0) return;

        foreach (var config in configs)
        {
            try
            {
                // If task has an assignee, notify them directly; otherwise use config recipient
                var recipientId = notification.AssigneeId
                    ?? (Guid.TryParse(config.RecipientValue, out var parsed) ? parsed : (Guid?)null);

                if (recipientId is null) continue;

                var subject = (config.Subject ?? "Phê duyệt song song")
                    .Replace("{{TaskTitle}}", notification.TaskTitle);
                var body = (config.BodyTemplate ?? "Bạn có một nhiệm vụ phê duyệt: {{TaskTitle}}")
                    .Replace("{{TaskTitle}}", notification.TaskTitle);

                // CorrelationId uses TaskInternalId (long) — internal routing only, not exposed
                await notificationClient.SendAsync(new SendNotificationRequest(
                    recipientId.Value, notification.TenantId,
                    subject, body, config.Channel,
                    $"parallel-approval-{notification.TaskInternalId}"), ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to send parallel approval notification for task {TaskInternalId}.",
                    notification.TaskInternalId);
            }
        }
    }

    private sealed record ConfigRow(
        string Channel, string RecipientType, string? RecipientValue,
        string? Subject, string? BodyTemplate);
}
