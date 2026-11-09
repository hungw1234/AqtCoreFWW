namespace AqtCoreFW.SharedKernel.Contracts.Clients;

/// <summary>
/// Sends an alert email to a literal address.
///
/// Distinct from <see cref="INotificationModuleClient"/>: that contract addresses a recipient by
/// <c>RecipientUserId</c> and requires a <c>TenantId</c>. Infrastructure alerting has neither —
/// an AlertRule stores an address string in NotifyTarget and is system-wide, not tenant-scoped.
///
/// Lives in Notification.Contracts (zero ProjectReferences) so shared infrastructure can consume
/// it without referencing the notifications module's Application/Infrastructure layers.
/// </summary>
public interface IAlertEmailSender
{
    Task SendAsync(
        string toAddress,
        string subject,
        string body,
        CancellationToken cancellationToken = default);
}
