namespace AqtCoreFW.Organization.Infrastructure.Seeding.Models;

/// <summary>POCO for JSON seed data deserialization.</summary>
public sealed class OrgUnitSeedDto
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public required string NameEn { get; init; }
    public string? ParentCode { get; init; }
    public required int Level { get; init; }
    public string? ExternalCode { get; init; }
}
