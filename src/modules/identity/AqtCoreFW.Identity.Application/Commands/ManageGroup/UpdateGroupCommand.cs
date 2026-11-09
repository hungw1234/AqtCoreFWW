using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Identity.Application.Commands.ManageGroup;

/// <summary>Update group name, description, and active status.</summary>
[AuditCategory(AuditCategory.AccountManagement)]
public sealed record UpdateGroupCommand(
    Guid GroupId,
    string Name,
    string? Description,
    bool? IsActive) : ICommand;

/// <summary>Remove a user from a group.</summary>
[AuditCategory(AuditCategory.AccountManagement)]
public sealed record RemoveUserFromGroupCommand(Guid GroupId, Guid UserId) : ICommand;

/// <summary>Remove a role assignment from a group.</summary>
[AuditCategory(AuditCategory.AccountManagement)]
public sealed record RemoveRoleFromGroupCommand(Guid GroupId, Guid RoleId) : ICommand;
