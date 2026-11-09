using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// User-owned signature profile aggregate — ties together a display name, optional visual signature
/// images (key references into Files/MinIO storage, written at stamp slice P4/P5), and an optional
/// link to an admin-approved <see cref="SignerCertEnrollment"/>. One profile per user can be the
/// default; the invariant (≤1 default/user) is enforced in the domain + backed by a filtered unique
/// index (defense-in-depth).
///
/// <para>T2 entity: long <see cref="Id"/> (internal, DB-assigned) + Guid <see cref="PublicId"/> AK
/// (exposed in API URLs and DTOs — anti-IDOR). Schema [identity] via IdentityDbContext default.</para>
///
/// <para>Image columns (<see cref="FullSignatureKey"/>, <see cref="InitialSignatureKey"/>) are
/// created nullable now (M2 — additive) and remain null until the stamp slice (P4/P5) writes them
/// via the Files upload pipeline.</para>
///
/// <para>Optimistic concurrency: <see cref="RowVersion"/> (SQL rowversion — EF byte[] concurrency
/// token) guards against lost updates in concurrent profile edits.</para>
/// </summary>
public class UserSignatureProfile : AuditableEntity, ITenantScoped, IPublicIdentified
{
    /// <inheritdoc/>
    public Guid PublicId { get; private set; }

    /// <inheritdoc/>
    public Guid TenantId { get; private set; }

    /// <summary>The user who owns this profile (FK to AspNetUsers.Id).</summary>
    public Guid UserId { get; private set; }

    /// <summary>Human-readable name shown on the PDF stamp and in the profile list (≤128 chars).</summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>
    /// True when this is the user's currently-selected default profile. At most one profile per
    /// (TenantId, UserId) may be default — enforced by domain <see cref="MarkDefault"/> /
    /// <see cref="ClearDefault"/> and backed by a filtered unique index.
    /// </summary>
    public bool IsDefault { get; private set; }

    /// <summary>
    /// Optional FK to the admin-approved <see cref="SignerCertEnrollment"/> (internal long Id).
    /// Nullable — profile may be created before explicit cert enrollment. NoAction delete behaviour
    /// (matches Collab FK pattern; no cascade needed, 0 hard-delete paths in module).
    /// </summary>
    public long? SignerCertEnrollmentId { get; private set; }

    /// <summary>
    /// MinIO/Files storage key for the full handwritten-style signature image (PNG/JPEG).
    /// Null until stamp slice (P4/P5) uploads and records the key (M2 — column additive now).
    /// ≤512 chars (same limit as PAdES storage keys in the module).
    /// </summary>
    public string? FullSignatureKey { get; private set; }

    /// <summary>
    /// MinIO/Files storage key for the initial/paraph signature image (PNG/JPEG).
    /// Null until stamp slice (P4/P5). ≤512 chars.
    /// </summary>
    public string? InitialSignatureKey { get; private set; }

    /// <summary>
    /// Whether the visible PAdES stamp includes the "Serial" line (the signing cert's serial number).
    /// Default true (existing rows keep the prior always-shown behaviour). Toggled per profile so a signer
    /// can suppress the serial on a given stamp. Display-only — never a signing/binding gate.
    /// </summary>
    public bool ShowSerial { get; private set; } = true;

    /// <summary>
    /// Whether the visible PAdES stamp includes the "Nhà CC" line (the signing cert's issuer DN).
    /// Default true (existing rows keep the prior always-shown behaviour). Display-only — never a gate.
    /// </summary>
    public bool ShowIssuer { get; private set; } = true;

    /// <summary>EF Core optimistic concurrency token (SQL rowversion — auto-managed by DB).</summary>
    public byte[]? RowVersion { get; private set; }

    /// <summary>EF Core: private parameterless constructor for materialisation.</summary>
    private UserSignatureProfile() { }

    /// <summary>
    /// Factory — creates a new signature profile for <paramref name="userId"/> in <paramref name="tenantId"/>.
    /// <see cref="PublicId"/> is assigned as a v4 random Guid (timing-insensitive for user-owned entities).
    /// <see cref="IsDefault"/> defaults false; caller must explicitly call <see cref="MarkDefault"/> if
    /// this should be the user's active default.
    /// </summary>
    public static UserSignatureProfile Create(
        IGuidGenerator gen,
        Guid tenantId,
        Guid userId,
        string displayName,
        Guid createdBy,
        bool showSerial = true,
        bool showIssuer = true)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId must not be empty.", nameof(tenantId));
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId must not be empty.", nameof(userId));
        if (createdBy == Guid.Empty)
            throw new ArgumentException("CreatedBy must not be empty.", nameof(createdBy));

        ValidateDisplayName(displayName);

        var profile = new UserSignatureProfile
        {
            // v4 random — user profile id should not leak creation timing
            PublicId    = gen.NewIdRandom(),
            TenantId    = tenantId,
            UserId      = userId,
            DisplayName = displayName.Trim(),
            IsDefault   = false,
            ShowSerial  = showSerial,
            ShowIssuer  = showIssuer,
        };
        profile.SetAuditCreate(createdBy);
        return profile;
    }

    /// <summary>
    /// Renames the profile. Rejects empty or whitespace-only names and names longer than 128 chars.
    /// </summary>
    public void Rename(string displayName, Guid modifiedBy)
    {
        ValidateDisplayName(displayName);
        DisplayName = displayName.Trim();
        SetAuditUpdate(modifiedBy);
    }

    /// <summary>
    /// Sets whether the visible stamp shows the Serial / Nhà CC (issuer) lines. Display-only toggles;
    /// never affect signing or the CMS signature. Idempotent-safe (plain assignment).
    /// </summary>
    public void SetStampVisibility(bool showSerial, bool showIssuer, Guid modifiedBy)
    {
        ShowSerial = showSerial;
        ShowIssuer = showIssuer;
        SetAuditUpdate(modifiedBy);
    }

    /// <summary>
    /// Records the MinIO/Files storage keys for the visual signature images.
    /// Either parameter may be null to clear that image slot. Called by stamp slice (P4/P5).
    /// </summary>
    public void SetImages(string? fullSignatureKey, string? initialSignatureKey, Guid modifiedBy)
    {
        if (fullSignatureKey is { Length: > 512 })
            throw new ArgumentException("FullSignatureKey must not exceed 512 characters.", nameof(fullSignatureKey));
        if (initialSignatureKey is { Length: > 512 })
            throw new ArgumentException("InitialSignatureKey must not exceed 512 characters.", nameof(initialSignatureKey));

        FullSignatureKey    = fullSignatureKey;
        InitialSignatureKey = initialSignatureKey;
        SetAuditUpdate(modifiedBy);
    }

    /// <summary>
    /// Marks this profile as the user's default. Caller is responsible for clearing the prior default
    /// (via <see cref="ClearDefault"/> on the previously-default profile) BEFORE calling this, so at
    /// most one default exists at any moment (invariant enforcement at aggregate level; the filtered
    /// unique index is the DB-level backstop).
    /// </summary>
    public void MarkDefault(Guid modifiedBy)
    {
        if (IsDefault) return; // idempotent
        IsDefault = true;
        SetAuditUpdate(modifiedBy);
    }

    /// <summary>
    /// Clears the default flag on this profile (called on the previously-default profile when another
    /// is being promoted). Idempotent if already not-default.
    /// </summary>
    public void ClearDefault(Guid modifiedBy)
    {
        if (!IsDefault) return; // idempotent
        IsDefault = false;
        SetAuditUpdate(modifiedBy);
    }

    /// <summary>
    /// Links this profile to an approved <see cref="SignerCertEnrollment"/> (internal long Id).
    /// Replaces any previously linked enrollment. The caller must verify the enrollment exists and
    /// belongs to the same (TenantId, UserId) before calling.
    /// </summary>
    public void LinkCert(long enrollmentId, Guid modifiedBy)
    {
        SignerCertEnrollmentId = enrollmentId;
        SetAuditUpdate(modifiedBy);
    }

    // ── Private helpers ───────────────────────────────────────────────────────────────────────

    private static void ValidateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("DisplayName must not be empty.", nameof(displayName));
        if (displayName.Trim().Length > 128)
            throw new ArgumentException("DisplayName must not exceed 128 characters.", nameof(displayName));
    }
}
