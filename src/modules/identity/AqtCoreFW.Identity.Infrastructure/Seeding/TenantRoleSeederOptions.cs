namespace AqtCoreFW.Identity.Infrastructure.Seeding;

/// <summary>
/// Options for <see cref="TenantRoleSeeder"/>. Default OFF — enabled per-environment (Development +
/// the dev reset flow) where demo data is wanted; production inherits the default.
/// </summary>
public sealed class TenantRoleSeederOptions
{
    public const string SectionName = "Identity:TenantRoleSeed";

    /// <summary>Enable/disable role seeding on startup.</summary>
    public bool Enabled { get; set; } = false;
}
