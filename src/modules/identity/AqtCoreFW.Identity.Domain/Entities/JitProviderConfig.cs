using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Per-provider JIT SSO provisioning configuration.
/// Scheme is the ASP.NET auth scheme name — unique key matching middleware registration.
/// </summary>
public class JitProviderConfig : AuditableEntity
{
    /// <summary>ASP.NET auth scheme name — unique key, matches middleware registration.</summary>
    public string Scheme { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;
    public ExternalIdentityProvider ProviderType { get; private set; }
    public bool JitEnabled { get; private set; }
    public string DefaultRoleName { get; private set; } = "Viewer";
    public bool RequireApproval { get; private set; }

    /// <summary>JSON object mapping claim types to user profile fields.</summary>
    public string? ClaimMappingJson { get; private set; }

    public Guid? DefaultTenantId { get; private set; }

    /// <summary>JSON string[] of allowed email domains. Null/empty = allow all.</summary>
    public string? AllowedDomainsJson { get; private set; }

    /// <summary>Max JIT provisions per hour per provider. 0 = unlimited.</summary>
    public int MaxProvisionsPerHour { get; private set; }

    public bool IsActive { get; private set; } = true;

    private JitProviderConfig() { }

    public static JitProviderConfig Create(
        string scheme,
        string displayName,
        ExternalIdentityProvider providerType,
        bool jitEnabled,
        string defaultRoleName,
        bool requireApproval,
        string? claimMappingJson,
        Guid? defaultTenantId,
        string? allowedDomainsJson,
        int maxProvisionsPerHour,
        Guid createdBy)
    {
        var entity = new JitProviderConfig
        {
            Scheme = scheme,
            DisplayName = displayName,
            ProviderType = providerType,
            JitEnabled = jitEnabled,
            DefaultRoleName = defaultRoleName,
            RequireApproval = requireApproval,
            ClaimMappingJson = claimMappingJson,
            DefaultTenantId = defaultTenantId,
            AllowedDomainsJson = allowedDomainsJson,
            MaxProvisionsPerHour = maxProvisionsPerHour,
            IsActive = true,
        };
        entity.SetAuditCreate(createdBy);
        return entity;
    }

    public void Update(
        string displayName,
        bool jitEnabled,
        string defaultRoleName,
        bool requireApproval,
        string? claimMappingJson,
        Guid? defaultTenantId,
        string? allowedDomainsJson,
        int maxProvisionsPerHour,
        Guid modifiedBy)
    {
        DisplayName = displayName;
        JitEnabled = jitEnabled;
        DefaultRoleName = defaultRoleName;
        RequireApproval = requireApproval;
        ClaimMappingJson = claimMappingJson;
        DefaultTenantId = defaultTenantId;
        AllowedDomainsJson = allowedDomainsJson;
        MaxProvisionsPerHour = maxProvisionsPerHour;
        SetAuditUpdate(modifiedBy);
    }

    public void Deactivate(Guid modifiedBy)
    {
        IsActive = false;
        SetAuditUpdate(modifiedBy);
    }
}
