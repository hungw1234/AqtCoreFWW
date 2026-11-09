using AqtCoreFW.Notifications.Application.Providers;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Contracts.Events;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Notifications.Application.EventHandlers;

/// <summary>
/// MassTransit consumer for <see cref="AccountAutoDisabledIntegrationEvent"/>.
/// Sends an email alert to the affected user when their account is auto-disabled
/// after exceeding the consecutive lockout escalation threshold (QĐ 742 1.4.d).
///
/// Pattern mirrors <see cref="FormSubmissionAcknowledgedConsumer"/>:
/// event type lives in SharedKernel.Contracts — no cross-module project reference needed.
///
/// Fire-and-log: email failures are caught and swallowed so MassTransit does not
/// retry the message (a retry storm against a degraded SMTP relay would not help the user).
/// Delivery semantics: at-most-once (no Outbox dispatcher on this path today).
/// </summary>
public sealed class AccountAutoDisabledNotificationConsumer(
    IEmailSender emailSender,
    IProcessedMessageStore dedup,
    ILogger<AccountAutoDisabledNotificationConsumer> logger)
    : IConsumer<AccountAutoDisabledIntegrationEvent>
{
    public async Task Consume(ConsumeContext<AccountAutoDisabledIntegrationEvent> context)
    {
        var evt = context.Message;

        // No email address — nothing to send, nothing to de-duplicate. Claim BELOW so this path writes
        // no ledger row.
        if (string.IsNullOrWhiteSpace(evt.Email))
        {
            logger.LogInformation(
                "Skipping AccountAutoDisabled email — no email address for user {UserId}.",
                evt.UserId);
            return;
        }

        // Idempotency (Slice 1C): unlike the outbox-routed events, AccountAutoDisabledIntegrationEvent is
        // a direct-publish record with no MessageId/business-key field — so the dedup key is the natural
        // per-event identity (this user, disabled at this instant). A broker redelivery carries the same
        // payload → same key → no duplicate email. Claim-first does not weaken this security alert's
        // guarantee: the contract is already at-most-once (direct publish, no Outbox), so dedup only
        // suppresses a duplicate — it adds no new loss window this path did not already have.
        var dedupKey = $"{evt.UserId:N}:{evt.AutoDisabledAtUtc.Ticks}";
        if (!await dedup.TryClaimAsync(
                nameof(AccountAutoDisabledNotificationConsumer), dedupKey, context.CancellationToken))
        {
            logger.LogInformation(
                "Skipping AccountAutoDisabled email — already processed (duplicate delivery) for user {UserId}.",
                evt.UserId);
            return;
        }

        var subject = "Tài khoản bị tạm khóa do nhiều lần sai mật khẩu";
        var body =
            $"Tài khoản {evt.UserName} đã bị tự động khóa lúc " +
            $"{evt.AutoDisabledAtUtc:yyyy-MM-dd HH:mm:ss} UTC " +
            $"do {evt.ConsecutiveLockoutCount} lần lockout liên tiếp.\n" +
            "Vui lòng liên hệ quản trị viên để mở khóa.";

        try
        {
            await emailSender.SendAsync(evt.Email, subject, body, context.CancellationToken);

            logger.LogInformation(
                "AccountAutoDisabled alert email sent to {Email} for user {UserId}.",
                PiiLogMasking.MaskEmail(evt.Email), evt.UserId);
        }
        catch (OperationCanceledException)
        {
            // Propagate cancellation — do not swallow, do not fault the consumer.
            throw;
        }
        catch (Exception ex)
        {
            // Fire-and-log: throwing would trigger MassTransit retry against a degraded SMTP relay.
            logger.LogWarning(ex,
                "Failed to send AccountAutoDisabled alert email to {Email} for user {UserId}.",
                PiiLogMasking.MaskEmail(evt.Email), evt.UserId);
        }
    }
}
