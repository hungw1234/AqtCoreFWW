using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Application.Caching;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.Identity.Application.Commands.ChangePassword;

/// <summary>
/// Verifies current password, then changes to new password.
/// Invalidates token cache to force re-authentication with new credentials.
/// </summary>
public sealed class ChangePasswordCommandHandler(
    UserManager<ApplicationUser> userManager,
    ICacheService cache) : IRequestHandler<ChangePasswordCommand, Result>
{
    public async Task<Result> Handle(ChangePasswordCommand cmd, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(cmd.UserId.ToString());
        if (user is null)
            return Result.Fail(new NotFoundError($"User {cmd.UserId} not found"));

        var identityResult = await userManager.ChangePasswordAsync(user, cmd.CurrentPassword, cmd.NewPassword);
        if (!identityResult.Succeeded)
        {
            var errors = identityResult.Errors.Select(e => new ValidationError(e.Description));
            return Result.Fail(errors);
        }

        // Reset password expiry — QĐ742 1.3e: unlock on successful password change
        user.PasswordExpiresAt = DateTime.UtcNow.AddDays(90); // Default 90 days, configurable via SystemParams
        // QĐ 742 1.3.a: clear first-login force-change flag after successful password change
        user.MustChangePassword = false;
        await userManager.UpdateAsync(user);

        // Invalidate token cache so next request requires fresh auth
        await cache.RemoveAsync($"user-roles:{cmd.UserId}");
        return Result.Ok();
    }
}
