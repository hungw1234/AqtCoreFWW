using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.Identity.Application.Mfa;

/// <summary>
/// Unenrols TOTP for the current user: clears the encrypted secret, the enrolment timestamp, and the
/// recovery-code hashes. A security-sensitive account write (QĐ 742 §1.6.b) — routed through
/// <see cref="ICommand"/> so it is audited and validated, replacing the controller's prior
/// service-locator <c>UserManager</c> mutation that produced no audit row (ADR-004 gap).
/// </summary>
[AuditCategory(AuditCategory.AccountManagement)]
public sealed record UnenrolTotpCommand(Guid UserId) : ICommand;

public sealed class UnenrolTotpCommandHandler(UserManager<ApplicationUser> userManager)
    : IRequestHandler<UnenrolTotpCommand, Result>
{
    public async Task<Result> Handle(UnenrolTotpCommand command, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user is null)
            return Result.Fail(new NotFoundError($"User {command.UserId} not found."));

        user.TotpSecret = null;
        user.TotpEnrolledAt = null;
        user.RecoveryCodesHash = null;

        var result = await userManager.UpdateAsync(user);
        return result.Succeeded
            ? Result.Ok()
            : Result.Fail(new BadRequestError(
                "Failed to unenrol TOTP: " + string.Join(", ", result.Errors.Select(e => e.Description)),
                "MFA_UNENROL_FAILED"));
    }
}
