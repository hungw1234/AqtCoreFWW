using AqtCoreFW.SharedKernel.Abstractions;

namespace AqtCoreFW.Infrastructure.Webhooks;

/// <summary>
/// Records each HTTP delivery attempt for a webhook subscription (schema: webhooks).
/// One row per attempt — multiple rows per dispatch event when retries occur.
/// </summary>
public sealed class WebhookDeliveryAttempt
{
    public Guid Id { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public string EventType { get; private set; } = string.Empty;

    /// <summary>JSON-serialized payload body sent to the endpoint.</summary>
    public string Payload { get; private set; } = string.Empty;

    /// <summary>1-based attempt counter (1 = first try, 2 = first retry, 3 = second retry).</summary>
    public int AttemptNumber { get; private set; }

    /// <summary>HTTP response status code; null if connection failed (timeout/DNS/refused).</summary>
    public int? StatusCode { get; private set; }

    public string? ErrorMessage { get; private set; }
    public bool IsSuccess { get; private set; }
    public DateTimeOffset AttemptedAt { get; private set; } = DateTimeOffset.UtcNow;

    // EF Core parameterless ctor
    private WebhookDeliveryAttempt() { }

    public static WebhookDeliveryAttempt Success(
        IGuidGenerator guidGen,
        Guid subscriptionId,
        string eventType,
        string payload,
        int attemptNumber,
        int statusCode) =>
        new()
        {
            Id = guidGen.NewIdMonotonic(),
            SubscriptionId = subscriptionId,
            EventType = eventType,
            Payload = payload,
            AttemptNumber = attemptNumber,
            StatusCode = statusCode,
            IsSuccess = true
        };

    public static WebhookDeliveryAttempt Failure(
        IGuidGenerator guidGen,
        Guid subscriptionId,
        string eventType,
        string payload,
        int attemptNumber,
        int? statusCode,
        string errorMessage) =>
        new()
        {
            Id = guidGen.NewIdMonotonic(),
            SubscriptionId = subscriptionId,
            EventType = eventType,
            Payload = payload,
            AttemptNumber = attemptNumber,
            StatusCode = statusCode,
            ErrorMessage = errorMessage,
            IsSuccess = false
        };
}
