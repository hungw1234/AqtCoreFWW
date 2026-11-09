namespace AqtCoreFW.Notifications.Application.Providers;

/// <summary>
/// Email delivery interface — swappable provider (SMTP/SendGrid/SES).
/// SMTP via MailKit is the default implementation.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
}
