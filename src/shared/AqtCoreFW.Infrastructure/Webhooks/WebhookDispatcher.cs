using System.Text.Json;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Webhooks;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Infrastructure.Webhooks;

/// <summary>
/// IWebhookService implementation.
/// DispatchAsync: finds active subscriptions for (eventType, tenantId),
///   enqueues one Hangfire job per subscription on "webhook-critical" queue.
/// SubscribeAsync: validates URL (SSRF), enforces max-10-subscriptions-per-event-type (NF2),
///   stores hash of secret, returns plaintext secret once.
/// UnsubscribeAsync: soft-deactivates subscription scoped to tenantId.
/// </summary>
public sealed class WebhookDispatcher(
    WebhookDbContext db,
    WebhookUrlValidator urlValidator,
    IBackgroundJobClient jobClient,
    ILogger<WebhookDispatcher> logger,
    IGuidGenerator guidGen) : IWebhookService
{
    private const int MaxSubscriptionsPerEventType = 10; // NF2

    public async Task DispatchAsync(
        string eventType,
        object payload,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var subscriptions = await db.Subscriptions
            .Where(s => s.TenantId == tenantId && s.IsActive)
            .ToListAsync(ct);

        // Filter in memory — EventTypesJson is a JSON array; LIKE check sufficient for ≤10 subs
        var targets = subscriptions
            .Where(s => s.GetEventTypes().Contains(eventType, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (targets.Count == 0)
        {
            logger.LogDebug(
                "WebhookDispatch: no active subscriptions for event {Event} tenant {Tenant}.",
                eventType, tenantId);
            return;
        }

        // tenantId intentionally excluded from outbound payload (F06 security — internal UUID
        // must not leak to external webhook consumers). TenantId used for subscription lookup only.
        var serializedPayload = JsonSerializer.Serialize(new
        {
            eventType,
            data = payload,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        });

        foreach (var sub in targets)
        {
            jobClient.Enqueue<WebhookDeliveryJob>(
                queue: "webhook-critical",
                methodCall: job => job.ExecuteAsync(
                    sub.Id, eventType, serializedPayload, 1, CancellationToken.None));

            logger.LogInformation(
                "WebhookDispatch: enqueued delivery for sub {SubId} event {Event} tenant {Tenant}.",
                sub.Id, eventType, tenantId);
        }
    }

    public async Task<Result<WebhookSubscriptionDto>> SubscribeAsync(
        CreateWebhookSubscriptionRequest request,
        CancellationToken ct = default)
    {
        // SSRF validation
        var urlCheck = await urlValidator.ValidateAsync(request.EndpointUrl, ct);
        if (urlCheck.IsFailed)
            return Result.Fail(urlCheck.Errors);

        // NF2: max 10 subscriptions per event type per tenant
        // H3-BE-Arch: hoist query outside loop to avoid N+1 DB calls
        var allSubscriptions = await db.Subscriptions
            .Where(s => s.TenantId == request.TenantId && s.IsActive)
            .ToListAsync(ct);

        foreach (var eventType in request.EventTypes)
        {
            var countForEvent = allSubscriptions
                .Count(s => s.GetEventTypes().Contains(eventType, StringComparer.OrdinalIgnoreCase));

            if (countForEvent >= MaxSubscriptionsPerEventType)
            {
                return Result.Fail(new ValidationError(
                    $"Maximum {MaxSubscriptionsPerEventType} active subscriptions allowed per event type '{eventType}'.",
                    nameof(request.EventTypes)));
            }
        }

        // Generate or use provided secret — hash for storage, return plaintext once
        var plaintextSecret = string.IsNullOrEmpty(request.Secret)
            ? WebhookSigningService.GenerateSecret()
            : request.Secret;
        var secretHash = WebhookSigningService.HashSecret(plaintextSecret);

        var subscription = WebhookSubscription.Create(
            guidGen,
            request.TenantId,
            request.EndpointUrl,
            request.EventTypes,
            secretHash);

        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "WebhookSubscribe: created sub {SubId} for tenant {Tenant} events [{Events}].",
            subscription.Id, request.TenantId, string.Join(", ", request.EventTypes));

        return Result.Ok(new WebhookSubscriptionDto(
            subscription.Id,
            subscription.EndpointUrl,
            subscription.GetEventTypes(),
            subscription.IsActive,
            subscription.CreatedAt,
            PlaintextSecret: plaintextSecret)); // Shown once
    }

    public async Task<Result> UnsubscribeAsync(Guid subscriptionId, Guid tenantId, CancellationToken ct = default)
    {
        var subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.Id == subscriptionId && s.TenantId == tenantId, ct);

        if (subscription is null)
            return Result.Fail(new NotFoundError(
                $"Webhook subscription {subscriptionId} not found for tenant {tenantId}."));

        subscription.Deactivate();
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "WebhookUnsubscribe: deactivated sub {SubId} for tenant {Tenant}.",
            subscriptionId, tenantId);

        return Result.Ok();
    }
}
