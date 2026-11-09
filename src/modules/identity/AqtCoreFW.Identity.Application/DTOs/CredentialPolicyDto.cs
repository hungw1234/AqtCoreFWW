namespace AqtCoreFW.Identity.Application.DTOs;

/// <summary>Read DTO for CredentialPolicy.</summary>
public sealed record CredentialPolicyDto(
    long Id,
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
    int EscalationLockoutCount,
    int EscalationWindowHours,
    bool ShowRemainingAttempts);
