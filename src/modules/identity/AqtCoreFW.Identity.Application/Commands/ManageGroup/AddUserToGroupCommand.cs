using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Identity.Application.Commands.ManageGroup;

/// <summary>Add a user to a group. Idempotent — no error if membership already exists.</summary>
[AuditCategory(AuditCategory.AccountManagement)]
public sealed record AddUserToGroupCommand(
    Guid GroupId,
    Guid UserId,
    Guid AddedBy) : ICommand;
