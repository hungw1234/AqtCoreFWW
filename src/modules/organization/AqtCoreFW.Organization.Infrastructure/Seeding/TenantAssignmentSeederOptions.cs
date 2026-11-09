namespace AqtCoreFW.Organization.Infrastructure.Seeding;

/// <summary>Configuration options for tenant-agnostic assignment + position-history seeding.</summary>
public class TenantAssignmentSeederOptions
{
    public const string SectionName = "Organization:TenantAssignmentSeed";

    /// <summary>
    /// Enable/disable seeder execution on startup. Default OFF (demo/reset data only) — enabled in
    /// Development via appsettings.Development.json and by the reset-all script.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>EF batch size for bulk assignment + history insert.</summary>
    public int BatchSize { get; set; } = 1_000;

    /// <summary>
    /// Per-tenant sanity ceiling for the self-healing orphan purge. If detected orphan count exceeds
    /// this value the purge aborts with InvalidOperationException — that magnitude signals config drift
    /// (wrong DB / wrong tenant) rather than a legitimate cleanup target.
    /// Default 25k = 2× the largest tenant's target user count + buffer.
    /// </summary>
    public int OrphanPurgeCeiling { get; set; } = 25_000;
}
