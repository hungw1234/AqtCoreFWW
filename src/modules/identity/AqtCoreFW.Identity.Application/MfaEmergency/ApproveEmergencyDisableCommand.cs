using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.Identity.Application.MfaEmergency;

/// <summary>
/// Second-admin approval for an MFA emergency disable request (QĐ742 1.6).
/// On approval: dual-clear per ADR-007 — removes AspNetUserTokens AuthenticatorKey
/// (Identity primary store) AND clears TotpSecret/TotpEnrolledAt/RecoveryCodesHash/MfaGraceUntil
/// (encrypted backup columns) AND sets TwoFactorEnabled=false so SignInManager skips the MFA branch.
/// Approver MUST differ from requester (enforced here, not in controller).
/// Approval notification: Critical audit log entry only (IEmailSender deferred to phase-07b).
/// </summary>
public sealed record ApproveEmergencyDisableCommand(
    long RequestId,
    bool Approve,
    string? RejectionNote = null) : IRequest<Result>;

public sealed class ApproveEmergencyDisableCommandValidator
    : AbstractValidator<ApproveEmergencyDisableCommand>
{
    public ApproveEmergencyDisableCommandValidator()
    {
        RuleFor(x => x.RequestId).GreaterThan(0);
    }
}

public sealed class ApproveEmergencyDisableCommandHandler(
    IMfaEmergencyDisableRequestRepository repository,
    UserManager<ApplicationUser> userManager,
    IAuditLogger auditLogger,
    ICurrentUser currentUser)
    : IRequestHandler<ApproveEmergencyDisableCommand, Result>
{
    public async Task<Result> Handle(ApproveEmergencyDisableCommand request, CancellationToken ct)
    {
        var disableRequest = await repository.GetByIdAsync(request.RequestId, ct);
        if (disableRequest is null)
            return Result.Fail(new NotFoundError(
                $"Emergency disable request {request.RequestId} not found."));

        // Approver must be a different admin than the requester (2-admin control per QĐ742 1.6).
        if (disableRequest.RequestedBy == currentUser.UserId)
            return Result.Fail(new ForbiddenError(
                "Approver must be different from the requester."));

        if (!request.Approve)
        {
            disableRequest.Reject(currentUser.UserId);
            await repository.UpdateAsync(disableRequest, ct);

            await auditLogger.LogCommandAsync(
                action: "MFA_EMERGENCY_DISABLE_REJECTED",
                moduleName: "Identity",
                resourceType: "MfaEmergencyDisableRequest",
                resourceId: disableRequest.Id.ToString(),
                commandType: typeof(ApproveEmergencyDisableCommand),
                cancellationToken: ct);

            return Result.Ok();
        }

        // Approve — clear all MFA state from target user.
        var targetUser = await userManager.FindByIdAsync(disableRequest.TargetUserId.ToString());
        if (targetUser is null)
            return Result.Fail(new NotFoundError(
                $"Target user {disableRequest.TargetUserId} not found."));

        // Dual-clear per ADR-007: AspNetUserTokens (Identity-native primary store) + TotpSecret (encrypted backup).
        // RemoveAuthenticationTokenAsync deletes the AspNetUserTokens row so
        // UserManager.GetAuthenticatorKeyAsync returns null and SignInManager.TwoFactorAuthenticatorSignInAsync fails.
        // (We deliberately do NOT use ResetAuthenticatorKeyAsync, which generates a new random key — emergency disable should leave nothing usable.)
        const string authenticatorStoreLoginProvider = "[AspNetUserStore]";
        const string authenticatorKeyTokenName = "AuthenticatorKey";
        var removeKeyResult = await userManager.RemoveAuthenticationTokenAsync(
            targetUser, authenticatorStoreLoginProvider, authenticatorKeyTokenName);
        if (!removeKeyResult.Succeeded)
        {
            var errors = string.Join(", ", removeKeyResult.Errors.Select(e => e.Description));
            return Result.Fail(new BadRequestError(
                $"Failed to remove authenticator key: {errors}", ErrorCodes.System.InternalError));
        }

        var disable2FaResult = await userManager.SetTwoFactorEnabledAsync(targetUser, false);
        if (!disable2FaResult.Succeeded)
        {
            var errors = string.Join(", ", disable2FaResult.Errors.Select(e => e.Description));
            return Result.Fail(new BadRequestError(
                $"Failed to disable 2FA flag: {errors}", ErrorCodes.System.InternalError));
        }

        targetUser.TotpSecret = null;
        targetUser.TotpEnrolledAt = null;
        targetUser.RecoveryCodesHash = null;
        targetUser.MfaGraceUntil = null;

        var updateResult = await userManager.UpdateAsync(targetUser);
        if (!updateResult.Succeeded)
        {
            var errors = string.Join(", ", updateResult.Errors.Select(e => e.Description));
            return Result.Fail(new BadRequestError(
                $"Failed to clear MFA for user: {errors}", ErrorCodes.System.InternalError));
        }

        disableRequest.Approve(currentUser.UserId);
        await repository.UpdateAsync(disableRequest, ct);

        // Critical audit entry — satisfies notification requirement for phase-03.
        // Email notification via IEmailSender deferred to phase-07b.
        await auditLogger.LogCommandAsync(
            action: "MFA_EMERGENCY_DISABLE_APPROVED",
            moduleName: "Identity",
            resourceType: "MfaEmergencyDisableRequest",
            resourceId: disableRequest.Id.ToString(),
            commandType: typeof(ApproveEmergencyDisableCommand),
            cancellationToken: ct);

        return Result.Ok();
    }
}
