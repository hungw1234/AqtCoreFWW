namespace AqtCoreFW.Organization.Infrastructure.Seeding;

/// <summary>
/// Options for the tenant-agnostic <see cref="OrgTreeSeeder"/>. Default OFF; enabled per-environment
/// (e.g. Development + the dev reset flow) where demo data is wanted. MaxLevel acts as a global
/// ceiling over each tenant's own profile depth.
/// </summary>
public class OrgTreeSeederOptions
{
    public const string SectionName = "Organization:OrgTreeSeed";

    /// <summary>Enable/disable seeder execution on startup.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Max hierarchy level to seed (1=Bộ, 2=Vụ/Cục, 3=Chi cục, 4=Đội/placeholder).</summary>
    public int MaxLevel { get; set; } = 4;

    /// <summary>Skip the BTC L4 KBNN treasury ward-placeholder generation step.</summary>
    public bool SkipTreasuryWardPlaceholders { get; set; } = false;
}
