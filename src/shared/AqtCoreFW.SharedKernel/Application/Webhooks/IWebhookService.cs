using FluentResults;

namespace AqtCoreFW.SharedKernel.Application.Webhooks;

/// <summary>
/// Outgoing webhook dispatcher interface.
/// Resolves active subscriptions for eventType+tenantId and dispatches via Hangfire.
/// Implementation: WebhookDispatcher (Infrastructure/Webhooks).
/// </summary>
public interface IWebhookService
{
    /// <summary>
    /// Fire-and-forget dispatch: resolves subscribers and enqueues Hangfire delivery jobs.
    /// tenantId scopes subscription lookup — only subscriptions for this tenant are triggered.
    /// </summary>
    Task DispatchAsync(string eventType, object payload, Guid tenantId, CancellationToken ct = default);

    /// <summary>Creates a new webhook subscription. Returns the subscription DTO with plaintext secret (shown once).</summary>
    Task<Result<WebhookSubscriptionDto>> SubscribeAsync(CreateWebhookSubscriptionRequest request, CancellationToken ct = default);

    /// <summary>Deactivates a subscription (soft-delete). Scoped to tenantId for security.</summary>
    Task<Result> UnsubscribeAsync(Guid subscriptionId, Guid tenantId, CancellationToken ct = default);
}

/// <summary>Request to create a new webhook subscription.</summary>
/// <param name="EndpointUrl">Target HTTPS URL. Must pass SSRF validation.</param>
/// <param name="EventTypes">List of event type strings to subscribe (e.g. "case.approved").</param>
/// <param name="TenantId">Owning tenant — restricts which events trigger this subscription.</param>
/// <param name="Secret">Optional caller-supplied secret. If null, service generates one.</param>
public sealed record CreateWebhookSubscriptionRequest(
    string EndpointUrl,
    IReadOnlyList<string> EventTypes,
    Guid TenantId,
    string? Secret = null);

/// <summary>Webhook subscription data returned to caller. Secret is shown only on creation.</summary>
public sealed record WebhookSubscriptionDto(
    Guid Id,
    string EndpointUrl,
    IReadOnlyList<string> EventTypes,
    bool IsActive,
    DateTimeOffset CreatedAt,
    string? PlaintextSecret = null);

/// <summary>Summary of a single delivery attempt — returned in delivery history list.</summary>
public sealed record WebhookDeliveryAttemptDto(
    Guid Id,
    Guid SubscriptionId,
    string EventType,
    int AttemptNumber,
    int? StatusCode,
    string? ErrorMessage,
    DateTimeOffset AttemptedAt,
    bool IsSuccess);
