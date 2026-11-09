namespace AqtCoreFW.SharedKernel.Application;

/// <summary>
/// Abstract message bus — publish/send cross-module integration events and commands.
/// Implementation: MassTransit with InMemory (dev) or RabbitMQ (staging/prod).
/// Injected into Application layer only — never in Domain layer.
/// </summary>
public interface IMessageBus
{
    /// <summary>
    /// Publish an integration event to all subscribers (fan-out).
    /// Use for IExternalDomainEvent — IDs only, no PII.
    /// </summary>
    Task PublishAsync<T>(T message, CancellationToken ct = default) where T : class;

    /// <summary>
    /// Send a command to a specific queue (point-to-point).
    /// </summary>
    Task SendAsync<T>(T command, string queueName, CancellationToken ct = default) where T : class;
}
