using System.Text.Json;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.AspNetCore.Identity;

namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Extended IdentityUser — teams add custom fields via ExtraProperties without forking.
/// Convention: prefix ExtraProperties keys with module name, e.g. "hr.employee_code".
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    // IdentityUser inherited PII properties — shadowed to apply DataClassification attributes.
    [DataClassification(DataClassificationLevel.Confidential)]
    public override string? Email { get => base.Email; set => base.Email = value; }

    [DataClassification(DataClassificationLevel.Confidential)]
    public override string? PhoneNumber { get => base.PhoneNumber; set => base.PhoneNumber = value; }

    [DataClassification(DataClassificationLevel.Restricted)]
    [DoNotEncryptAtRest("ASP.NET Core Identity manages PasswordHash internally; " +
        "re-encrypting would break the Identity pipeline (verify, update, lockout).")]
    public override string? PasswordHash { get => base.PasswordHash; set => base.PasswordHash = value; }

    [DataClassification(DataClassificationLevel.Restricted)]
    [DoNotEncryptAtRest("SecurityStamp is written on every security event; " +
        "encrypting creates Vault round-trips per write and breaks concurrency detection.")]
    public override string? SecurityStamp { get => base.SecurityStamp; set => base.SecurityStamp = value; }

    [DataClassification(DataClassificationLevel.Internal)]
    public override string? ConcurrencyStamp { get => base.ConcurrencyStamp; set => base.ConcurrencyStamp = value; }

    /// <summary>IdentityUser.UserName — shadowed to apply DataClassification.</summary>
    [DataClassification(DataClassificationLevel.Confidential)]
    public override string? UserName { get => base.UserName; set => base.UserName = value; }

    [DataClassification(DataClassificationLevel.Confidential)]
    public string FullName { get; set; } = string.Empty;

    /// <summary>Department code — used by ABAC DepartmentAccessHandler.</summary>
    [DataClassification(DataClassificationLevel.Confidential)]
    public string? DepartmentCode { get; set; }

    /// <summary>Data classification clearance per QĐ742.</summary>
    public ClassificationLevel ClearanceLevel { get; set; } = ClassificationLevel.Internal;

    /// <summary>Soft-delete / admin lock flag.</summary>
    public bool IsActive { get; set; } = true;

    public Guid? TenantId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }

    /// <summary>Password expiry enforcement per QĐ742 password policy.</summary>
    public DateTime? PasswordExpiresAt { get; set; }

    /// <summary>HR employee code, e.g. "CB001234". Cross-module reference — no FK constraint.</summary>
    [DataClassification(DataClassificationLevel.Confidential)]
    public string? EmployeeCode { get; set; }

    /// <summary>
    /// Signer's national ID (CCCD, 12 digits) used as the CA-side <c>user_id</c> for remote
    /// signing providers (Viettel Mysign). Distinct from the internal user Guid: the Guid stays
    /// the audit actor, this maps the person to their certificate at the CA. Confidential PII
    /// (QĐ742) — encrypted at rest via EncryptedStringConverter.
    /// </summary>
    [DataClassification(DataClassificationLevel.Confidential)]
    public string? SigningNationalId { get; set; }

    /// <summary>Primary organizational unit (cross-module ref to organization.OrgUnit — no nav property).</summary>
    public Guid? PrimaryOrgUnitId { get; set; }

    /// <summary>Display position/title text, e.g. "Chuyên viên xử lý hồ sơ".</summary>
    public string? PositionName { get; set; }

    /// <summary>Authentication source: LOCAL | AD | VNEID | LDAP. Defaults to LOCAL.</summary>
    public string AuthSource { get; set; } = "LOCAL";

    /// <summary>QĐ 742 1.3.a: Force password change on next login.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>QĐ 742 1.4.d: Số lần lockout liên tiếp trong window. Reset khi login thành công.</summary>
    public int ConsecutiveLockoutCount { get; set; }

    /// <summary>QĐ 742 1.4.d: Timestamp của lockout gần nhất (window calculation).</summary>
    public DateTime? LastLockoutAt { get; set; }

    /// <summary>QĐ 742 1.4.d: Set khi escalation hit; account vĩnh viễn lock cho đến admin unlock.</summary>
    public DateTime? AutoDisabledAt { get; set; }

    // --- QĐ 742 §4.4 Email hash columns (Wave-3, Fixup P0-1: two-column hash split) ---

    /// <summary>
    /// HMAC-SHA-256 of <c>email.ToLowerInvariant().Trim()</c> with Guid.Empty pepper (global sentinel).
    /// Used for cross-tenant login lookup in <c>EmailHashedUserStore.FindByEmailAsync</c>.
    /// Populated by <c>EmailHashInterceptor</c> on every SaveChanges that touches Email.
    /// NULL until backfill completes for existing rows.
    /// Non-reversible hash derivative — classified Internal (not Restricted).
    /// </summary>
    [DataClassification(DataClassificationLevel.Internal)]
    public string? EmailHashGlobal { get; set; }

    /// <summary>
    /// HMAC-SHA-256 of <c>normalizedEmail.ToLowerInvariant().Trim()</c> with Guid.Empty pepper.
    /// Primary login lookup key — carries unique index (replaces broken NormalizedEmail unique index).
    /// Non-reversible hash derivative — classified Internal (not Restricted).
    /// </summary>
    [DataClassification(DataClassificationLevel.Internal)]
    public string? NormalizedEmailHashGlobal { get; set; }

    /// <summary>
    /// HMAC-SHA-256 of <c>email</c> canonical form with per-tenant pepper.
    /// NULL when <c>TenantId</c> is null or Guid.Empty (global/system users have no tenant scope).
    /// For future admin/audit scoped queries — NOT used in the login path.
    /// Non-reversible hash derivative — classified Internal (not Restricted).
    /// </summary>
    [DataClassification(DataClassificationLevel.Internal)]
    public string? EmailHashTenant { get; set; }

    /// <summary>
    /// HMAC-SHA-256 of <c>normalizedEmail</c> canonical form with per-tenant pepper.
    /// NULL when <c>TenantId</c> is null or Guid.Empty.
    /// Non-reversible hash derivative — classified Internal (not Restricted).
    /// </summary>
    [DataClassification(DataClassificationLevel.Internal)]
    public string? NormalizedEmailHashTenant { get; set; }

    // --- QĐ 742 1.6 MFA fields (Phase-03 — added from scratch per amendment) ---

    /// <summary>
    /// AES-256-GCM encrypted TOTP base32 secret.
    /// Null until user completes TOTP enrolment flow (BeginTotpEnrol → CompleteTotpEnrol).
    /// Stored encrypted at rest via EncryptedStringConverter.
    /// </summary>
    [DataClassification(DataClassificationLevel.Restricted)]
    public string? TotpSecret { get; set; }

    /// <summary>UTC timestamp when TOTP was successfully enrolled.</summary>
    public DateTime? TotpEnrolledAt { get; set; }

    /// <summary>
    /// JSON array of BCrypt-hashed one-time recovery codes (16 codes).
    /// Null until enrolment. Regenerated atomically (old batch invalidated).
    /// Stored as nvarchar(max) — never log plaintext codes.
    /// BCrypt verify requires equality comparison on the stored hash value; encrypting would
    /// require decryption before every verify call — that pattern defeats the purpose of BCrypt
    /// (slow-hash) and would add a Vault round-trip to every recovery-code use.
    /// </summary>
    [DataClassification(DataClassificationLevel.Restricted)]
    [DoNotEncryptAtRest("BCrypt recovery codes verified by equality; re-encrypting requires " +
        "decrypt-before-verify on every use, adding Vault latency to the hot path and defeating BCrypt hardening.")]
    public string? RecoveryCodesHash { get; set; }

    /// <summary>
    /// UTC deadline for grace period. After this timestamp, MFA-mandatory users are blocked (403 mfa_required).
    /// Computed from CreatedAtUtc + MfaPolicy.GraceDays when policy is applied.
    /// Null for users not yet subject to a mandatory policy.
    /// </summary>
    public DateTime? MfaGraceUntil { get; set; }

    /// <summary>
    /// Extension point: teams add custom fields without forking ApplicationUser.
    /// Key = "module.field_name", Value = JSON-serialized value.
    /// EF Core serializes via value converter (nvarchar(max)).
    /// </summary>
    public Dictionary<string, JsonElement> ExtraProperties { get; set; } = new();

    // Navigation
    public ICollection<UserDelegation> DelegationsAsDelegate { get; set; } = new List<UserDelegation>();
    public ICollection<UserDelegation> DelegationsAsDelegator { get; set; } = new List<UserDelegation>();
}
