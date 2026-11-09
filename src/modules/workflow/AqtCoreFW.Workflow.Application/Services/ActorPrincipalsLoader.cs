using AqtCoreFW.SharedKernel.Contracts.Clients;

namespace AqtCoreFW.Workflow.Application.Services;

/// <summary>
/// Loads a user's four principal sets for transition-authz matching — the SAME methods the
/// inbox/claim path uses (<c>GetInboxQueryHandler</c> / <c>IsPoolEligibleAsync</c>), so authz stays
/// in parity with claim eligibility. Role codes are direct-only (no group-inheritance); org-unit
/// codes are the active temporal set.
/// </summary>
internal static class ActorPrincipalsLoader
{
    public static async Task<ActorPrincipals> LoadAsync(
        IIdentityModuleClient identityClient,
        IOrgUnitModuleClient orgUnitClient,
        Guid userId,
        Guid tenantId,
        CancellationToken ct)
    {
        var permissionCodes = await identityClient.GetEffectivePermissionCodesAsync(userId, ct);
        var roleCodes = await identityClient.GetUserRoleCodesAsync(userId, tenantId, ct);
        var groupCodes = await identityClient.GetUserGroupCodesAsync(userId, tenantId, ct);
        var orgUnitCodes = await orgUnitClient.GetUserOrgUnitCodesAsync(userId, tenantId, ct);
        return new ActorPrincipals(permissionCodes, roleCodes, groupCodes, orgUnitCodes);
    }
}
