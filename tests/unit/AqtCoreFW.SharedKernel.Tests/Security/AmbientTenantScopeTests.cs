using AqtCoreFW.Infrastructure.Security;
using FluentAssertions;

namespace AqtCoreFW.SharedKernel.Tests.Security;

/// <summary>
/// Unit tests for the Slice 1D async-local tenant override and the scoped tenant context it
/// feeds. These cover the push/restore semantics the MassTransit consume filter relies on;
/// the end-to-end SESSION_CONTEXT wiring is proven in the integration suite.
/// </summary>
public sealed class AmbientTenantScopeTests
{
    [Fact]
    public void Current_IsNull_WhenNothingPushed()
    {
        new AmbientTenantScope().Current.Should().BeNull();
    }

    [Fact]
    public void Push_SetsCurrent_AndDisposeRestoresPrevious()
    {
        var scope = new AmbientTenantScope();
        var tenantId = Guid.NewGuid();

        using (scope.Push(tenantId))
        {
            scope.Current.Should().NotBeNull();
            scope.Current!.TenantId.Should().Be(tenantId);
        }

        scope.Current.Should().BeNull();
    }

    [Fact]
    public void Push_Nested_RestoresOuterOnInnerDispose()
    {
        var scope = new AmbientTenantScope();
        var outer = Guid.NewGuid();
        var inner = Guid.NewGuid();

        using (scope.Push(outer))
        {
            using (scope.Push(inner))
                scope.Current!.TenantId.Should().Be(inner);

            scope.Current!.TenantId.Should().Be(outer);
        }

        scope.Current.Should().BeNull();
    }

    [Fact]
    public void Push_DoubleDispose_IsIdempotent()
    {
        var scope = new AmbientTenantScope();
        var handle = scope.Push(Guid.NewGuid());

        handle.Dispose();
        var act = () => handle.Dispose();

        act.Should().NotThrow();
        scope.Current.Should().BeNull();
    }

    [Fact]
    public async Task Push_ValueFlows_ToAsyncContinuationsButNotSiblingFlows()
    {
        var scope = new AmbientTenantScope();
        var tenantId = Guid.NewGuid();

        Guid? observedInsideFlow = null;
        using (scope.Push(tenantId))
        {
            await Task.Yield();
            observedInsideFlow = scope.Current?.TenantId;
        }

        observedInsideFlow.Should().Be(tenantId);
        scope.Current.Should().BeNull();
    }

    [Fact]
    public void ScopedTenantContext_PinsTenant_AndIsNotSystemAdmin()
    {
        var tenantId = Guid.NewGuid();

        var ctx = new ScopedTenantContext(tenantId);

        ctx.TenantId.Should().Be(tenantId);
        ctx.IsSystemAdmin.Should().BeFalse();
    }
}
