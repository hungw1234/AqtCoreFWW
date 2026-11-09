using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Identity.Application.Commands.ManageGroup;

/// <summary>Assign a role to a group. Idempotent — no error if assignment already exists.</summary>
[AuditCategory(AuditCategory.AccountManagement)]
public sealed record AssignRoleToGroupCommand(
    Guid GroupId,
    Guid RoleId,
    Guid AssignedBy) : ICommand;
