using AqtCoreFW.Audit.Application.Commands.LogAuditEvent;
using AqtCoreFW.SharedKernel.Domain.Events;
using MediatR;

namespace AqtCoreFW.Audit.Application.EventHandlers;

/// <summary>
/// Listens for IInternalDomainEvent notifications and auto-logs them as audit entries.
/// Modules raise domain events; this handler captures them without coupling to Audit module directly.
/// </summary>
public sealed class DomainEventAuditHandler(ISender mediator)
    : INotificationHandler<AuditEventPublished>
{
    public async Task Handle(AuditEventPublished notification, CancellationToken cancellationToken)
    {
        var command = new LogAuditEventCommand(
            notification.TenantId,
            notification.UserId,
            notification.UserName,
            notification.Action,
            notification.ModuleName,
            notification.ResourceType,
            notification.ResourceId,
            notification.DataSnapshot,
            notification.IpAddress,
            notification.CorrelationId);

        await mediator.Send(command, cancellationToken);
    }
}

/// <summary>
/// MediatR notification raised by any module to trigger an audit log entry.
/// Use IPublisher.Publish() from command handlers or domain event handlers.
/// </summary>
public sealed record AuditEventPublished(
    Guid? TenantId,
    Guid? UserId,
    string UserName,
    string Action,
    string ModuleName,
    string ResourceType,
    string? ResourceId = null,
    string? DataSnapshot = null,
    string? IpAddress = null,
    string? CorrelationId = null) : INotification;
