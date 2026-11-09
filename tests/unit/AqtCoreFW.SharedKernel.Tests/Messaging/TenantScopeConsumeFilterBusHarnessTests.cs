using AqtCoreFW.Infrastructure.Messaging;
using AqtCoreFW.Infrastructure.Security;
using AqtCoreFW.SharedKernel.Domain.Events;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.SharedKernel.Tests.Messaging;

/// <summary>
/// Bus-level proof that <c>UseConsumeFilter(typeof(TenantScopeConsumeFilter&lt;&gt;), …)</c> — the exact
/// wiring <c>MessagingRegistration</c> applies on both transports — actually runs on a real MassTransit
/// pipeline and pins the payload tenant into <see cref="AmbientTenantScope"/> for the consume.
///
/// This closes the false-green gap the unit + read-back tests leave open: those exercise the filter
/// against a mock pipe and the <c>ambient → SESSION_CONTEXT</c> chain via a hand-rolled push, but never
/// the <c>UseConsumeFilter</c> registration itself. Without this test a dropped/misconfigured filter
/// registration would consume under the cross-tenant background context and stay invisible under the
/// current db_owner login — precisely the least-privilege scenario Slice 1D exists to fix.
///
/// Uses the in-memory test harness (no transport/SQL) — the tenant pinning is observable purely from
/// the consume scope, so no database is required.
/// </summary>
public sealed class TenantScopeConsumeFilterBusHarnessTests
{
    // Test-only messages mirroring a tenant-scoped integration event and a plain one.
    public sealed record TenantScopedProbe(Guid? TenantId) : ITenantScopedIntegrationEvent;

    public sealed record PlainProbe(Guid Value);

    /// <summary>Singleton sink: the spy consumer records the ambient tenant seen inside the consume.</summary>
    public sealed class TenantObservation
    {
        private readonly TaskCompletionSource _consumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Guid? AmbientTenant { get; private set; }
        public Task Consumed => _consumed.Task;

        public void Record(Guid? ambientTenant)
        {
            AmbientTenant = ambientTenant;
            _consumed.TrySetResult();
        }
    }

    public sealed class TenantScopedProbeConsumer(AmbientTenantScope scope, TenantObservation observation)
        : IConsumer<TenantScopedProbe>
    {
        public Task Consume(ConsumeContext<TenantScopedProbe> context)
        {
            observation.Record(scope.Current?.TenantId);
            return Task.CompletedTask;
        }
    }

    public sealed class PlainProbeConsumer(AmbientTenantScope scope, TenantObservation observation)
        : IConsumer<PlainProbe>
    {
        public Task Consume(ConsumeContext<PlainProbe> context)
        {
            observation.Record(scope.Current?.TenantId);
            return Task.CompletedTask;
        }
    }

    private static async Task<(ServiceProvider Provider, ITestHarness Harness, TenantObservation Observation)>
        StartHarnessAsync()
    {
        var observation = new TenantObservation();
        var provider = new ServiceCollection()
            .AddSingleton<AmbientTenantScope>()
            .AddSingleton(observation)
            .AddMassTransitTestHarness(x =>
            {
                x.AddConsumer<TenantScopedProbeConsumer>();
                x.AddConsumer<PlainProbeConsumer>();
                x.UsingInMemory((context, cfg) =>
                {
                    // The production wiring under test (see MessagingRegistration).
                    cfg.UseConsumeFilter(typeof(TenantScopeConsumeFilter<>), context);
                    cfg.ConfigureEndpoints(context);
                });
            })
            .BuildServiceProvider(validateScopes: true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return (provider, harness, observation);
    }

    [Fact]
    public async Task MarkerEventWithTenant_FilterPinsPayloadTenant_DuringConsume()
    {
        var tenant = Guid.NewGuid();
        var (provider, harness, observation) = await StartHarnessAsync();
        await using var _ = provider;
        try
        {
            await harness.Bus.Publish(new TenantScopedProbe(tenant));
            await observation.Consumed.WaitAsync(TimeSpan.FromSeconds(10));

            observation.AmbientTenant.Should().Be(tenant,
                "the UseConsumeFilter registration must push the payload tenant into AmbientTenantScope for the consume");
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task MarkerEventWithNullTenant_FilterDoesNotPin()
    {
        var (provider, harness, observation) = await StartHarnessAsync();
        await using var _ = provider;
        try
        {
            await harness.Bus.Publish(new TenantScopedProbe(null));
            await observation.Consumed.WaitAsync(TimeSpan.FromSeconds(10));

            observation.AmbientTenant.Should().BeNull("a null payload tenant must not pin the scope");
        }
        finally
        {
            await harness.Stop();
        }
    }

    [Fact]
    public async Task NonMarkerEvent_FilterDoesNotPin()
    {
        var (provider, harness, observation) = await StartHarnessAsync();
        await using var _ = provider;
        try
        {
            await harness.Bus.Publish(new PlainProbe(Guid.NewGuid()));
            await observation.Consumed.WaitAsync(TimeSpan.FromSeconds(10));

            observation.AmbientTenant.Should().BeNull("a non-tenant-scoped message must not touch the scope");
        }
        finally
        {
            await harness.Stop();
        }
    }
}
