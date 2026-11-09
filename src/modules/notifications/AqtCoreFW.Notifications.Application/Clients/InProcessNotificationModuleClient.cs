using MediatR;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.Notifications.Application.Commands.SendNotification;

namespace AqtCoreFW.Notifications.Application.Clients;

/// <summary>
/// Monolith in-process adapter for INotificationModuleClient.
/// Zero overhead — direct MediatR dispatch, no HTTP/gRPC.
/// Swap to GrpcNotificationModuleClient when module is extracted to microservice (1 DI line change).
/// </summary>
public sealed class InProcessNotificationModuleClient(ISender mediator) : INotificationModuleClient
{
    public async Task SendAsync(SendNotificationRequest request, CancellationToken cancellationToken = default)
    {
        var command = new SendNotificationCommand(
            request.TenantId,
            request.RecipientUserId,
            request.Subject,
            request.Body,
            request.Channel,
            CorrelationId: request.CorrelationId);

        // Fire-and-forget style: caller does not need the notification ID
        await mediator.Send(command, cancellationToken);
    }
}
