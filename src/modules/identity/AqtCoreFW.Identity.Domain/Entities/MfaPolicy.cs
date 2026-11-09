using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// MFA factors supported per policy — stored as bitmask int column.
/// Totp=1, EmailOtp=2, Totp|EmailOtp=3.
/// </summary>
[Flags]
public enum MfaFactor
{
    None = 0,
    Totp = 1,
    EmailOtp = 2
}

/// <summary>
/// Per-role MFA enforcement policy (QĐ742 1.6.a).
/// Role is a semantic key matching ApplicationRole.Name — no FK constraint (Name is mutable).
/// Three system-seeded rows: SystemAdmin (mandatory, 0d grace), Admin (mandatory, 7d grace),
/// GovOfficer (optional, 7d grace). Admins can add more rows per role.
/// RowVersion enables optimistic concurrency for concurrent policy updates.
/// </summary>
public class MfaPolicy : AuditableEntity, IPublicIdentified
{
    /// <summary>
    /// Public alternate key for external addressing (admin URLs, DTOs).
    /// Set by factory via IGuidGenerator.NewIdMonotonic(). Never exposed as FK.
    /// </summary>
    public Guid PublicId { get; private set; }

    /// <summary>
    /// Role name this policy applies to. Matches ApplicationRole.Name.
    /// Not a FK — semantic key to tolerate role renames without cascade.
    /// Unique index enforced via EF configuration.
    /// </summary>
    public string Role { get; private set; } = string.Empty;

    /// <summary>
    /// When true, users in this role must enrol MFA within GraceDays or get 403 mfa_required.
    /// SystemAdmin=true, GraceDays=0 (must enrol before policy enable — P1-1 safeguard).
    /// </summary>
    public bool IsMandatory { get; private set; }

    /// <summary>
    /// Days from user creation until MFA becomes blocking. Default 7 (NC-5).
    /// SystemAdmin = 0 (no grace, must enrol before account activation).
    /// </summary>
    public int GraceDays { get; private set; } = 7;

    /// <summary>Allowed MFA factor bitmask for this role (Totp=1, EmailOtp=2).</summary>
    public MfaFactor AllowedFactors { get; private set; } = MfaFactor.Totp;

    /// <summary>EF optimistic concurrency token.</summary>
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    private MfaPolicy() { }

    /// <summary>
    /// Factory — use in command handlers. Generator issues v7 monotonic Guid for PublicId.
    /// </summary>
    public static MfaPolicy Create(
        IGuidGenerator gen,
        string role,
        bool isMandatory,
        int graceDays,
        MfaFactor allowedFactors,
        Guid createdBy)
    {
        if (gen is null)
            throw new ArgumentNullException(nameof(gen));
        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("Role must not be empty.", nameof(role));
        if (graceDays < 0)
            throw new ArgumentException("GraceDays must be non-negative.", nameof(graceDays));

        var policy = new MfaPolicy
        {
            PublicId = gen.NewIdMonotonic(),
            Role = role,
            IsMandatory = isMandatory,
            GraceDays = graceDays,
            AllowedFactors = allowedFactors,
        };
        policy.SetAuditCreate(createdBy);
        return policy;
    }

    /// <summary>Update settings — call SetAuditUpdate from command handler.</summary>
    public void Update(
        bool isMandatory,
        int graceDays,
        MfaFactor allowedFactors,
        Guid modifiedBy)
    {
        if (graceDays < 0)
            throw new ArgumentException("GraceDays must be non-negative.", nameof(graceDays));

        IsMandatory = isMandatory;
        GraceDays = graceDays;
        AllowedFactors = allowedFactors;
        SetAuditUpdate(modifiedBy);
    }
}
