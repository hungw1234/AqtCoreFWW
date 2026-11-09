using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Password and lockout policy configuration per tenant.
/// Enforces QĐ742 password complexity and rotation requirements.
/// IsDefault=true means this policy is applied when no explicit policy is assigned.
/// </summary>
public class CredentialPolicy : AuditableEntity, ITenantScoped
{
    public string Name { get; private set; } = string.Empty;
    public int MinLength { get; private set; }
    public int MaxLength { get; private set; }
    public bool RequireUppercase { get; private set; }
    public bool RequireLowercase { get; private set; }
    public bool RequireDigit { get; private set; }
    public bool RequireSpecialChar { get; private set; }

    /// <summary>Days until password expires and rotation is required. 0 = never.</summary>
    public int RotationDays { get; private set; }
    public int MaxFailedAttempts { get; private set; }
    public int LockoutMinutes { get; private set; }

    /// <summary>Number of previous passwords to remember — prevents reuse.</summary>
    public int PasswordHistoryCount { get; private set; }

    /// <summary>When true this policy is the fallback for all users in the tenant.</summary>
    public bool IsDefault { get; private set; }

    public Guid TenantId { get; private set; }

    /// <summary>QĐ 742 1.4.d: Số lần lockout liên tiếp để escalate. Default 3.</summary>
    public int EscalationLockoutCount { get; private set; } = 3;

    /// <summary>QĐ 742 1.4.d: Window đếm lockout (giờ). Default 24.</summary>
    public int EscalationWindowHours { get; private set; } = 24;

    /// <summary>QĐ 742 1.4.b: Hiển thị remaining attempts cho user. Default true.</summary>
    public bool ShowRemainingAttempts { get; private set; } = true;

    private CredentialPolicy() { }

    public static CredentialPolicy Create(
        string name,
        Guid tenantId,
        int minLength,
        int maxLength,
        bool requireUppercase,
        bool requireLowercase,
        bool requireDigit,
        bool requireSpecialChar,
        int rotationDays,
        int maxFailedAttempts,
        int lockoutMinutes,
        int passwordHistoryCount,
        bool isDefault,
        Guid createdBy,
        int escalationLockoutCount = 3,
        int escalationWindowHours = 24,
        bool showRemainingAttempts = true)
    {
        if (minLength < 1)
            throw new ArgumentException("MinLength must be at least 1", nameof(minLength));
        if (minLength > maxLength)
            throw new ArgumentException("MinLength cannot exceed MaxLength", nameof(minLength));

        var policy = new CredentialPolicy
        {
            Name = name,
            TenantId = tenantId,
            MinLength = minLength,
            MaxLength = maxLength,
            RequireUppercase = requireUppercase,
            RequireLowercase = requireLowercase,
            RequireDigit = requireDigit,
            RequireSpecialChar = requireSpecialChar,
            RotationDays = rotationDays,
            MaxFailedAttempts = maxFailedAttempts,
            LockoutMinutes = lockoutMinutes,
            PasswordHistoryCount = passwordHistoryCount,
            IsDefault = isDefault,
            EscalationLockoutCount = escalationLockoutCount,
            EscalationWindowHours = escalationWindowHours,
            ShowRemainingAttempts = showRemainingAttempts
        };
        policy.SetAuditCreate(createdBy);
        return policy;
    }

    public void Update(
        string name,
        int minLength,
        int maxLength,
        bool requireUppercase,
        bool requireLowercase,
        bool requireDigit,
        bool requireSpecialChar,
        int rotationDays,
        int maxFailedAttempts,
        int lockoutMinutes,
        int passwordHistoryCount,
        bool isDefault,
        Guid modifiedBy,
        int escalationLockoutCount = 3,
        int escalationWindowHours = 24,
        bool showRemainingAttempts = true)
    {
        Name = name;
        MinLength = minLength;
        MaxLength = maxLength;
        RequireUppercase = requireUppercase;
        RequireLowercase = requireLowercase;
        RequireDigit = requireDigit;
        RequireSpecialChar = requireSpecialChar;
        RotationDays = rotationDays;
        MaxFailedAttempts = maxFailedAttempts;
        LockoutMinutes = lockoutMinutes;
        PasswordHistoryCount = passwordHistoryCount;
        IsDefault = isDefault;
        EscalationLockoutCount = escalationLockoutCount;
        EscalationWindowHours = escalationWindowHours;
        ShowRemainingAttempts = showRemainingAttempts;
        SetAuditUpdate(modifiedBy);
    }
}
