using AqtCoreFW.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.AuthServer.Services;

/// <summary>
/// OWASP A07 / QĐ742 timing-side-channel mitigation.
/// Equalizes work-factor cost between user-found and user-not-found login paths
/// so attackers cannot enumerate accounts via response-time analysis.
/// Companion to anti-enumeration message unification (BE-LOGIN-USER-ENUMERATION).
/// </summary>
public interface IPasswordVerifyTimingDecoy
{
    /// <summary>
    /// Runs the password hasher's verify routine against a one-shot decoy hash
    /// using the supplied password. Result is discarded; only the work-factor
    /// CPU cost matters. Decoy hash is computed once per process and cached.
    /// </summary>
    void Consume(string password);
}

public sealed class PasswordVerifyTimingDecoy : IPasswordVerifyTimingDecoy
{
    // Process-wide cached decoy hash. Standalone PasswordHasher with default
    // PBKDF2 options (no project-level customization — verified via grep).
    // Computed lazily on first Consume invocation, then reused for the
    // process lifetime so we pay the ~100-300ms hash cost exactly once.
    private static readonly Lazy<string> DecoyHash = new(
        () => new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), "timing-decoy"),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly IPasswordHasher<ApplicationUser> _hasher;

    public PasswordVerifyTimingDecoy(IPasswordHasher<ApplicationUser> hasher)
    {
        _hasher = hasher;
    }

    public void Consume(string password)
    {
        _ = _hasher.VerifyHashedPassword(new ApplicationUser(), DecoyHash.Value, password ?? string.Empty);
    }
}
