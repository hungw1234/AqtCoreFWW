namespace AqtCoreFW.Identity.Application.DTOs;

/// <summary>Read DTO for JitProviderConfig. Uses class (not record) for Dapper compatibility.</summary>
public class JitProviderConfigDto
{
    public long Id { get; set; }
    public string Scheme { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int ProviderType { get; set; }
    public bool JitEnabled { get; set; }
    public string DefaultRoleName { get; set; } = string.Empty;
    public bool RequireApproval { get; set; }
    public string? ClaimMappingJson { get; set; }
    public Guid? DefaultTenantId { get; set; }
    public string? AllowedDomainsJson { get; set; }
    public int MaxProvisionsPerHour { get; set; }
    public bool IsActive { get; set; }
}
