using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Identity.Application.Commands.UpdateCredentialPolicy;

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record UpdateCredentialPolicyCommand(
    long Id,
    string Name,
    int MinLength,
    int MaxLength,
    bool RequireUppercase,
    bool RequireLowercase,
    bool RequireDigit,
    bool RequireSpecialChar,
    int RotationDays,
    int MaxFailedAttempts,
    int LockoutMinutes,
    int PasswordHistoryCount,
    bool IsDefault,
    Guid ActorId,
    int EscalationLockoutCount = 3,
    int EscalationWindowHours = 24,
    bool ShowRemainingAttempts = true) : ICommand;
