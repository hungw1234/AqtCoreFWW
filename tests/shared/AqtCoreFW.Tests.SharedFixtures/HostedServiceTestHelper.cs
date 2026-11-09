using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AqtCoreFW.Tests.SharedFixtures;

/// <summary>
/// Drives a single <see cref="IHostedService"/>'s Start/Stop lifecycle through a REAL DI
/// container resolution — never hand-newed. A hand-rolled construction bypasses the exact
/// DI-resolution failure (missing dependency, misconfigured constructor) this helper exists to
/// catch; resolving through the container is what makes a boot-time hosted-service regression
/// surface here instead of slipping to prod startup (the WebAppFixture strip false-green).
///
/// Pair with <see cref="WebAppFixture.HostedServicesToRetain"/> (or an equivalent per-test
/// <c>ConfigureTestServices</c>/<c>WithWebHostBuilder</c> registration) to opt a specific hosted
/// service back into a stripped test host before calling this helper.
/// </summary>
public static class HostedServiceTestHelper
{
    /// <summary>
    /// Resolves the single registered <typeparamref name="THosted"/> instance from
    /// <paramref name="serviceProvider"/>'s <see cref="IHostedService"/> collection, then awaits
    /// <c>StartAsync(ct)</c> followed by <c>StopAsync(ct)</c>.
    ///
    /// Throws <see cref="InvalidOperationException"/> (via <c>Enumerable.Single</c>) if zero or
    /// more than one <typeparamref name="THosted"/> is registered — an ambiguous or missing
    /// target is a test-setup bug, not something to silently tolerate. Any exception thrown by
    /// <c>StartAsync</c>/<c>StopAsync</c> propagates unchanged to the caller.
    /// </summary>
    public static async Task DriveHostedServiceAsync<THosted>(
        IServiceProvider serviceProvider,
        CancellationToken ct = default)
        where THosted : IHostedService
    {
        var hosted = serviceProvider.GetServices<IHostedService>().OfType<THosted>().Single();
        await hosted.StartAsync(ct);
        await hosted.StopAsync(ct);
    }
}
