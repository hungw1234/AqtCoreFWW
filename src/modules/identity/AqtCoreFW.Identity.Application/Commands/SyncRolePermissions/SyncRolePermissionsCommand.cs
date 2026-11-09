using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Identity.Application.Commands.SyncRolePermissions;

[AuditCategory(AuditCategory.AccountManagement)]
public sealed record SyncRolePermissionsCommand(
    Guid RoleId,
    IReadOnlyList<Guid> PermissionIds,
    Guid ActorId,
    Guid TenantId,
    bool Confirmed = false,
    string? Justification = null) : ICommand<SyncRolePermissionsResult>;

/// <summary>Result with optional SoD warnings.</summary>
public sealed class SyncRolePermissionsResult
{
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public bool RequiresConfirmation { get; init; }
}
