using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.Identity.Application.Services;

/// <summary>
/// Tracks consecutive lockouts per user and escalates to permanent auto-disable
/// when the threshold is reached (QĐ 742 1.4.d).
/// </summary>
public interface ILockoutEscalationService
{
    /// <summary>
    /// Call IMMEDIATELY AFTER SignInResult.IsLockedOut == true.
    /// Increments ConsecutiveLockoutCount, checks escalation window, sets AutoDisabledAt
    /// and publishes AccountAutoDisabledIntegrationEvent if the threshold is hit.
    /// Returns true if the account was just auto-disabled.
    /// </summary>
    Task<bool> OnLockoutAsync(ApplicationUser user, CancellationToken ct = default);

    /// <summary>
    /// Call after a SUCCESSFUL login. Resets ConsecutiveLockoutCount to 0 (idempotent).
    /// </summary>
    Task ResetLockoutCounterAsync(ApplicationUser user, CancellationToken ct = default);
}
