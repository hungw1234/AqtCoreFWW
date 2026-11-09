using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using FluentResults;

namespace AqtCoreFW.Identity.Application.Commands.AssignRole;

[AuditCategory(AuditCategory.AccountManagement)]
public sealed record AssignRoleCommand(
    Guid UserId,
    string RoleName,
    Guid AssignedBy) : ICommand;
