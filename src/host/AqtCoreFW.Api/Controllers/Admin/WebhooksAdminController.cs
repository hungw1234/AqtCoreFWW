using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Infrastructure.Webhooks;
using AqtCoreFW.SharedKernel.Api;
using AqtCoreFW.SharedKernel.Application.Webhooks;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using AqtCoreFW.SharedKernel.Presentation;
using FluentResults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Api.Controllers.Admin;

/// <summary>
/// Admin API for webhook subscription management and delivery history.
/// Requires Admin policy (Admin or SystemAdmin role).
/// Tenant-scoped: all operations filter by ITenantContext.TenantId.
/// </summary>
[Route("api/v1/admin/webhooks")]
[Authorize(Policy = PermCodes.System.Config.Manage)]
[RequiresMfa]
[EnableRateLimiting("write-ops")]
public sealed class WebhooksAdminController(
    IWebhookService webhookService,
    WebhookDbContext db,
    ITenantContext tenantContext) : ApiControllerBase
{
    // â”€â”€ Subscription CRUD â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Create a new webhook subscription.
    /// Returns the subscription with PlaintextSecret â€” shown ONCE, not stored.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<WebhookSubscriptionDto>), 201)]
    [ProducesResponseType(typeof(ApiResponse<object>), 400)]
    [ProducesResponseType(typeof(ApiResponse<object>), 422)]
    public async Task<IActionResult> Create(
        [FromBody] CreateWebhookRequest body,
        CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null)
            return StatusCode(400, ApiResponse<object>.Fail(
                [new ValidationError("TenantId is required.", "tenantId")]));

        var request = new CreateWebhookSubscriptionRequest(
            body.EndpointUrl,
            body.EventTypes,
            tenantId.Value,
            body.Secret);

        var result = await webhookService.SubscribeAsync(request, ct);
        if (result.IsFailed)
            return ToApiResponse(Result.Fail(result.Errors));

        return StatusCode(201, ApiResponse<WebhookSubscriptionDto>.Ok(result.Value));
    }

    /// <summary>List all active webhook subscriptions for the current tenant.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<WebhookSubscriptionSummaryDto>>), 200)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var resolved = ResolveTenantId();
        if (resolved is null)
            return Ok(ApiResponse<List<WebhookSubscriptionSummaryDto>>.Ok([]));

        var subs = await db.Subscriptions
            .Where(s => s.TenantId == resolved.Value && s.IsActive)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

        var dtos = subs.Select(WebhookSubscriptionSummaryDto.From).ToList();
        return Ok(ApiResponse<List<WebhookSubscriptionSummaryDto>>.Ok(dtos));
    }

    /// <summary>Deactivate (soft-delete) a webhook subscription.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null)
            return StatusCode(400, ApiResponse<object>.Fail(
                [new ValidationError("TenantId is required.", "tenantId")]));

        var result = await webhookService.UnsubscribeAsync(id, tenantId.Value, ct);
        return ToApiResponse(result);
    }

    // â”€â”€ Delivery history â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Paginated delivery attempt history for a subscription.
    /// Scoped to tenant â€” returns 404 if subscription belongs to a different tenant.
    /// </summary>
    [HttpGet("{id:guid}/deliveries")]
    [ProducesResponseType(typeof(ApiResponse<List<WebhookDeliveryAttemptDto>>), 200)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> GetDeliveries(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null)
            return StatusCode(400, ApiResponse<object>.Fail(
                [new ValidationError("TenantId is required.", "tenantId")]));

        // Verify subscription belongs to this tenant
        var exists = await db.Subscriptions
            .AnyAsync(s => s.Id == id && s.TenantId == tenantId.Value, ct);

        if (!exists)
            return NotFound(ApiResponse<object>.Fail(
                [new NotFoundError($"Webhook subscription {id} not found.")]));

        pageSize = Math.Clamp(pageSize, 1, 100);
        var skip = (Math.Max(page, 1) - 1) * pageSize;

        var attempts = await db.DeliveryAttempts
            .Where(a => a.SubscriptionId == id)
            .OrderByDescending(a => a.AttemptedAt)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(ct);

        var dtos = attempts.Select(a => new WebhookDeliveryAttemptDto(
            a.Id, a.SubscriptionId, a.EventType, a.AttemptNumber,
            a.StatusCode, a.ErrorMessage, a.AttemptedAt, a.IsSuccess)).ToList();

        return Ok(ApiResponse<List<WebhookDeliveryAttemptDto>>.Ok(dtos));
    }

    // â”€â”€ Test ping â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>
    /// Sends a test ping event to verify the subscription endpoint is reachable.
    /// Enqueues a Hangfire job with eventType "webhook.test" â€” does not count as real dispatch.
    /// </summary>
    [HttpPost("{id:guid}/test")]
    [ProducesResponseType(202)]
    [ProducesResponseType(typeof(ApiResponse<object>), 404)]
    public async Task<IActionResult> SendTestPing(Guid id, CancellationToken ct)
    {
        var tenantId = ResolveTenantId();
        if (tenantId is null)
            return StatusCode(400, ApiResponse<object>.Fail(
                [new ValidationError("TenantId is required.", "tenantId")]));

        var exists = await db.Subscriptions
            .AnyAsync(s => s.Id == id && s.TenantId == tenantId.Value && s.IsActive, ct);

        if (!exists)
            return NotFound(ApiResponse<object>.Fail(
                [new NotFoundError($"Webhook subscription {id} not found or inactive.")]));

        // Dispatch a test event â€” reuses full delivery pipeline including SSRF + HMAC
        await webhookService.DispatchAsync(
            "webhook.test",
            new { message = "Test ping from AqtCoreFW webhook engine.", subscriptionId = id },
            tenantId.Value,
            ct);

        return Accepted();
    }

    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private new Guid? ResolveTenantId()
    {
        if (tenantContext.TenantId.HasValue)
            return tenantContext.TenantId;

        // Only SystemAdmin can override tenant via X-Tenant-Id header
        if (tenantContext.IsSystemAdmin
            && Request.Headers.TryGetValue("X-Tenant-Id", out var headerVal)
            && Guid.TryParse(headerVal.FirstOrDefault(), out var parsed))
            return parsed;

        return null;
    }
}

// â”€â”€ Request / Response DTOs â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

/// <summary>Request body for webhook subscription creation.</summary>
public sealed record CreateWebhookRequest(
    string EndpointUrl,
    IReadOnlyList<string> EventTypes,
    string? Secret = null);

/// <summary>Lightweight subscription summary â€” excludes SecretHash.</summary>
public sealed record WebhookSubscriptionSummaryDto(
    Guid Id,
    string EndpointUrl,
    IReadOnlyList<string> EventTypes,
    bool IsActive,
    DateTimeOffset CreatedAt)
{
    public static WebhookSubscriptionSummaryDto From(WebhookSubscription s) =>
        new(s.Id, s.EndpointUrl, s.GetEventTypes(), s.IsActive, s.CreatedAt);
}
