namespace AqtCoreFW.Reporting.Infrastructure.Seeding;

/// <summary>
/// Options for <see cref="DemoReportingSeeder"/>. Bound from config section
/// <c>Reporting:DemoReportingSeed</c>. Default OFF — demo data is opt-in per environment
/// (reset-all.ps1 flips it via env override; never on by default in prod).
/// </summary>
public sealed class DemoReportingSeederOptions
{
    /// <summary>Config section path.</summary>
    public const string SectionName = "Reporting:DemoReportingSeed";

    /// <summary>When false (default) the seeder is a no-op.</summary>
    public bool Enabled { get; set; }
}
