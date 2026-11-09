using AqtCoreFW.Identity.Application.Services;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.Identity.Application.Mfa;

/// <summary>
/// Invalidates all existing recovery codes and issues 16 fresh ones for the current user. The plaintext
/// codes are returned ONCE — the caller must prompt the user to save them. A sensitive account write
/// (it replaces the MFA backup factor) — routed through <see cref="ICommand{TResponse}"/> so it is
/// audited and validated, replacing the controller's prior service-locator mutation (ADR-004 gap).
/// </summary>
[AuditCategory(AuditCategory.AccountManagement)]
public sealed record RegenerateRecoveryCodesCommand(Guid UserId)
    : ICommand<RegenerateRecoveryCodesResult>;

/// <summary>The freshly generated plaintext recovery codes — shown once, never re-exposed.</summary>
public sealed record RegenerateRecoveryCodesResult(string[] RecoveryCodes);

public sealed class RegenerateRecoveryCodesCommandHandler(
    UserManager<ApplicationUser> userManager,
    IRecoveryCodeService recoveryCodeService)
    : IRequestHandler<RegenerateRecoveryCodesCommand, Result<RegenerateRecoveryCodesResult>>
{
    public async Task<Result<RegenerateRecoveryCodesResult>> Handle(
        RegenerateRecoveryCodesCommand command, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user is null)
            return Result.Fail(new NotFoundError($"User {command.UserId} not found."));

        var (plaintextCodes, hashedJson) = await recoveryCodeService.GenerateAsync();
        user.RecoveryCodesHash = hashedJson;

        var result = await userManager.UpdateAsync(user);
        return result.Succeeded
            ? Result.Ok(new RegenerateRecoveryCodesResult(plaintextCodes))
            : Result.Fail(new BadRequestError(
                "Failed to regenerate recovery codes: " +
                string.Join(", ", result.Errors.Select(e => e.Description)),
                "MFA_RECOVERY_REGEN_FAILED"));
    }
}
