namespace AqtCoreFW.SharedKernel.Contracts.Clients;

/// <summary>
/// Cross-module interface for sending notifications.
/// Monolith: InProcessNotificationModuleClient (direct service call, zero overhead).
/// Microservice: GrpcNotificationModuleClient (when module extracted — Phase 2+).
/// Interface is transport-agnostic — NO .proto files in monolith.
/// </summary>
public interface INotificationModuleClient
{
    Task SendAsync(SendNotificationRequest request, CancellationToken cancellationToken = default);
}

public record SendNotificationRequest(
    Guid RecipientUserId,
    Guid TenantId,
    string Subject,
    string Body,
    string Channel,       // "email" | "sms" | "inapp"
    string? CorrelationId = null);
