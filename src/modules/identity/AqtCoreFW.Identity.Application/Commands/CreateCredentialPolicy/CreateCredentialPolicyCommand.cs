using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Identity.Application.Commands.CreateCredentialPolicy;

[AuditCategory(AuditCategory.ConfigChange)]
public sealed record CreateCredentialPolicyCommand(
    string Name,
    Guid TenantId,
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
    bool ShowRemainingAttempts = true) : ICommand<long>;
