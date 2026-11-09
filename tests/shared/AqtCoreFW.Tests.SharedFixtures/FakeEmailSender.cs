using AqtCoreFW.Notifications.Application.Providers;

namespace AqtCoreFW.Tests.SharedFixtures;

// Test double — no SMTP connection, returns immediately so integration tests
// can exercise email-channel delivery paths without hitting a real mail server.
public sealed class FakeEmailSender : IEmailSender
{
    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
