using AqtCoreFW.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.Identity.Infrastructure.Services;

/// <summary>
/// Enforces QĐ742 password expiry policy (default 90 days).
/// Called after successful authentication to check if password is expired.
/// </summary>
public sealed class PasswordExpiryService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public PasswordExpiryService(UserManager<ApplicationUser> userManager)
        => _userManager = userManager;

    /// <summary>Sets PasswordExpiresAt = UtcNow + expiryDays. Call after password change.</summary>
    public async Task SetExpiryAsync(ApplicationUser user, int expiryDays = 90)
    {
        user.PasswordExpiresAt = DateTime.UtcNow.AddDays(expiryDays);
        await _userManager.UpdateAsync(user);
    }

    /// <summary>Returns true if the user's password has expired.</summary>
    public Task<bool> IsPasswordExpiredAsync(ApplicationUser user)
    {
        if (user.PasswordExpiresAt is null)
            return Task.FromResult(false);

        return Task.FromResult(DateTime.UtcNow > user.PasswordExpiresAt.Value);
    }

    /// <summary>Returns days until expiry, or null if no expiry is set.</summary>
    public Task<int?> GetDaysUntilExpiryAsync(ApplicationUser user)
    {
        if (user.PasswordExpiresAt is null)
            return Task.FromResult<int?>(null);

        var days = (int)Math.Ceiling((user.PasswordExpiresAt.Value - DateTime.UtcNow).TotalDays);
        return Task.FromResult<int?>(days);
    }
}
