using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.Workflow.Domain.Events;
using MediatR;

namespace AqtCoreFW.Workflow.Application.EventHandlers;

/// <summary>
/// Invalidates analytics cache for both source and target definitions
/// after a version migration completes.
/// Cache key pattern: "wf:analytics:{tenantId}:{definitionId}:"
/// Uses ICacheService.RemoveByPrefixAsync for pattern removal.
/// </summary>
public sealed class WorkflowInstancesMigratedEventHandler(ICacheService cache)
    : INotificationHandler<WorkflowInstancesMigratedEvent>
{
    public async Task Handle(
        WorkflowInstancesMigratedEvent notification,
        CancellationToken cancellationToken)
    {
        // SourceDefinitionPublicId/TargetDefinitionPublicId = WorkflowDefinition.PublicId (T2 Guid AK)
        var sourcePrefix = $"wf:analytics:{notification.TenantId}:{notification.SourceDefinitionPublicId}:";
        var targetPrefix = $"wf:analytics:{notification.TenantId}:{notification.TargetDefinitionPublicId}:";

        await cache.RemoveByPrefixAsync(sourcePrefix);
        await cache.RemoveByPrefixAsync(targetPrefix);
    }
}
