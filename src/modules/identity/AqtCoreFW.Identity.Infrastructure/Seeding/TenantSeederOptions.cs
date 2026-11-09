namespace AqtCoreFW.Identity.Infrastructure.Seeding;

/// <summary>
/// Configuration options for the proactive <see cref="TenantSeeder"/>.
///
/// Default OFF: normal dev startup is unchanged — Tenant rows continue to be created reactively by
/// <see cref="TenantBackfillSeeder"/>. The reseed orchestrator (reset-all.ps1 / aqt-runapp --reseed)
/// flips this on via env override so the canonical, diacritic-correct rows are seeded up front.
/// </summary>
public class TenantSeederOptions
{
    public const string SectionName = "Identity:TenantSeed";

    /// <summary>Enable/disable proactive Tenant-row seeding on startup.</summary>
    public bool Enabled { get; set; } = false;
}
