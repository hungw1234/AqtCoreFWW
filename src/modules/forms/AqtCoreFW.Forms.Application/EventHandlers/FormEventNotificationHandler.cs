using AqtCoreFW.Forms.Domain.Events;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Forms.Application.EventHandlers;

/// <summary>
/// Handles all three form lifecycle domain events and sends in-app notifications.
///
/// FormSubmitted  → look up the template's CreatedBy (owner/admin) to notify of new pending submission.
///                  Anonymous public submissions (SubmittedBy == Guid.Empty) are skipped.
/// FormApproved   → notify the original submitter that their submission was approved.
/// FormRejected   → notify the original submitter that their submission was rejected.
///
/// Uses Dapper (IReadDbConnection) for read-side lookups — no EF dependency.
/// Notifications are non-fatal: a send failure will not roll back the primary command.
/// </summary>
public sealed class FormEventNotificationHandler(
    IReadDbConnection db,
    INotificationModuleClient notificationClient,
    ILogger<FormEventNotificationHandler> logger)
    : INotificationHandler<FormSubmittedEvent>,
      INotificationHandler<FormApprovedEvent>,
      INotificationHandler<FormRejectedEvent>
{
    // ── FormSubmitted ─────────────────────────────────────────────────────────

    public async Task Handle(FormSubmittedEvent notification, CancellationToken cancellationToken)
    {
        // Skip anonymous public submissions — no known recipient
        if (notification.SubmittedBy == Guid.Empty) return;

        // Notify the form template owner (CreatedBy) that a new submission is pending review
        const string sql = """
            SELECT CreatedBy
            FROM forms.FormTemplates
            WHERE Id = @FormTemplateId AND IsDeleted = 0
            """;

        var row = await db.QueryFirstOrDefaultAsync<TemplateOwnerRow>(
            sql, new { notification.FormTemplateId }, cancellationToken);

        if (row is null || row.CreatedBy == Guid.Empty) return;

        // Do not notify the submitter about their own submission
        if (row.CreatedBy == notification.SubmittedBy) return;

        await SafeSendAsync(
            new SendNotificationRequest(
                RecipientUserId: row.CreatedBy,
                TenantId: notification.TenantId,
                Subject: "Biểu mẫu mới cần xét duyệt",
                Body: $"Một biểu mẫu mới (submission {notification.SubmissionId}) vừa được nộp và đang chờ xét duyệt.",
                Channel: "inapp",
                CorrelationId: $"form:submitted:{notification.SubmissionId}"),
            cancellationToken);
    }

    // ── FormApproved ──────────────────────────────────────────────────────────

    public async Task Handle(FormApprovedEvent notification, CancellationToken cancellationToken)
    {
        var submitter = await GetSubmitterAsync(notification.SubmissionId, cancellationToken);
        if (submitter == Guid.Empty) return;

        await SafeSendAsync(
            new SendNotificationRequest(
                RecipientUserId: submitter,
                TenantId: notification.TenantId,
                Subject: "Biểu mẫu của bạn đã được duyệt",
                Body: $"Biểu mẫu của bạn (submission {notification.SubmissionId}) đã được duyệt.",
                Channel: "inapp",
                CorrelationId: $"form:approved:{notification.SubmissionId}"),
            cancellationToken);
    }

    // ── FormRejected ──────────────────────────────────────────────────────────

    public async Task Handle(FormRejectedEvent notification, CancellationToken cancellationToken)
    {
        var submitter = await GetSubmitterAsync(notification.SubmissionId, cancellationToken);
        if (submitter == Guid.Empty) return;

        await SafeSendAsync(
            new SendNotificationRequest(
                RecipientUserId: submitter,
                TenantId: notification.TenantId,
                Subject: "Biểu mẫu của bạn đã bị từ chối",
                Body: $"Biểu mẫu của bạn (submission {notification.SubmissionId}) đã bị từ chối. Vui lòng kiểm tra lý do và nộp lại.",
                Channel: "inapp",
                CorrelationId: $"form:rejected:{notification.SubmissionId}"),
            cancellationToken);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Returns the submitter's user ID, or Guid.Empty if not found / anonymous.</summary>
    private async Task<Guid> GetSubmitterAsync(Guid submissionId, CancellationToken ct)
    {
        const string sql = """
            SELECT SubmittedBy
            FROM forms.FormSubmissions
            WHERE Id = @SubmissionId AND IsDeleted = 0
            """;

        var row = await db.QueryFirstOrDefaultAsync<SubmissionRow>(
            sql, new { SubmissionId = submissionId }, ct);

        return row?.SubmittedBy ?? Guid.Empty;
    }

    /// <summary>Non-fatal send — a notification failure must not roll back the originating command.</summary>
    private async Task SafeSendAsync(SendNotificationRequest request, CancellationToken ct)
    {
        try
        {
            await notificationClient.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Form notification send failed (channel={Channel}, correlation={CorrelationId}). Non-fatal.",
                request.Channel, request.CorrelationId);
        }
    }

    // ── Internal DTOs for Dapper projection ──────────────────────────────────

    private sealed record TemplateOwnerRow(Guid CreatedBy);
    private sealed record SubmissionRow(Guid SubmittedBy);
}
