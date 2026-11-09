using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.Workflow.Application.EventHandlers;
using AqtCoreFW.Workflow.Domain.Events;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Workflow.Application.Tests.EventHandlers;

/// <summary>
/// Unit tests for WorkflowInstancesMigratedEventHandler.
/// Verifies analytics cache is invalidated for both source and target definitions.
/// </summary>
public sealed class WorkflowInstancesMigratedEventHandlerTests
{
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly WorkflowInstancesMigratedEventHandler _handler;

    public WorkflowInstancesMigratedEventHandlerTests()
    {
        _handler = new WorkflowInstancesMigratedEventHandler(_cache);
    }

    [Fact]
    public async Task Handle_InvalidatesBothSourceAndTargetCachePrefixes()
    {
        var tenantId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        var evt = new WorkflowInstancesMigratedEvent(sourceId, targetId, tenantId, Guid.NewGuid());

        await _handler.Handle(evt, CancellationToken.None);

        await _cache.Received(1).RemoveByPrefixAsync(
            $"wf:analytics:{tenantId}:{sourceId}:");
        await _cache.Received(1).RemoveByPrefixAsync(
            $"wf:analytics:{tenantId}:{targetId}:");
    }

    [Fact]
    public async Task Handle_CallsRemoveTwice_EvenWhenSourceEqualsTarget()
    {
        // Edge case: source == target should still call twice (idempotent)
        var tenantId = Guid.NewGuid();
        var defId = Guid.NewGuid();

        var evt = new WorkflowInstancesMigratedEvent(defId, defId, tenantId, Guid.NewGuid());

        await _handler.Handle(evt, CancellationToken.None);

        await _cache.Received(2).RemoveByPrefixAsync(
            $"wf:analytics:{tenantId}:{defId}:");
    }
}
