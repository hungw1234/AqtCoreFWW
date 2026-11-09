using AqtCoreFW.Identity.Domain.ValueObjects;

namespace AqtCoreFW.Identity.Application.Queries.ListTenantsAdmin;

/// <summary>Tenant summary for admin list view.</summary>
public sealed class TenantAdminDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string? ContactEmail { get; init; }
    public TenantStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }

    /// <summary>Number of users in this tenant (enrichment from AspNetUsers).</summary>
    public int UserCount { get; set; }
}
