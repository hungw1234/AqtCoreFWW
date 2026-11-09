using AqtCoreFW.Identity.Application.Services;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Contracts.Events;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.Authentication;

/// <summary>
/// Default implementation of <see cref="ILockoutEscalationService"/>.
/// Fallback policy (when no CredentialPolicy is found): EscalationLockoutCount=3, EscalationWindowHours=24.
/// </summary>
public sealed class LockoutEscalationService(
    UserManager<ApplicationUser> userManager,
    ICredentialPolicyRepository credentialPolicyRepository,
    IMessageBus messageBus,
    ILogger<LockoutEscalationService> logger) : ILockoutEscalationService
{
    // Hardcoded fallback values matching CredentialPolicy property defaults.
    private const int FallbackEscalationLockoutCount = 3;
    private const int FallbackEscalationWindowHours = 24;

    public async Task<bool> OnLockoutAsync(ApplicationUser user, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // Lookup tenant-scoped policy; fall back to hard-coded defaults when none exists.
        // FU6: also swallow repo exceptions — a transient DB hiccup or schema drift must
        // not bypass escalation accounting (the hardcoded defaults still trigger auto-disable).
        CredentialPolicy? policy;
        try
        {
            policy = await credentialPolicyRepository
                .GetDefaultForTenantAsync(user.TenantId ?? Guid.Empty, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "CredentialPolicy lookup failed for tenant {TenantId}; using hardcoded escalation fallback (count={Count}, window={Hours}h).",
                user.TenantId, FallbackEscalationLockoutCount, FallbackEscalationWindowHours);
            policy = null;
        }

        var escalationCount = policy?.EscalationLockoutCount ?? FallbackEscalationLockoutCount;
        var windowHours = policy?.EscalationWindowHours ?? FallbackEscalationWindowHours;

        // Reset or increment counter based on whether the escalation window has expired.
        if (user.LastLockoutAt is null ||
            (now - user.LastLockoutAt.Value).TotalHours > windowHours)
        {
            // Window expired — restart the consecutive counter.
            user.ConsecutiveLockoutCount = 1;
            logger.LogInformation(
                "Lockout escalation window reset for user {UserId}. ConsecutiveLockoutCount=1.",
                user.Id);
        }
        else
        {
            user.ConsecutiveLockoutCount += 1;
            logger.LogInformation(
                "Consecutive lockout #{Count} for user {UserId} (threshold={Threshold}).",
                user.ConsecutiveLockoutCount, user.Id, escalationCount);
        }

        user.LastLockoutAt = now;

        // Check if escalation threshold is reached and account is not already auto-disabled.
        if (user.ConsecutiveLockoutCount >= escalationCount && user.AutoDisabledAt is null)
        {
            user.AutoDisabledAt = now;

            // Permanently lock the account (DateTimeOffset.MaxValue = never expires).
            await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

            logger.LogWarning(
                "User {UserId} auto-disabled after {Count} consecutive lockouts. AutoDisabledAt={At:u}.",
                user.Id, user.ConsecutiveLockoutCount, now);

            // Direct publish (at-most-once), NOT outbox — deliberate. The auto-disable above is already
            // persisted, so a lost event drops only the notification; see AccountAutoDisabledIntegrationEvent
            // for the full recorded rationale (notification-only, best-effort email, carries transient PII).
            await messageBus.PublishAsync(new AccountAutoDisabledIntegrationEvent(
                UserId: user.Id,
                TenantId: user.TenantId,
                UserName: user.UserName,
                Email: user.Email,
                AutoDisabledAtUtc: now,
                ConsecutiveLockoutCount: user.ConsecutiveLockoutCount), ct);

            // SetLockoutEndDateAsync already calls UpdateAsync internally; no double-save needed.
            return true;
        }

        // Save counter + LastLockoutAt changes (escalation not triggered).
        await userManager.UpdateAsync(user);
        return false;
    }

    public async Task ResetLockoutCounterAsync(ApplicationUser user, CancellationToken ct = default)
    {
        // Idempotent: skip DB write when counter is already zero.
        if (user.ConsecutiveLockoutCount <= 0)
            return;

        user.ConsecutiveLockoutCount = 0;
        await userManager.UpdateAsync(user);

        logger.LogInformation(
            "Lockout counter reset to 0 for user {UserId} after successful login.", user.Id);
    }
}
