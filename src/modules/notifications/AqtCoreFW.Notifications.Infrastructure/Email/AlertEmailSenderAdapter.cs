using AqtCoreFW.Notifications.Application.Providers;
using AqtCoreFW.SharedKernel.Contracts.Clients;

namespace AqtCoreFW.Notifications.Infrastructure.Email;

/// <summary>
/// Implements the address-based <see cref="IAlertEmailSender"/> contract over the module's
/// <see cref="IEmailSender"/> provider, so shared infrastructure alerting can send mail without
/// depending on the notifications module.
///
/// Deliberately does not route through SendNotificationCommand: that path persists a
/// Notification row keyed by RecipientUserId, and an AlertRule has an address, not a user.
/// </summary>
public sealed class AlertEmailSenderAdapter(IEmailSender emailSender) : IAlertEmailSender
{
    public Task SendAsync(
        string toAddress,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
        => emailSender.SendAsync(toAddress, subject, body, cancellationToken);
}
