using AqtCoreFW.SharedKernel.Application.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AqtCoreFW.Tests.SharedFixtures;

/// <summary>
/// Shared IHostedService strip-predicate used by both <see cref="WebAppFixture"/> and
/// <c>SeederSmokeFixture</c> (the two loops were byte-identical duplicates — DRY-extracted here).
/// Both fixtures strip non-seeder hosted services (Hangfire, MassTransit, OutboxProcessor,
/// pub/sub, polling) to avoid test noise, while keeping <see cref="IIntegrationTestSeeder"/>-marked
/// services so seeder regressions surface in tests instead of slipping to prod startup
/// (the false-green pattern this whole filter exists to prevent).
/// </summary>
public static class HostedServiceStripFilter
{
    /// <summary>
    /// True when a registered <see cref="IHostedService"/> descriptor should be RETAINED (kept
    /// booting) in a stripped test host, rather than removed.
    ///
    /// Implementation type is resolved from (in order) <c>ImplementationType</c>,
    /// <c>ImplementationInstance</c>'s runtime type, then a factory delegate's
    /// <c>Method.ReturnType</c>. A factory registered with the concrete service as its generic
    /// argument (<c>AddHostedService(sp => new FooService())</c> or
    /// <c>AddHostedService&lt;Foo&gt;(sp => ...)</c>) still resolves to the concrete type this way,
    /// so those are decided precisely — retained only when opted in via <paramref name="retainTypes"/>
    /// (see <see cref="WebAppFixture.HostedServicesToRetain"/>) or when the type implements
    /// <see cref="IIntegrationTestSeeder"/>; every other concrete non-seeder (Hangfire's background
    /// job server, MassTransit, the outbox processor, pub/sub subscribers, polling services) is
    /// stripped, exactly as before this predicate was extracted.
    ///
    /// The ONLY fail-safe-retain case is a factory whose generic argument at the registration call
    /// site is pinned to the interface (<c>services.AddSingleton&lt;IHostedService&gt;(sp => ...)</c>):
    /// there the delegate's <c>Method.ReturnType</c> collapses to <see cref="IHostedService"/> itself
    /// and the concrete type is unrecoverable, so a seeder registered this way is indistinguishable
    /// from a noise service. Retain it — a wrongly-stripped seeder silently re-opens the exact
    /// false-green this filter exists to prevent, and this ambiguous shape is narrow enough that
    /// retaining it costs only test time. Note the earlier over-broad form of this predicate (retain
    /// ANY factory registration) wrongly kept Hangfire's job server booting in every fixture host.
    /// </summary>
    public static bool ShouldRetain(ServiceDescriptor descriptor, IReadOnlyCollection<Type> retainTypes)
    {
        var implType = descriptor.ImplementationType
            ?? descriptor.ImplementationInstance?.GetType()
            ?? descriptor.ImplementationFactory?.Method.ReturnType;

        // Concrete implementation type is known (including a factory whose return type is the
        // concrete service): decide precisely — opt-in or seeder retains, everything else strips.
        if (implType is not null && implType != typeof(IHostedService))
        {
            return retainTypes.Contains(implType) || typeof(IIntegrationTestSeeder).IsAssignableFrom(implType);
        }

        // Return type collapsed to IHostedService itself (interface-pinned factory) — concrete type
        // unrecoverable, cannot prove non-seeder, so fail-safe retain. Only genuinely-ambiguous
        // factory registrations reach here; all concrete cases were resolved above.
        return descriptor.ImplementationFactory is not null;
    }
}
