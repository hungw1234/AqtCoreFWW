using Microsoft.AspNetCore.Authorization;

namespace AqtCoreFW.SharedKernel.Authorization;

/// <summary>
/// Authorization requirement satisfied when the current user holds ANY ONE of the listed
/// fine-grained permission codes (OR semantics). Use when a single endpoint is legitimately
/// reachable from two role surfaces that hold different permissions — e.g. a resource a user
/// can reach either as the resource's own editor or as a consumer of a containing workflow.
///
/// Contrast with <see cref="PermissionRequirement"/> (single code). Stacking multiple
/// <c>[Authorize(Policy = code)]</c> attributes is AND, not OR — this requirement is the
/// only way to express OR within the permission model.
/// </summary>
public sealed class AnyPermissionRequirement(params string[] permissionCodes) : IAuthorizationRequirement
{
    public IReadOnlyList<string> PermissionCodes { get; } = permissionCodes;
}
