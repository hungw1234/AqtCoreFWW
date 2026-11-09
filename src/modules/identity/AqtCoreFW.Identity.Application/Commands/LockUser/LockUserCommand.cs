using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using FluentResults;

namespace AqtCoreFW.Identity.Application.Commands.LockUser;

[AuditCategory(AuditCategory.AccountManagement)]
public sealed record LockUserCommand(Guid UserId, bool Lock, Guid ActorId) : ICommand;
