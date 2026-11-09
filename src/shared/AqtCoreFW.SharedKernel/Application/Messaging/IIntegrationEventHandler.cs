namespace AqtCoreFW.SharedKernel.Application.Messaging;

/// <summary>
/// Marker interface for integration event handlers (cross-module consumers).
/// Infrastructure layer implements this alongside MassTransit IConsumer&lt;T&gt;.
/// SharedKernel has no transport dependency — KISS/YAGNI.
/// </summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : class
{
    Task HandleAsync(TEvent message, CancellationToken ct = default);
}
