using AqtCoreFW.Notifications.Application.Providers;

namespace AqtCoreFW.Tests.SharedFixtures;

// Test double — no webhook HTTP call, returns immediately so integration tests
// can exercise sms-channel delivery paths without hitting an external provider.
public sealed class FakeSmsProvider : ISmsProvider
{
    public Task SendAsync(string phoneNumber, string message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
