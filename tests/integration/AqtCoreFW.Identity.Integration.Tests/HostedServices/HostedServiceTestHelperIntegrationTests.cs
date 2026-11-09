using AqtCoreFW.Identity.Infrastructure.Authorization;
using AqtCoreFW.SharedKernel.Application.Hosting;
using AqtCoreFW.Tests.SharedFixtures;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using StackExchange.Redis;

namespace AqtCoreFW.Identity.Integration.Tests.HostedServices;

/// <summary>
/// Proof tests for <see cref="HostedServiceTestHelper"/> and the
/// <see cref="WebAppFixture.HostedServicesToRetain"/> opt-in + <see cref="HostedServiceStripFilter"/>
/// retain predicate.
///
/// Closes the false-green where WebAppFixture strips every non-seeder IHostedService, so a
/// boot-time hosted-service regression (missing DI registration, throwing StartAsync) never
/// surfaces in integration tests even though it would crash prod startup.
///
/// IMPORTANT mechanics note (found empirically while writing these tests): ASP.NET Core's
/// Generic Host auto-starts EVERY registered <see cref="IHostedService"/> the moment
/// <c>WebApplicationFactory.Services</c>/<c>CreateClient()</c> is first accessed — a retained
/// (non-stripped) hosted service therefore already runs its StartAsync as part of normal host
/// boot, BEFORE any test code gets a chance to call <see cref="HostedServiceTestHelper"/> itself.
/// Proof #1 (a throwing fake with no real dependencies) uses a minimal, purpose-built DI
/// container instead of the full WebAppFixture host so the helper — not host boot — is what the
/// assertion observes. Proof #2 legitimately needs the FULL app host (to prove the REAL
/// production DI graph resolves a real service), so it keeps WebAppFixture and neutralises the
/// host's own auto-start by substituting only the Redis dependency. Proof #3 tests
/// <see cref="HostedServiceStripFilter.ShouldRetain"/> directly (a pure function) rather than
/// end-to-end: a descriptor added via <c>WithWebHostBuilder(builder => builder.ConfigureServices(...))</c>
/// empirically lands in the container AFTER WebAppFixture's own strip loop already ran, so it
/// would never be a genuine subject of the predicate at all.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class HostedServiceTestHelperIntegrationTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public HostedServiceTestHelperIntegrationTests(WebAppFixture app)
    {
        _app = app;
    }

    // -------------------------------------------------------------------------
    // Proof #1: a throwing StartAsync, resolved through a real DI container, surfaces via the
    // helper. Uses a minimal ServiceCollection (not WebAppFixture) — the fake has no production
    // dependencies to verify, and a minimal container avoids the Generic Host's own auto-start
    // racing the helper's explicit drive (see class doc).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DriveHostedServiceAsync_ThrowingFakeHostedService_PropagatesException()
    {
        var services = new ServiceCollection();
        services.AddHostedService<ThrowingFakeHostedService>();
        await using var sp = services.BuildServiceProvider();

        var act = () => HostedServiceTestHelper.DriveHostedServiceAsync<ThrowingFakeHostedService>(sp);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*fake StartAsync failure*",
                because: "the helper must resolve the real DI-registered instance (GetServices<IHostedService>().OfType<T>().Single(), never hand-newed) and surface its StartAsync exception, not swallow it");
    }

    // -------------------------------------------------------------------------
    // Proof #2: AbacCacheSubscriber (real production hosted service) DI-resolution smoke through
    // the FULL app host. AbacCacheSubscriber swallows subscribe failures with a 5-attempt
    // retry+backoff (5s→60s) and NEVER throws from StartAsync (see its own header doc), but that
    // retry loop has real wall-clock cost the test must not pay: the Generic Host auto-starts
    // every retained hosted service the moment factory.Services is accessed, using its OWN
    // (uncancellable-by-us) token, so ONLY the Redis dependency — not AbacCacheSubscriber
    // itself — is substituted, letting the FIRST subscribe attempt succeed deterministically and
    // instantly. This still resolves AbacCacheSubscriber's constructor (IConnectionMultiplexer,
    // IMemoryCache, ILogger<T>) through the real production DI graph — proving that graph is
    // wired correctly — while keeping the test fast and deterministic.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DriveHostedServiceAsync_AbacCacheSubscriber_CompletesLifecycleThroughRealContainer()
    {
        _app.HostedServicesToRetain.Add(typeof(AbacCacheSubscriber));

        var fastSubscriber = Substitute.For<ISubscriber>();
        fastSubscriber
            .SubscribeAsync(Arg.Any<RedisChannel>(), Arg.Any<Action<RedisChannel, RedisValue>>())
            .Returns(Task.CompletedTask);
        var fastRedis = Substitute.For<IConnectionMultiplexer>();
        fastRedis.GetSubscriber(Arg.Any<object>()).Returns(fastSubscriber);

        var factory = _app.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IConnectionMultiplexer>();
                services.AddSingleton(fastRedis);
            }));

        using var scope = factory.Services.CreateScope();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var act = () => HostedServiceTestHelper.DriveHostedServiceAsync<AbacCacheSubscriber>(scope.ServiceProvider, cts.Token);

        await act.Should().NotThrowAsync(
            because: "AbacCacheSubscriber is registered via AddHostedService<AbacCacheSubscriber>() in " +
                      "production DI — resolving + running Start/StopAsync through the real container must " +
                      "not throw a DI-resolution error");
    }

    // -------------------------------------------------------------------------
    // Proof #3: strip-predicate hardening — a factory-lambda-registered IIntegrationTestSeeder
    // whose static return type collapses to IHostedService must be RETAINED, not stripped.
    //
    // Direct unit test of HostedServiceStripFilter.ShouldRetain rather than an end-to-end
    // WebAppFixture scenario: an earlier attempt tried injecting the fake seeder via
    // WithWebHostBuilder(builder => builder.ConfigureServices(...)), but empirically (verified
    // with a temporary diagnostic) that descriptor never reached the strip loop at all — it lands
    // in the container AFTER WebAppFixture's own ConfigureTestServices-based strip loop already
    // ran, so it would "survive" trivially regardless of the predicate. ShouldRetain is a pure,
    // stateless function — testing it directly against a hand-built ServiceDescriptor
    // reproducing the exact pathological shape is simpler and fully deterministic.
    // -------------------------------------------------------------------------

    [Fact]
    public void ShouldRetain_FactoryLambdaRegisteredSeederDescriptor_ReturnsTrue()
    {
        // Reproduces AddSingleton<IHostedService>(sp => ...) / AddHostedService<IHostedService>(sp => ...):
        // the delegate variable's declared type pins IHostedService as the factory's return type,
        // so ImplementationType/ImplementationInstance are both null and
        // ImplementationFactory.Method.ReturnType is IHostedService itself — not the concrete
        // FactoryLambdaRegisteredFakeSeeder — exactly the impl-type-inference gap the hardened
        // predicate must fail-safe on.
        Func<IServiceProvider, IHostedService> factory = _ => new FactoryLambdaRegisteredFakeSeeder();
        var descriptor = ServiceDescriptor.Singleton(factory);

        descriptor.ImplementationType.Should().BeNull(
            because: "this test only proves what it claims to prove if the pathological shape is reproduced");
        descriptor.ImplementationFactory!.Method.ReturnType.Should().Be(typeof(IHostedService),
            because: "this test only proves what it claims to prove if the pathological shape is reproduced");

        var retained = HostedServiceStripFilter.ShouldRetain(descriptor, Array.Empty<Type>());

        retained.Should().BeTrue(
            because: "a factory-lambda-registered IIntegrationTestSeeder must survive the strip filter " +
                      "even though its ImplementationFactory return type collapses to IHostedService — " +
                      "stripping it would silently re-open the false-green this filter exists to prevent");
    }

    // -------------------------------------------------------------------------
    // Proof #4 (strip-branch regression guard): a factory-registered CONCRETE non-seeder hosted
    // service — the shape Hangfire's background job server uses
    // (AddTransient<IHostedService, TConcrete>(factory): ImplementationType null, but
    // ImplementationFactory.Method.ReturnType is the recoverable concrete type) — must be STRIPPED,
    // not retained. This is the counterpart to proof #3: proof #3 covers the interface-pinned
    // collapse case that must fail-safe RETAIN; this covers the concrete-typed case that must
    // STRIP. An over-broad predicate that retained ANY factory registration would silently boot
    // Hangfire's job server in every fixture host and execute enqueued jobs mid-test.
    // -------------------------------------------------------------------------

    [Fact]
    public void ShouldRetain_FactoryRegisteredConcreteNonSeederDescriptor_ReturnsFalse()
    {
        // Reproduces AddHostedService(sp => new ConcreteNonSeederHostedService()) / Hangfire's
        // AddTransient<IHostedService, TConcrete>(factory): ImplementationType is null but the
        // delegate's return type is the concrete service, so Method.ReturnType is recoverable —
        // unlike the interface-pinned shape in proof #3 whose return type collapses to IHostedService.
        Func<IServiceProvider, ConcreteNonSeederHostedService> factory = _ => new ConcreteNonSeederHostedService();
        var descriptor = ServiceDescriptor.Singleton<IHostedService, ConcreteNonSeederHostedService>(factory);

        descriptor.ImplementationType.Should().BeNull(
            because: "this test only proves what it claims to prove if the factory shape is reproduced");
        descriptor.ImplementationFactory!.Method.ReturnType.Should().Be(typeof(ConcreteNonSeederHostedService),
            because: "a concrete-typed factory keeps its return type recoverable — the whole point that lets the predicate strip it precisely");

        var retained = HostedServiceStripFilter.ShouldRetain(descriptor, Array.Empty<Type>());

        retained.Should().BeFalse(
            because: "a factory-registered concrete non-seeder (e.g. Hangfire's job server) must be stripped — " +
                      "retaining it would boot the job server in every fixture host and race enqueued jobs against test assertions");
    }

    /// <summary>Test-only non-seeder IHostedService for the factory-registered strip regression guard (proof #4).</summary>
    private sealed class ConcreteNonSeederHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Test-only IHostedService whose StartAsync always throws — proves the helper surfaces it.</summary>
    private sealed class ThrowingFakeHostedService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
            => throw new InvalidOperationException("fake StartAsync failure — proves propagation through HostedServiceTestHelper");

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Test-only seeder registered via a generic-pinned factory lambda (see proof #3).</summary>
    private sealed class FactoryLambdaRegisteredFakeSeeder : IIntegrationTestSeeder
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
