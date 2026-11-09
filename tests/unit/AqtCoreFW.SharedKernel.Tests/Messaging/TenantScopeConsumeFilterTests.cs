using AqtCoreFW.Infrastructure.Messaging;
using AqtCoreFW.Infrastructure.Security;
using AqtCoreFW.SharedKernel.Domain.Events;
using FluentAssertions;
using MassTransit;
using NSubstitute;

namespace AqtCoreFW.SharedKernel.Tests.Messaging;

/// <summary>
/// Unit tests for <see cref="TenantScopeConsumeFilter{T}"/> (Slice 1D). Proves the filter pushes
/// the payload tenant into <see cref="AmbientTenantScope"/> only for tenant-scoped messages, only
/// while the downstream consumer runs, and restores it afterwards.
/// </summary>
public sealed class TenantScopeConsumeFilterTests
{
    // Public so NSubstitute/Castle DynamicProxy (proxying MassTransit's strong-named
    // ConsumeContext<T>) can see the message type. Test-local — not scanned by the event guards.
    public sealed record TenantScopedMessage(Guid? TenantId) : ITenantScopedIntegrationEvent;

    public sealed record PlainMessage(string Value);

    [Fact]
    public async Task Send_MarkerMessageWithTenant_ScopesTenantForConsumeThenRestores()
    {
        var scope = new AmbientTenantScope();
        var tenantId = Guid.NewGuid();
        var filter = new TenantScopeConsumeFilter<TenantScopedMessage>(scope);

        var context = Substitute.For<ConsumeContext<TenantScopedMessage>>();
        context.Message.Returns(new TenantScopedMessage(tenantId));

        Guid? observedDuringConsume = null;
        var next = Substitute.For<IPipe<ConsumeContext<TenantScopedMessage>>>();
        next.Send(context).Returns(_ =>
        {
            observedDuringConsume = scope.Current?.TenantId;
            return Task.CompletedTask;
        });

        await filter.Send(context, next);

        observedDuringConsume.Should().Be(tenantId, "the consumer must run pinned to the payload tenant");
        scope.Current.Should().BeNull("the scope must be restored once the consume completes");
        await next.Received(1).Send(context);
    }

    [Fact]
    public async Task Send_MarkerMessageWithNullTenant_PassesThroughWithoutScoping()
    {
        var scope = new AmbientTenantScope();
        var filter = new TenantScopeConsumeFilter<TenantScopedMessage>(scope);

        var context = Substitute.For<ConsumeContext<TenantScopedMessage>>();
        context.Message.Returns(new TenantScopedMessage(null));

        Guid? observedDuringConsume = Guid.NewGuid();
        var next = Substitute.For<IPipe<ConsumeContext<TenantScopedMessage>>>();
        next.Send(context).Returns(_ =>
        {
            observedDuringConsume = scope.Current?.TenantId;
            return Task.CompletedTask;
        });

        await filter.Send(context, next);

        observedDuringConsume.Should().BeNull("a null payload tenant means no scoping");
        await next.Received(1).Send(context);
    }

    [Fact]
    public async Task Send_NonMarkerMessage_PassesThroughUnchanged()
    {
        var scope = new AmbientTenantScope();
        var filter = new TenantScopeConsumeFilter<PlainMessage>(scope);

        var context = Substitute.For<ConsumeContext<PlainMessage>>();
        context.Message.Returns(new PlainMessage("payload"));

        Guid? observedDuringConsume = Guid.NewGuid();
        var next = Substitute.For<IPipe<ConsumeContext<PlainMessage>>>();
        next.Send(context).Returns(_ =>
        {
            observedDuringConsume = scope.Current?.TenantId;
            return Task.CompletedTask;
        });

        await filter.Send(context, next);

        observedDuringConsume.Should().BeNull("a non-tenant-scoped message must not touch the scope");
        await next.Received(1).Send(context);
    }
}
