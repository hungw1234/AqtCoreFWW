using AqtCoreFW.Identity.Domain.Entities;

namespace AqtCoreFW.Identity.Application.Services;

/// <summary>
/// Resolves the effective MFA policy for a user based on their roles.
/// Resolution rule: mandatory policy wins over optional; among mandatories, use shortest GraceDays.
/// Returns null when user has no roles or no matching policy rows exist.
/// Used by login flow (mfa_required gate) and enrolment gate.
/// </summary>
public interface IMfaPolicyResolver
{
    /// <summary>
    /// Returns the effective <see cref="MfaPolicy"/> for <paramref name="userId"/>,
    /// or null if no policy applies.
    /// </summary>
    Task<MfaPolicy?> ResolveAsync(Guid userId, CancellationToken ct = default);
}
