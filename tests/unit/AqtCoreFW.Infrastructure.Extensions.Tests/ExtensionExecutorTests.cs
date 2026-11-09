using AqtCoreFW.Infrastructure.Extensions;
using AqtCoreFW.SharedKernel.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AqtCoreFW.Infrastructure.Extensions.Tests;

/// <summary>
/// Unit tests for ExtensionExecutor.
/// Uses inline test-double handlers — no mocking framework needed.
/// </summary>
public sealed class ExtensionExecutorTests
{
    // ── helpers ─────────────────────────────────────────────────────────────

    private static (ExtensionExecutor executor, ExtensionRegistry registry) Build(
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        configure?.Invoke(services);

        var sp = services.BuildServiceProvider();
        var config = new ConfigurationBuilder().Build();
        var registry = new ExtensionRegistry(sp, NullLogger<ExtensionRegistry>.Instance);
        var executor = new ExtensionExecutor(registry, config, NullLogger<ExtensionExecutor>.Instance);
        return (executor, registry);
    }

    private static IConfiguration ConfigWith(int timeoutSeconds) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Extensions:HandlerTimeoutSeconds"] = timeoutSeconds.ToString()
            })
            .Build();

    // ── no handlers ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_NoHandlersRegistered_ReturnsEmpty()
    {
        var (executor, _) = Build();

        var results = await executor.ExecuteAsync<string, int>("unknown.key", "input");

        results.Should().BeEmpty();
    }

    // ── single handler ───────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_SingleHandler_ReturnsItsResult()
    {
        var (executor, _) = Build(svc =>
            svc.AddExtensionHandler<EchoLengthHandler, string, int>());

        var results = await executor.ExecuteAsync<string, int>(TestKeys.Echo, "hello");

        results.Should().ContainSingle().Which.Should().Be(5);
    }

    // ── multiple handlers ordered by priority ───────────────────────────────

    [Fact]
    public async Task Execute_MultipleHandlers_ReturnedInPriorityOrder()
    {
        var (executor, _) = Build(svc =>
        {
            svc.AddExtensionHandler<PriorityHandler200, string, string>();
            svc.AddExtensionHandler<PriorityHandler10, string, string>();
            svc.AddExtensionHandler<PriorityHandler50, string, string>();
        });

        var results = await executor.ExecuteAsync<string, string>(TestKeys.Priority, "x");

        // Priority 10 < 50 < 200
        results.Should().Equal("p10", "p50", "p200");
    }

    // ── exception isolation ──────────────────────────────────────────────────

    [Fact]
    public async Task Execute_OneHandlerThrows_OtherHandlersStillExecute()
    {
        var (executor, _) = Build(svc =>
        {
            svc.AddExtensionHandler<ThrowingHandler, string, string>();
            svc.AddExtensionHandler<SucceedingHandler, string, string>();
        });

        var results = await executor.ExecuteAsync<string, string>(TestKeys.Fault, "x");

        // The throwing handler's result is absent; succeeding handler's result is present
        results.Should().ContainSingle().Which.Should().Be("ok");
    }

    // ── timeout isolation ────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_HandlerExceedsTimeout_SkippedOtherHandlersStillExecute()
    {
        var services = new ServiceCollection();
        services.AddExtensionHandler<SlowHandler, string, string>();
        services.AddExtensionHandler<FastHandler, string, string>();
        var sp = services.BuildServiceProvider();

        // 1-second timeout; SlowHandler delays 10 s
        var config = ConfigWith(1);
        var registry = new ExtensionRegistry(sp, NullLogger<ExtensionRegistry>.Instance);
        var executor = new ExtensionExecutor(registry, config, NullLogger<ExtensionExecutor>.Instance);

        var results = await executor.ExecuteAsync<string, string>(TestKeys.Timeout, "x");

        results.Should().ContainSingle().Which.Should().Be("fast");
    }

    // ── test doubles ────────────────────────────────────────────────────────

    private static class TestKeys
    {
        public const string Echo     = "test.echo";
        public const string Priority = "test.priority";
        public const string Fault    = "test.fault";
        public const string Timeout  = "test.timeout";
    }

    private sealed class EchoLengthHandler
        : IExtensionHandler<string, int>, IExtensionHandlerMarker
    {
        public string ExtensionPointKey => TestKeys.Echo;
        public int Priority => 100;
        public Task<int> HandleAsync(string input, CancellationToken ct = default)
            => Task.FromResult(input.Length);
    }

    private sealed class PriorityHandler10
        : IExtensionHandler<string, string>, IExtensionHandlerMarker
    {
        public string ExtensionPointKey => TestKeys.Priority;
        public int Priority => 10;
        public Task<string> HandleAsync(string input, CancellationToken ct = default)
            => Task.FromResult("p10");
    }

    private sealed class PriorityHandler50
        : IExtensionHandler<string, string>, IExtensionHandlerMarker
    {
        public string ExtensionPointKey => TestKeys.Priority;
        public int Priority => 50;
        public Task<string> HandleAsync(string input, CancellationToken ct = default)
            => Task.FromResult("p50");
    }

    private sealed class PriorityHandler200
        : IExtensionHandler<string, string>, IExtensionHandlerMarker
    {
        public string ExtensionPointKey => TestKeys.Priority;
        public int Priority => 200;
        public Task<string> HandleAsync(string input, CancellationToken ct = default)
            => Task.FromResult("p200");
    }

    private sealed class ThrowingHandler
        : IExtensionHandler<string, string>, IExtensionHandlerMarker
    {
        public string ExtensionPointKey => TestKeys.Fault;
        public int Priority => 10;
        public Task<string> HandleAsync(string input, CancellationToken ct = default)
            => throw new InvalidOperationException("simulated failure");
    }

    private sealed class SucceedingHandler
        : IExtensionHandler<string, string>, IExtensionHandlerMarker
    {
        public string ExtensionPointKey => TestKeys.Fault;
        public int Priority => 20;
        public Task<string> HandleAsync(string input, CancellationToken ct = default)
            => Task.FromResult("ok");
    }

    private sealed class SlowHandler
        : IExtensionHandler<string, string>, IExtensionHandlerMarker
    {
        public string ExtensionPointKey => TestKeys.Timeout;
        public int Priority => 10;
        public async Task<string> HandleAsync(string input, CancellationToken ct = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return "slow";
        }
    }

    private sealed class FastHandler
        : IExtensionHandler<string, string>, IExtensionHandlerMarker
    {
        public string ExtensionPointKey => TestKeys.Timeout;
        public int Priority => 20;
        public Task<string> HandleAsync(string input, CancellationToken ct = default)
            => Task.FromResult("fast");
    }
}
