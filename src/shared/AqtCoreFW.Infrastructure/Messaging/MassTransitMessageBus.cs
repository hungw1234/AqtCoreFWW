using AqtCoreFW.SharedKernel.Application;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Infrastructure.Messaging;

/// <summary>
/// IMessageBus implementation backed by MassTransit.
/// Transport is configured externally (InMemory dev / RabbitMQ prod).
/// Scoped lifetime — injected into Application layer command/query handlers.
/// </summary>
public sealed class MassTransitMessageBus(
    IBus bus,
    ISendEndpointProvider sendEndpointProvider,
    ILogger<MassTransitMessageBus> logger) : IMessageBus
{
    public async Task PublishAsync<T>(T message, CancellationToken ct = default)
        where T : class
    {
        logger.LogDebug("Publishing {MessageType}", typeof(T).Name);
        await bus.Publish(message, ct);
    }

    public async Task SendAsync<T>(T command, string queueName, CancellationToken ct = default)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(queueName))
            throw new ArgumentException("queueName must not be empty.", nameof(queueName));

        var endpoint = await sendEndpointProvider.GetSendEndpoint(
            new Uri($"queue:{queueName}"));

        logger.LogDebug("Sending {MessageType} to queue {Queue}", typeof(T).Name, queueName);
        await endpoint.Send(command, ct);
    }
}
