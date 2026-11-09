using Microsoft.AspNetCore.Authorization;

namespace AqtCoreFW.SharedKernel.Authorization;

/// <summary>
/// Authorization requirement that checks whether the current user holds
/// a specific fine-grained permission code (e.g. "HOSO.HOSO.APPROVE").
/// Evaluated by <see cref="PermissionAuthorizationHandler"/>.
/// </summary>
public sealed class PermissionRequirement(string permissionCode) : IAuthorizationRequirement
{
    public string PermissionCode { get; } = permissionCode;
}
