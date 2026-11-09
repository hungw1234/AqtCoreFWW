using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.Identity.Application.Services;

/// <summary>
/// Resolves the effective MFA policy for a user from their role memberships (QĐ742 1.6.a).
///
/// Resolution rules (applied in order):
///   1. Load user's role names via UserManager.GetRolesAsync.
///   2. Query MfaPolicy rows matching any of those role names.
///   3. If no matching policies → null (no enforcement).
///   4. Mandatory policies beat optional policies.
///   5. Among mandatories, the one with the shortest GraceDays is most restrictive — return it.
///   6. If only optional policies → return the first (arbitrary; resolver callers treat any non-null
///      optional as "enrolment encouraged but not blocking").
/// </summary>
public sealed class MfaPolicyResolver : IMfaPolicyResolver
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IMfaPolicyRepository _policyRepository;

    public MfaPolicyResolver(
        UserManager<ApplicationUser> userManager,
        IMfaPolicyRepository policyRepository)
    {
        _userManager = userManager;
        _policyRepository = policyRepository;
    }

    /// <inheritdoc/>
    public async Task<MfaPolicy?> ResolveAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return null;

        var roleNames = await _userManager.GetRolesAsync(user);
        if (roleNames.Count == 0)
            return null;

        var policies = await _policyRepository.GetByRoleNamesAsync(roleNames, ct);
        if (policies.Count == 0)
            return null;

        // Mandatory wins over optional; among mandatories, shortest GraceDays is most restrictive.
        var mandatory = policies.Where(p => p.IsMandatory).ToList();
        if (mandatory.Count > 0)
            return mandatory.MinBy(p => p.GraceDays);

        // All optional — return first (non-blocking, callers may prompt enrolment).
        return policies[0];
    }
}
