using AqtCoreFW.Identity.Application.Services;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.Identity.Application.Mfa;

/// <summary>
/// Completes TOTP enrolment: validates the code against the cached pending secret, persists the secret,
/// then issues the user's initial recovery codes (returned once). Routed through
/// <see cref="ICommand{TResponse}"/> so the enrolment write is audited + validated and the controller
/// no longer persists the recovery-code hash via a service-locator <c>UserManager</c> (ADR-004 gap).
/// </summary>
[AuditCategory(AuditCategory.AccountManagement)]
public sealed record CompleteTotpEnrolCommand(Guid UserId, string TotpCode)
    : ICommand<CompleteTotpEnrolResult>;

/// <summary>The one-time plaintext recovery codes issued on successful enrolment.</summary>
public sealed record CompleteTotpEnrolResult(string[] RecoveryCodes);

public sealed class CompleteTotpEnrolCommandValidator : AbstractValidator<CompleteTotpEnrolCommand>
{
    public CompleteTotpEnrolCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.TotpCode).NotEmpty();
    }
}

public sealed class CompleteTotpEnrolCommandHandler(
    IMfaService mfaService,
    IRecoveryCodeService recoveryCodeService,
    UserManager<ApplicationUser> userManager)
    : IRequestHandler<CompleteTotpEnrolCommand, Result<CompleteTotpEnrolResult>>
{
    public async Task<Result<CompleteTotpEnrolResult>> Handle(
        CompleteTotpEnrolCommand command, CancellationToken ct)
    {
        var enrolled = await mfaService.CompleteTotpEnrolAsync(command.UserId, command.TotpCode);
        if (!enrolled)
            return Result.Fail(new BadRequestError(
                "Invalid or expired TOTP code.", "MFA_ENROL_INVALID_CODE"));

        // Generate and store recovery codes immediately after successful enrolment.
        var (plaintextCodes, hashedJson) = await recoveryCodeService.GenerateAsync();

        // Persist hashed codes to the user record. Mirrors the prior controller's defensive
        // "if user is not null" persist — enrolment already succeeded above, so a vanished user here
        // is unreachable; the recovery codes are still returned to the caller as before.
        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user is not null)
        {
            user.RecoveryCodesHash = hashedJson;
            await userManager.UpdateAsync(user);
        }

        return Result.Ok(new CompleteTotpEnrolResult(plaintextCodes));
    }
}
