using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.Identity.Application.Commands.UpdateUser;

public sealed class UpdateUserCommandHandler(
    UserManager<ApplicationUser> userManager,
    IOrgUnitModuleClient orgUnitClient,
    ISignerCertEnrollmentRepository enrollmentRepository)
    : IRequestHandler<UpdateUserCommand, Result>
{
    public async Task<Result> Handle(UpdateUserCommand cmd, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(cmd.UserId.ToString());
        if (user is null)
            return Result.Fail(new NotFoundError($"User {cmd.UserId} not found."));

        // PrimaryOrgUnitId is a bare Guid? with no FK, so a User.Update-permitted caller could
        // point it at a cross-tenant or non-existent OrgUnit. Verify it resolves within the user's
        // own tenant — the tenant-scoped lookup omits cross-tenant ids, so a miss is rejected.
        // A null OrgUnitId clears the assignment (full-replace PUT semantics) and skips the check.
        // NOTE: for a system/global user (TenantId is null) the lookup degrades to existence-only
        // (unfiltered) — tenant isolation cannot be asserted here. Downstream blast radius stays
        // bounded: DataScopeService tenant-scopes org-tree expansion at query time, so a foreign id
        // yields a junk/empty scope claim, not cross-tenant data access.
        if (cmd.OrgUnitId is { } orgUnitId)
        {
            var found = await orgUnitClient.GetOrgUnitsByIdsAsync([orgUnitId], user.TenantId, ct);
            if (!found.ContainsKey(orgUnitId))
                return Result.Fail(new ValidationError(
                    $"OrgUnit {orgUnitId} does not exist in the user's tenant."));
        }

        // H5 guard: if the user has any cert enrollment row (any status — including Pending/Rejected/revoked),
        // reject a SigningNationalId change. The full-replace PUT semantics would otherwise silently null
        // out a possession-proven CCCD when an admin edits other fields (e.g. FullName) and leaves
        // SigningNationalId blank. Once enrolled, CCCD is immutable via this path.
        // Note: user.TenantId may be null for system/global users — skip the guard for those.
        if (cmd.SigningNationalId != user.SigningNationalId && user.TenantId.HasValue)
        {
            var hasEnrollment = await enrollmentRepository.HasAnyEnrollmentAsync(
                user.TenantId.Value, user.Id, ct);
            if (hasEnrollment)
                return Result.Fail(new BadRequestError(
                    "Cannot change SigningNationalId while the user has an active cert enrollment. " +
                    "Revoke all enrollments first.",
                    "SIGNING_NATIONAL_ID_LOCKED_BY_ENROLLMENT"));
        }

        user.FullName = cmd.FullName;
        user.DepartmentCode = cmd.DepartmentCode;
        user.PrimaryOrgUnitId = cmd.OrgUnitId;
        user.SigningNationalId = cmd.SigningNationalId;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return Result.Fail(result.Errors.Select(e => new ValidationError(e.Description)));

        return Result.Ok();
    }
}
