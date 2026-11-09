namespace AqtCoreFW.SharedKernel.Contracts.Events;

/// <summary>
/// Integration event published by Identity when a user account is automatically disabled after
/// exceeding the escalation lockout threshold (QĐ 742 1.4.d). Consumed by Notifications via MassTransit
/// <c>IConsumer&lt;T&gt;</c> to email the affected user — no cross-module project reference (type lives here).
///
/// Delivery semantics: AT-MOST-ONCE by deliberate decision — direct MassTransit publish, NOT routed
/// through the durable outbox. Recorded rationale (re-read before "fixing" this onto the outbox):
///   * The auto-disable is persisted independently by the lockout write, so a lost event drops only the
///     courtesy notification, never the security action itself.
///   * The email is best-effort at-most-once regardless — the consumer swallows send failures with no retry.
///   * This is a notification trigger, NOT an audit record: the auto-disable is separately audited on the
///     at-least-once audit path, so losing this event creates no audit gap under service extraction.
///
/// PII: this payload carries <see cref="UserName"/> + <see cref="Email"/> (contact fields) so the consumer
/// can send the alert without a re-hydration round-trip. That is acceptable ONLY because the message is
/// transient — a direct publish that is NEVER persisted to the outbox/dead-letters at rest. It is
/// intentionally NOT an <c>IExternalDomainEvent</c> (the interceptor/outbox capture path and the IDs-only
/// PII guard apply only to those). Routing it through the outbox would first require stripping to IDs-only
/// plus consumer-side contact re-hydration.
/// </summary>
public sealed record AccountAutoDisabledIntegrationEvent(
    Guid UserId,
    Guid? TenantId,
    string? UserName,
    string? Email,
    DateTime AutoDisabledAtUtc,
    int ConsecutiveLockoutCount);
