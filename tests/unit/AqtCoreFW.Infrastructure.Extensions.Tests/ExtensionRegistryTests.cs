using AqtCoreFW.Infrastructure.Extensions;
using AqtCoreFW.SharedKernel.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AqtCoreFW.Infrastructure.Extensions.Tests;

/// <summary>
/// Unit tests for ExtensionRegistry.
/// Verifies DI scanning, key grouping, and priority ordering.
/// </summary>
public sealed class ExtensionRegistryTests
{
    // ── helpers ─────────────────────────────────────────────────────────────

    private static ExtensionRegistry BuildRegistry(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        var sp = services.BuildServiceProvider();
        return new ExtensionRegistry(sp, NullLogger<ExtensionRegistry>.Instance);
    }

    // ── no handlers ─────────────────────────────────────────────────────────

    [Fact]
    public void GetHandlers_NoneRegistered_ReturnsEmpty()
    {
        var registry = BuildRegistry(_ => { });

        var handlers = registry.GetHandlers<string, int>("any.key");

        handlers.Should().BeEmpty();
    }

    // ── single handler found ─────────────────────────────────────────────────

    [Fact]
    public void GetHandlers_SingleRegistered_ReturnsThatHandler()
    {
        var registry = BuildRegistry(svc =>
            svc.AddExtensionHandler<AlphaHandler, string, string>());

        var handlers = registry.GetHandlers<string, string>(RegKeys.Alpha);

        handlers.Should().ContainSingle();
        handlers[0].Should().BeOfType<AlphaHandler>();
    }

    // ── key grouping ──────────────────────────────────────────────────────

    [Fact]
    public void GetHandlers_TwoKeysRegistered_EachKeyIsolated()
    {
        var registry = BuildRegistry(svc =>
        {
            svc.AddExtensionHandler<AlphaHandler, string, string>();
            svc.AddExtensionHandler<BetaHandler, string, string>();
        });

        var alpha = registry.GetHandlers<string, string>(RegKeys.Alpha);
        var beta  = registry.GetHandlers<string, string>(RegKeys.Beta);

        alpha.Should().ContainSingle().Which.Should().BeOfType<AlphaHandler>();
        beta.Should().ContainSingle().Which.Should().BeOfType<BetaHandler>();
    }

    // ── priority ordering ─────────────────────────────────────────────────

    [Fact]
    public void GetHandlers_MultipleHandlersSameKey_OrderedByPriorityAscending()
    {
        var registry = BuildRegistry(svc =>
        {
            svc.AddExtensionHandler<LowPriorityHandler, string, string>();
            svc.AddExtensionHandler<HighPriorityHandler, string, string>();
            svc.AddExtensionHandler<MidPriorityHandler, string, string>();
        });

        var handlers = registry.GetHandlers<string, string>(RegKeys.Multi);

        handlers.Should().HaveCount(3);
        handlers[0].Priority.Should().Be(5);
        handlers[1].Priority.Should().Be(50);
        handlers[2].Priority.Should().Be(500);
    }

    // ── wrong key returns empty ────────────────────────────────────────────

    [Fact]
    public void GetHandlers_WrongKey_ReturnsEmpty()
    {
        var registry = BuildRegistry(svc =>
            svc.AddExtensionHandler<AlphaHandler, string, string>());

        var handlers = registry.GetHandlers<string, string>("no.such.key");

        handlers.Should().BeEmpty();
    }

    // ── wrong type params returns empty ───────────────────────────────────

    [Fact]
    public void GetHandlers_WrongTypeParams_ReturnsEmpty()
    {
        var registry = BuildRegistry(svc =>
            svc.AddExtensionHandler<AlphaHandler, string, string>());

        // AlphaHandler is IExtensionHandler<string,string>; asking for <int,int>
        var handlers = registry.GetHandlers<int, int>(RegKeys.Alpha);

        handlers.Should().BeEmpty();
    }

    // ── test doubles ────────────────────────────────────────────────────────

    private static class RegKeys
    {
        public const string Alpha = "reg.alpha";
        public const string Beta  = "reg.beta";
        public const string Multi = "reg.multi";
    }

    private sealed class AlphaHandler
        : IExtensionHandler<string, string>, IExtensionHandlerMarker
    {
        public string ExtensionPointKey => RegKeys.Alpha;
        public int Priority => 100;
        public Task<string> HandleAsync(string input, CancellationToken ct = default)
            => Task.FromResult("alpha");
    }

    private sealed class BetaHandler
        : IExtensionHandler<string, string>, IExtensionHandlerMarker
    {
        public string ExtensionPointKey => RegKeys.Beta;
        public int Priority => 100;
        public Task<string> HandleAsync(string input, CancellationToken ct = default)
            => Task.FromResult("beta");
    }

    private sealed class HighPriorityHandler
        : IExtensionHandler<string, string>, IExtensionHandlerMarker
    {
        public string ExtensionPointKey => RegKeys.Multi;
        public int Priority => 5;
        public Task<string> HandleAsync(string input, CancellationToken ct = default)
            => Task.FromResult("high");
    }

    private sealed class MidPriorityHandler
        : IExtensionHandler<string, string>, IExtensionHandlerMarker
    {
        public string ExtensionPointKey => RegKeys.Multi;
        public int Priority => 50;
        public Task<string> HandleAsync(string input, CancellationToken ct = default)
            => Task.FromResult("mid");
    }

    private sealed class LowPriorityHandler
        : IExtensionHandler<string, string>, IExtensionHandlerMarker
    {
        public string ExtensionPointKey => RegKeys.Multi;
        public int Priority => 500;
        public Task<string> HandleAsync(string input, CancellationToken ct = default)
            => Task.FromResult("low");
    }
}
