using FluentResults;
using MediatR;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.Extensions.Logging;
using AqtCoreFW.Notifications.Domain.Entities;
using AqtCoreFW.Notifications.Domain.Repositories;
using AqtCoreFW.Notifications.Domain.ValueObjects;
using AqtCoreFW.Notifications.Application.Providers;

namespace AqtCoreFW.Notifications.Application.Commands.SendNotification;

/// <summary>
/// Sends a notification via the specified channel.
/// Dedup: if TemplateId + CorrelationId combination already logged, skips dispatch (returns existing concept).
/// Channel routing: InApp → DB insert; Email → address resolved from the recipient's user id, then
/// IEmailSender; SMS → phone number resolved from the same id, then ISmsProvider.
/// TemplateId is Guid PublicId (T2 AK) — resolved to internal long Id before FK storage.
/// </summary>
public sealed class SendNotificationCommandHandler(
    IGuidGenerator guidGen,
    INotificationRepository notificationRepository,
    INotificationLogRepository notificationLogRepository,
    INotificationTemplateRepository templateRepository,
    IEmailSender emailSender,
    ISmsProvider smsProvider,
    IIdentityModuleClient identityClient,
    ILogger<SendNotificationCommandHandler> logger,
    ICurrentUser currentUser) : IRequestHandler<SendNotificationCommand, Result<Guid>>
{
    /// <summary>
    /// Resolves the recipient's mailbox from their user id. The address is deliberately not stored
    /// on the Notification row — it is read at send time so a changed mailbox takes effect at once.
    /// Fails rather than throws: a missing user or a user without an email is a data problem that
    /// no retry can fix, and the callers are retrying event handlers.
    /// </summary>
    private async Task<Result<string>> ResolveRecipientEmailAsync(
        SendNotificationCommand request, CancellationToken cancellationToken)
    {
        var users = await identityClient.GetUserInfoByIdsAsync(
            [request.RecipientUserId], request.TenantId, cancellationToken);

        if (!users.TryGetValue(request.RecipientUserId, out var user))
            return Result.Fail($"Recipient user {request.RecipientUserId} not found.");

        if (string.IsNullOrWhiteSpace(user.Email))
            return Result.Fail($"Recipient user {request.RecipientUserId} has no email address.");

        return Result.Ok(user.Email);
    }

    /// <summary>
    /// Resolves the recipient's phone number from their user id, on the same terms as the mailbox
    /// above: read at send time, and failed rather than thrown so a retrying event handler is not
    /// made to retry a data problem it cannot fix.
    /// </summary>
    private async Task<Result<string>> ResolveRecipientPhoneAsync(
        SendNotificationCommand request, CancellationToken cancellationToken)
    {
        var phoneNumbers = await identityClient.GetUserPhoneNumbersByIdsAsync(
            [request.RecipientUserId], request.TenantId, cancellationToken);

        if (!phoneNumbers.TryGetValue(request.RecipientUserId, out var phoneNumber))
            return Result.Fail($"Recipient user {request.RecipientUserId} not found.");

        if (string.IsNullOrWhiteSpace(phoneNumber))
            return Result.Fail($"Recipient user {request.RecipientUserId} has no phone number.");

        return Result.Ok(phoneNumber);
    }

    public async Task<Result<Guid>> Handle(SendNotificationCommand request, CancellationToken cancellationToken)
    {
        // Resolve Guid PublicId → internal long Id (stays as long FK on Notification + NotificationLog)
        long? resolvedTemplateId = null;
        if (request.TemplateId.HasValue)
        {
            resolvedTemplateId = await templateRepository.GetInternalIdByPublicIdAsync(
                request.TemplateId.Value, request.TenantId, cancellationToken);
            if (resolvedTemplateId is null)
                return Result.Fail($"Notification template {request.TemplateId.Value} not found.");
        }

        // Dedup check: if templateId + recipientId + correlationId already sent, skip
        if (resolvedTemplateId.HasValue && !string.IsNullOrEmpty(request.CorrelationId))
        {
            var isDuplicate = await notificationLogRepository.ExistsAsync(
                resolvedTemplateId.Value, request.RecipientUserId, request.CorrelationId, cancellationToken);
            if (isDuplicate)
                return Result.Fail($"Notification already sent (template={request.TemplateId}, correlationId={request.CorrelationId}).");
        }

        var channel = NotificationChannel.From(request.Channel);

        var notification = Notification.Create(
            guidGen,
            request.TenantId,
            request.RecipientUserId,
            request.Subject,
            request.Body,
            channel,
            currentUser.UserId,
            resolvedTemplateId,
            request.CorrelationId);

        await notificationRepository.AddAsync(notification, cancellationToken);

        // Dispatch per channel
        try
        {
            if (channel == NotificationChannel.Email)
            {
                // The recipient is addressed by id; only Identity knows the mailbox behind it.
                // Resolved inside the Email branch so the InApp path keeps its zero-lookup cost.
                var recipientEmail = await ResolveRecipientEmailAsync(request, cancellationToken);
                if (recipientEmail.IsFailed)
                {
                    // The cross-module client returns Task, so callers cannot see this Result — without
                    // a log the failure would leave nothing but a Failed row nobody queries.
                    logger.LogWarning(
                        "Notification {NotificationId} not sent: {Reason}",
                        notification.PublicId,
                        string.Join("; ", recipientEmail.Errors.Select(e => e.Message)));

                    notification.MarkAsFailed();
                    await notificationRepository.UpdateAsync(notification, cancellationToken);
                    return recipientEmail.ToResult();
                }

                await emailSender.SendAsync(recipientEmail.Value, request.Subject, request.Body, cancellationToken);
            }
            else if (channel == NotificationChannel.Sms)
            {
                // Same shape as the Email branch: the recipient is addressed by id, only Identity
                // knows the number behind it, and the lookup stays inside the branch so InApp keeps
                // its zero-lookup cost.
                var recipientPhone = await ResolveRecipientPhoneAsync(request, cancellationToken);
                if (recipientPhone.IsFailed)
                {
                    logger.LogWarning(
                        "Notification {NotificationId} not sent: {Reason}",
                        notification.PublicId,
                        string.Join("; ", recipientPhone.Errors.Select(e => e.Message)));

                    notification.MarkAsFailed();
                    await notificationRepository.UpdateAsync(notification, cancellationToken);
                    return recipientPhone.ToResult();
                }

                await smsProvider.SendAsync(recipientPhone.Value, request.Body, cancellationToken);
            }
            // InApp: saved to DB above; SignalR push handled by NotificationCreatedEvent handler

            notification.MarkAsSent();
            await notificationRepository.UpdateAsync(notification, cancellationToken);
        }
        catch (Exception)
        {
            notification.MarkAsFailed();
            await notificationRepository.UpdateAsync(notification, cancellationToken);
            throw;
        }

        // Log dedup record using resolved internal long Id
        if (resolvedTemplateId.HasValue && !string.IsNullOrEmpty(request.CorrelationId))
        {
            var log = NotificationLog.Create(guidGen, resolvedTemplateId.Value, request.RecipientUserId, request.CorrelationId);
            await notificationLogRepository.AddAsync(log, cancellationToken);
        }

        // T2: return PublicId (Guid AK) — not internal long Id
        return Result.Ok(notification.PublicId);
    }
}
