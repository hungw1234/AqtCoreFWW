using System.Text;
using System.Text.Json;
using AqtCoreFW.SharedKernel.Abstractions;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Infrastructure.Webhooks;

/// <summary>
/// Hangfire job: delivers a single webhook event to one subscription endpoint.
/// Enqueued by WebhookDispatcher on the "webhook-critical" queue.
///
/// Retry strategy: Hangfire's built-in AutomaticRetry handles re-enqueue.
/// WebhookDeliveryJob records each attempt in WebhookDeliveryAttempt table.
///
/// SSRF guard: WebhookUrlValidator.ValidateAsync() is called at delivery time
/// (re-validates DNS — guards against DNS rebinding after subscription creation).
///
/// Payload max: 64 KB (NF3). Requests exceeding this are rejected before HTTP call.
/// Timeout: 10s per attempt (NF1).
/// </summary>
public sealed class WebhookDeliveryJob(
    WebhookDbContext db,
    IHttpClientFactory httpClientFactory,
    WebhookUrlValidator urlValidator,
    ILogger<WebhookDeliveryJob> logger,
    IGuidGenerator guidGen)
{
    private const int MaxPayloadBytes = 64 * 1024; // 64 KB NF3
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10); // NF1

    /// <summary>
    /// Delivers the webhook payload to the subscription endpoint.
    /// Called by Hangfire — do NOT call directly.
    /// attemptNumber is 1-based; passed from WebhookDispatcher on initial enqueue,
    /// then Hangfire increments via retries tracked in DeliveryAttempt table.
    /// </summary>
    // H-01: Limit retries to 3 (not Hangfire default 10) — each attempt is recorded in DeliveryAttempt table.
    [AutomaticRetry(Attempts = 3)]
    public async Task ExecuteAsync(
        Guid subscriptionId,
        string eventType,
        string serializedPayload,
        int attemptNumber,
        CancellationToken ct = default)
    {
        var subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.Id == subscriptionId && s.IsActive, ct);

        if (subscription is null)
        {
            logger.LogWarning(
                "WebhookDelivery: subscription {SubId} not found or inactive — skipping.",
                subscriptionId);
            return;
        }

        // SSRF re-validation at delivery time (DNS rebinding guard)
        var urlCheck = await urlValidator.ValidateAsync(subscription.EndpointUrl, ct);
        if (urlCheck.IsFailed)
        {
            var reason = string.Join("; ", urlCheck.Errors.Select(e => e.Message));
            logger.LogWarning(
                "WebhookDelivery: SSRF check failed for sub {SubId} url {Url}: {Reason}",
                subscriptionId, subscription.EndpointUrl, reason);

            await RecordAttemptAsync(db, WebhookDeliveryAttempt.Failure(
                guidGen, subscriptionId, eventType, serializedPayload, attemptNumber,
                null, $"SSRF validation failed: {reason}"), ct);
            return; // Do not throw — prevents Hangfire retry for a security-rejected URL
        }

        // Enforce 64 KB payload limit
        if (Encoding.UTF8.GetByteCount(serializedPayload) > MaxPayloadBytes)
        {
            logger.LogWarning(
                "WebhookDelivery: payload exceeds 64 KB for sub {SubId} — aborting.",
                subscriptionId);
            await RecordAttemptAsync(db, WebhookDeliveryAttempt.Failure(
                guidGen, subscriptionId, eventType, serializedPayload, attemptNumber,
                null, "Payload exceeds 64 KB limit."), ct);
            return;
        }

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var request = new HttpRequestMessage(HttpMethod.Post, subscription.EndpointUrl)
        {
            Content = new StringContent(serializedPayload, Encoding.UTF8, "application/json")
        };

        request.Headers.TryAddWithoutValidation("X-Webhook-Event", eventType);
        request.Headers.TryAddWithoutValidation("X-Webhook-Delivery", guidGen.NewIdMonotonic().ToString());
        WebhookSigningService.ApplySignatureHeaders(
            request, subscription.SecretHash, serializedPayload, timestamp);

        var client = httpClientFactory.CreateClient("webhook");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RequestTimeout);

        try
        {
            using var response = await client.SendAsync(request, cts.Token);
            var statusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "WebhookDelivery: sub {SubId} event {Event} attempt {Attempt} — HTTP {Status} success.",
                    subscriptionId, eventType, attemptNumber, statusCode);

                await RecordAttemptAsync(db, WebhookDeliveryAttempt.Success(
                    guidGen, subscriptionId, eventType, serializedPayload, attemptNumber, statusCode), ct);
            }
            else
            {
                var msg = $"HTTP {statusCode} non-success response.";
                logger.LogWarning(
                    "WebhookDelivery: sub {SubId} event {Event} attempt {Attempt} — {Msg}",
                    subscriptionId, eventType, attemptNumber, msg);

                await RecordAttemptAsync(db, WebhookDeliveryAttempt.Failure(
                    guidGen, subscriptionId, eventType, serializedPayload, attemptNumber, statusCode, msg), ct);

                // Throw to let Hangfire retry (3 attempts total via AutomaticRetry)
                throw new HttpRequestException(msg);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            const string msg = "Delivery timed out after 10 seconds.";
            logger.LogWarning(
                "WebhookDelivery: sub {SubId} event {Event} attempt {Attempt} — timeout.",
                subscriptionId, eventType, attemptNumber);

            await RecordAttemptAsync(db, WebhookDeliveryAttempt.Failure(
                guidGen, subscriptionId, eventType, serializedPayload, attemptNumber, null, msg), ct);

            throw new TimeoutException(msg); // Rethrow so Hangfire retries
        }
    }

    private static async Task RecordAttemptAsync(
        WebhookDbContext db,
        WebhookDeliveryAttempt attempt,
        CancellationToken ct)
    {
        db.DeliveryAttempts.Add(attempt);
        await db.SaveChangesAsync(ct);
    }
}
