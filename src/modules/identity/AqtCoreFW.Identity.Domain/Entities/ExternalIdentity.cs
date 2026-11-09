using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>External identity provider link for SSO/LDAP/VNeID/OAuth/SAML federation.</summary>
public enum ExternalIdentityProvider
{
    SSO = 1,
    LDAP = 2,
    VNeID = 3,
    OAuth = 4,
    SAML = 5
}

/// <summary>
/// Links an ApplicationUser to an external identity provider account.
/// One user may have multiple external identities (one per provider).
/// </summary>
public class ExternalIdentity : AuditableEntity
{
    [DataClassification(DataClassificationLevel.Internal)]
    public Guid UserId { get; private set; }
    public ExternalIdentityProvider Provider { get; private set; }

    /// <summary>Unique identifier in the external system (subject claim, DN, etc.).</summary>
    [DataClassification(DataClassificationLevel.Confidential)]
    public string ExternalId { get; private set; } = string.Empty;

    [DataClassification(DataClassificationLevel.Confidential)]
    public string? DisplayName { get; private set; }
    [DataClassification(DataClassificationLevel.Confidential)]
    public string? Email { get; private set; }
    public DateTime LinkedAt { get; private set; }
    public DateTime? LastSyncAt { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>Arbitrary provider-specific metadata stored as JSON.</summary>
    public string? Metadata { get; private set; }

    // Navigation
    public ApplicationUser User { get; private set; } = null!;

    private ExternalIdentity() { }

    public static ExternalIdentity Create(
        Guid userId,
        ExternalIdentityProvider provider,
        string externalId,
        string? displayName,
        string? email,
        Guid createdBy)
    {
        var entity = new ExternalIdentity
        {
            UserId = userId,
            Provider = provider,
            ExternalId = externalId,
            DisplayName = displayName,
            Email = email,
            LinkedAt = DateTime.UtcNow,
            IsActive = true
        };
        entity.SetAuditCreate(createdBy);
        return entity;
    }

    public void UpdateSync(string? displayName, string? email, string? metadata, Guid modifiedBy)
    {
        DisplayName = displayName;
        Email = email;
        Metadata = metadata;
        LastSyncAt = DateTime.UtcNow;
        SetAuditUpdate(modifiedBy);
    }

    public void Deactivate(Guid modifiedBy)
    {
        IsActive = false;
        SetAuditUpdate(modifiedBy);
    }
}
