using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.Identity.Application.Signing.Profiles;

/// <summary>
/// Handles <see cref="SetMySigningNationalIdCommand"/>. Sets the caller's own CCCD, guarded by an active-enrollment
/// lock so an already-bound certificate's national id cannot drift. A same-value request is an idempotent no-op
/// (never trips the lock), so re-submitting the unchanged CCCD always succeeds.
/// </summary>
public sealed class SetMySigningNationalIdCommandHandler(
    UserManager<ApplicationUser> userManager,
    ISignerCertEnrollmentRepository enrollmentRepository)
    : IRequestHandler<SetMySigningNationalIdCommand, Result>
{
    public async Task<Result> Handle(SetMySigningNationalIdCommand cmd, CancellationToken ct)
    {
        // AspNetUsers has no tenant query filter / RLS, so FindByIdAsync is cross-tenant resolvable — assert the
        // row belongs to the caller's JWT tenant (mirrors EnrollMySignatureCertCommand / SignerBindingGuard). This
        // also closes a latent lock-bypass: a SystemAdmin using the X-Tenant-Id override against their own id would
        // otherwise query the active-enrollment lock under the wrong tenant and skip it.
        var user = await userManager.FindByIdAsync(cmd.UserId.ToString());
        if (user is null || user.TenantId != cmd.TenantId)
            return Result.Fail(new NotFoundError($"User {cmd.UserId} not found."));

        // Idempotent no-op: the same value never changes the binding, so it must succeed even when locked
        // (re-asserting one's own CCCD is harmless). Short-circuit before the enrollment lookup.
        if (string.Equals(user.SigningNationalId, cmd.NationalId, StringComparison.Ordinal))
            return Result.Ok();

        // Active-enrollment lock: a Pending/Approved enrollment binds a certificate to the current CCCD, so
        // CHANGING an already-set CCCD would orphan that binding. Active-only (GetActiveByUser, not
        // HasAnyEnrollment) — a user with only Rejected/revoked rows may still correct their CCCD and re-enroll.
        // Exception: first-time set (user currently has no CCCD) bypasses the lock — a no-CCCD USB Pending
        // enrollment must not permanently lock the user out of ever setting a CCCD (needed for Remote Mysign).
        // A first-time set cannot contradict any existing cert-CCCD binding because there was none. (RT-1 #3)
        if (!string.IsNullOrWhiteSpace(user.SigningNationalId))
        {
            var active = await enrollmentRepository.GetActiveByUserAsync(cmd.TenantId, cmd.UserId, ct);
            if (active.Count > 0)
                return Result.Fail(new BadRequestError(
                    "Không thể thay đổi CCCD khi đang có chứng thư số hiệu lực. Hãy thu hồi chứng thư trước.",
                    "SIGNING_NATIONAL_ID_LOCKED_BY_ENROLLMENT"));
        }

        user.SigningNationalId = cmd.NationalId;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return Result.Fail(result.Errors.Select(e => new ValidationError(e.Description)));

        return Result.Ok();
    }
}
