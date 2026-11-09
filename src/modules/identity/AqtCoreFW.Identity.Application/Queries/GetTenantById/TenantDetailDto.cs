using AqtCoreFW.Identity.Domain.ValueObjects;

namespace AqtCoreFW.Identity.Application.Queries.GetTenantById;

/// <summary>Full tenant details for admin view.</summary>
public sealed class TenantDetailDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string? ContactEmail { get; init; }
    public TenantStatus Status { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public Guid CreatedBy { get; init; }
    public Guid? ModifiedBy { get; init; }
}
