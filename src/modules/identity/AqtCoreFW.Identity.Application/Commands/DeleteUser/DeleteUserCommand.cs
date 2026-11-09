using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Identity.Application.Commands.DeleteUser;

/// <summary>Soft-delete (deactivate) a user. Sets IsActive=false + locks the account.</summary>
[AuditCategory(AuditCategory.AccountManagement)]
public sealed record DeleteUserCommand(Guid UserId, Guid ActorId) : ICommand;
