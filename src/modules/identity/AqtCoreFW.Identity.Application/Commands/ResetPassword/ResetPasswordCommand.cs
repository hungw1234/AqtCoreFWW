using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using FluentResults;

namespace AqtCoreFW.Identity.Application.Commands.ResetPassword;

/// <summary>Admin-initiated password reset — generates token and emails it to the user (F-25).</summary>
[AuditCategory(AuditCategory.AccountManagement)]
public sealed record ResetPasswordCommand(Guid UserId, Guid ActorId) : ICommand;
